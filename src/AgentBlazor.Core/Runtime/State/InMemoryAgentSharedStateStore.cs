using System.Collections.Concurrent;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.State;

internal sealed class InMemoryAgentSharedStateStore : IAgentSharedStateStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SessionState>> _stateByAgent =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SharedStateOptions _options;

    public InMemoryAgentSharedStateStore(IOptions<SharedStateOptions>? options = null)
    {
        _options = options?.Value ?? new SharedStateOptions();
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

        lock (session.Gate)
        {
            if (session.Runs.TryGetValue(runId, out var existing) &&
                ShouldRejectStale(timestamp, existing.UpdatedAt))
            {
                return new AgentSharedStateSnapshot(
                    agentName,
                    sessionId,
                    runId,
                    Clone(existing.Values),
                    existing.UpdatedAt);
            }

            session.Runs[runId] = new RunState(Clone(values), timestamp);
            session.LatestRunId = runId;

            return new AgentSharedStateSnapshot(
                agentName,
                sessionId,
                runId,
                Clone(values),
                timestamp);
        }
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
                return new AgentSharedStateDelta(
                    agentName,
                    sessionId,
                    runId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                    runState.UpdatedAt);
            }

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

            return new AgentSharedStateDelta(
                agentName,
                sessionId,
                runId,
                CloneNullable(changes),
                timestamp);
        }
    }

    private bool ShouldRejectStale(DateTimeOffset incoming, DateTimeOffset current)
        => _options.MergeMode == SharedStateMergeMode.RejectStaleWrites && incoming < current;

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
