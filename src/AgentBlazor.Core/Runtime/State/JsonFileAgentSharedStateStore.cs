using System.Collections.Concurrent;
using System.Text.Json;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.State;

internal sealed class JsonFileAgentSharedStateStore : IAgentSharedStateStore, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SessionState>> _stateByAgent =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SharedStateOptions _options;
    private readonly ILogger<JsonFileAgentSharedStateStore>? _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileWriteLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private sealed record SharedStateStoreSnapshot(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SessionStateRecord>> StateByAgent);

    private sealed record SessionStateRecord(
        string? LatestRunId,
        IReadOnlyDictionary<string, RunStateRecord> Runs,
        IReadOnlyDictionary<string, string> MessageToRun);

    private sealed record RunStateRecord(
        IReadOnlyDictionary<string, string> Values,
        DateTimeOffset UpdatedAt);

    public JsonFileAgentSharedStateStore(
        string filePath,
        IOptions<SharedStateOptions>? options = null,
        ILogger<JsonFileAgentSharedStateStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = Path.GetFullPath(filePath);
        _options = options?.Value ?? new SharedStateOptions();
        _logger = logger;

        LoadSnapshot();
    }

    public AgentSharedStateSnapshot GetSnapshot(
        string agentName,
        string sessionId,
        string? runId = null)
    {
        Validate(agentName, sessionId);

        if (!TryGetSession(agentName, sessionId, out var session))
        {
            return EmptySnapshot(agentName, sessionId, runId);
        }

        lock (session.Gate)
        {
            var resolvedRunId = ResolveRunId(session, runId);
            if (!session.Runs.TryGetValue(resolvedRunId, out var runState))
            {
                return EmptySnapshot(agentName, sessionId, resolvedRunId);
            }

            return new AgentSharedStateSnapshot(
                agentName,
                sessionId,
                resolvedRunId,
                Clone(runState.Values),
                runState.UpdatedAt);
        }
    }

    public AgentSharedStateSnapshot SaveSnapshot(
        string agentName,
        string sessionId,
        string runId,
        IReadOnlyDictionary<string, string> values,
        DateTimeOffset? updatedAt = null)
    {
        Validate(agentName, sessionId, runId);
        ArgumentNullException.ThrowIfNull(values);

        var session = GetOrCreateSession(agentName, sessionId);
        var timestamp = updatedAt ?? DateTimeOffset.UtcNow;

        AgentSharedStateSnapshot snapshot;
        lock (session.Gate)
        {
            if (session.Runs.TryGetValue(runId, out var existing) &&
                ShouldRejectStale(timestamp, existing.UpdatedAt))
            {
                snapshot = new AgentSharedStateSnapshot(
                    agentName,
                    sessionId,
                    runId,
                    Clone(existing.Values),
                    existing.UpdatedAt);
            }
            else
            {
                session.Runs[runId] = new RunState(Clone(values), timestamp);
                session.LatestRunId = runId;
                snapshot = new AgentSharedStateSnapshot(
                    agentName,
                    sessionId,
                    runId,
                    Clone(values),
                    timestamp);
            }
        }

        PersistSnapshot();
        return snapshot;
    }

    public AgentSharedStateDelta ApplyDelta(
        string agentName,
        string sessionId,
        string runId,
        IReadOnlyDictionary<string, string?> changes,
        DateTimeOffset? updatedAt = null)
    {
        Validate(agentName, sessionId, runId);
        ArgumentNullException.ThrowIfNull(changes);

        var session = GetOrCreateSession(agentName, sessionId);
        var timestamp = updatedAt ?? DateTimeOffset.UtcNow;

        AgentSharedStateDelta delta;
        lock (session.Gate)
        {
            if (!session.Runs.TryGetValue(runId, out var runState))
            {
                runState = new RunState(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    DateTimeOffset.MinValue);
                session.Runs[runId] = runState;
            }

            if (ShouldRejectStale(timestamp, runState.UpdatedAt))
            {
                delta = new AgentSharedStateDelta(
                    agentName,
                    sessionId,
                    runId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                    runState.UpdatedAt);
            }
            else
            {
                foreach (var (key, value) in changes)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (value is null)
                    {
                        runState.Values.Remove(key);
                    }
                    else
                    {
                        runState.Values[key] = value;
                    }
                }

                runState.UpdatedAt = timestamp;
                session.LatestRunId = runId;
                delta = new AgentSharedStateDelta(
                    agentName,
                    sessionId,
                    runId,
                    CloneNullable(changes),
                    timestamp);
            }
        }

        PersistSnapshot();
        return delta;
    }

    public void AssociateMessageWithRun(
        string agentName,
        string sessionId,
        string messageId,
        string runId)
    {
        Validate(agentName, sessionId, runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var session = GetOrCreateSession(agentName, sessionId);
        lock (session.Gate)
        {
            session.MessageToRun[messageId] = runId;
        }

        PersistSnapshot();
    }

    public string? GetRunIdForMessage(
        string agentName,
        string sessionId,
        string messageId)
    {
        Validate(agentName, sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        if (!TryGetSession(agentName, sessionId, out var session))
        {
            return null;
        }

        lock (session.Gate)
        {
            return session.MessageToRun.TryGetValue(messageId, out var runId)
                ? runId
                : null;
        }
    }

    public IReadOnlyList<string> GetRunIdsForSession(
        string agentName,
        string sessionId)
    {
        Validate(agentName, sessionId);

        if (!TryGetSession(agentName, sessionId, out var session))
        {
            return [];
        }

        lock (session.Gate)
        {
            return session.Runs
                .OrderByDescending(static pair => pair.Value.UpdatedAt)
                .Select(static pair => pair.Key)
                .ToArray();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileWriteLock.Dispose();
    }

    private bool ShouldRejectStale(DateTimeOffset incoming, DateTimeOffset current)
        => _options.MergeMode == SharedStateMergeMode.RejectStaleWrites && incoming < current;

    private SessionState GetOrCreateSession(string agentName, string sessionId)
    {
        var bySession = _stateByAgent.GetOrAdd(
            agentName,
            static _ => new ConcurrentDictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase));
        return bySession.GetOrAdd(sessionId, static _ => new SessionState());
    }

    private bool TryGetSession(
        string agentName,
        string sessionId,
        out SessionState session)
    {
        session = null!;
        if (!_stateByAgent.TryGetValue(agentName, out var bySession))
        {
            return false;
        }

        if (bySession.TryGetValue(sessionId, out var resolved) && resolved is not null)
        {
            session = resolved;
            return true;
        }

        return false;
    }

    private static string ResolveRunId(SessionState session, string? requestedRunId)
    {
        if (!string.IsNullOrWhiteSpace(requestedRunId))
        {
            return requestedRunId;
        }

        if (!string.IsNullOrWhiteSpace(session.LatestRunId))
        {
            return session.LatestRunId!;
        }

        return "latest";
    }

    private static AgentSharedStateSnapshot EmptySnapshot(
        string agentName,
        string sessionId,
        string? runId)
    {
        return new AgentSharedStateSnapshot(
            agentName,
            sessionId,
            string.IsNullOrWhiteSpace(runId) ? "latest" : runId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
    }

    private static Dictionary<string, string> Clone(IReadOnlyDictionary<string, string> source)
    {
        return source.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> CloneNullable(IReadOnlyDictionary<string, string?> source)
    {
        return source.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static void Validate(
        string agentName,
        string sessionId,
        string? runId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (runId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        }
    }

    private void LoadSnapshot()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_filePath))
            {
                return;
            }

            using var stream = File.OpenRead(_filePath);
            var snapshot = JsonSerializer.Deserialize<SharedStateStoreSnapshot>(stream, SerializerOptions);
            if (snapshot?.StateByAgent is null || snapshot.StateByAgent.Count == 0)
            {
                return;
            }

            foreach (var (agentName, sessions) in snapshot.StateByAgent)
            {
                if (string.IsNullOrWhiteSpace(agentName))
                {
                    continue;
                }

                var agentSessions = _stateByAgent.GetOrAdd(
                    agentName,
                    static _ => new ConcurrentDictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase));

                foreach (var (sessionId, sessionRecord) in sessions)
                {
                    if (string.IsNullOrWhiteSpace(sessionId) || sessionRecord is null)
                    {
                        continue;
                    }

                    var session = new SessionState
                    {
                        LatestRunId = sessionRecord.LatestRunId
                    };

                    foreach (var (runId, runRecord) in sessionRecord.Runs)
                    {
                        if (string.IsNullOrWhiteSpace(runId) || runRecord is null)
                        {
                            continue;
                        }

                        session.Runs[runId] = new RunState(
                            Clone(runRecord.Values),
                            runRecord.UpdatedAt);
                    }

                    foreach (var (messageId, runId) in sessionRecord.MessageToRun)
                    {
                        if (!string.IsNullOrWhiteSpace(messageId) &&
                            !string.IsNullOrWhiteSpace(runId))
                        {
                            session.MessageToRun[messageId] = runId;
                        }
                    }

                    agentSessions[sessionId] = session;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load shared-state snapshot from {Path}", _filePath);
        }
    }

    private void PersistSnapshot() => PersistSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();

    private async Task PersistSnapshotAsync(CancellationToken cancellationToken)
    {
        var lockTaken = false;
        try
        {
            await _fileWriteLock.WaitAsync(cancellationToken);
            lockTaken = true;

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = new SharedStateStoreSnapshot(BuildSerializableSnapshot());
            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist shared-state snapshot to {Path}", _filePath);
        }
        finally
        {
            if (lockTaken)
            {
                _fileWriteLock.Release();
            }
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, SessionStateRecord>> BuildSerializableSnapshot()
    {
        var root = new Dictionary<string, IReadOnlyDictionary<string, SessionStateRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (agentName, sessions) in _stateByAgent)
        {
            var sessionRecords = new Dictionary<string, SessionStateRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sessionId, session) in sessions)
            {
                lock (session.Gate)
                {
                    var runRecords = session.Runs.ToDictionary(
                        static pair => pair.Key,
                        static pair => new RunStateRecord(
                            pair.Value.Values.ToDictionary(
                                static v => v.Key,
                                static v => v.Value,
                                StringComparer.OrdinalIgnoreCase),
                            pair.Value.UpdatedAt),
                        StringComparer.OrdinalIgnoreCase);
                    var messageMap = session.MessageToRun.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase);

                    sessionRecords[sessionId] = new SessionStateRecord(
                        session.LatestRunId,
                        runRecords,
                        messageMap);
                }
            }

            root[agentName] = sessionRecords;
        }

        return root;
    }

    private sealed class SessionState
    {
        public object Gate { get; } = new();
        public Dictionary<string, RunState> Runs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> MessageToRun { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LatestRunId { get; set; }
    }

    private sealed class RunState(
        Dictionary<string, string> values,
        DateTimeOffset updatedAt)
    {
        public Dictionary<string, string> Values { get; } = values;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
    }
}
