using System.Collections.Concurrent;
using System.Text.Json;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Conversation;

/// <summary>
/// File-backed JSON implementation of IConversationStore for basic persistence across restarts.
/// </summary>
internal sealed class JsonFileConversationStore : IConversationStore, IDisposable
{
    private readonly ConcurrentDictionary<string, ConversationHistory> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _userSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConversationOptions _options;
    private readonly ILogger<JsonFileConversationStore>? _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private readonly SemaphoreSlim _fileWriteLock = new(1, 1);
    private Timer? _cleanupTimer;
    private bool _disposed;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private sealed record ConversationStoreSnapshot(
        IReadOnlyDictionary<string, ConversationHistory> Sessions);

    public JsonFileConversationStore(
        string filePath,
        IOptions<ConversationOptions>? options = null,
        ILogger<JsonFileConversationStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = Path.GetFullPath(filePath);
        _options = options?.Value ?? new ConversationOptions();
        _logger = logger;

        LoadSnapshot();

        if (_options.EnableAutoCleanup)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupExpiredSessionsAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);

            _logger?.LogInformation(
                "Started file conversation store cleanup timer with interval {Interval}",
                _options.CleanupInterval);
        }
    }

    public async Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_sessions.TryGetValue(sessionId, out var history))
        {
            return null;
        }

        if (!IsExpired(history))
        {
            return history;
        }

        _sessions.TryRemove(sessionId, out _);
        RemoveFromUserIndex(sessionId, history.UserId);
        await PersistSnapshotAsync(cancellationToken);
        return null;
    }

    public async Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turn);

        _sessions.AddOrUpdate(
            sessionId,
            _ => ConversationHistory.Create(sessionId).WithTurn(turn),
            (_, existing) =>
            {
                var updated = existing.WithTurn(turn);

                if (updated.Turns.Count > _options.MaxTurnsPerSession)
                {
                    updated = updated with
                    {
                        Turns = updated.Turns
                            .Skip(updated.Turns.Count - _options.MaxTurnsPerSession)
                            .ToArray()
                    };
                }

                return updated;
            });

        if (_sessions.Count > _options.MaxSessions)
        {
            EvictOldestSessions(_options.MaxSessions / 10);
        }

        await PersistSnapshotAsync(cancellationToken);
    }

    public async Task ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessions.TryRemove(sessionId, out var removed))
        {
            RemoveFromUserIndex(sessionId, removed.UserId);
            await PersistSnapshotAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var expired = _sessions
            .Where(kvp => IsExpired(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToArray();

        foreach (var sessionId in expired)
        {
            if (_sessions.TryRemove(sessionId, out var removed))
            {
                RemoveFromUserIndex(sessionId, removed.UserId);
            }
        }

        if (expired.Length > 0)
        {
            await PersistSnapshotAsync(CancellationToken.None);
        }

        return _sessions.Keys.ToArray();
    }

    public async Task SetUserIdAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_sessions.TryGetValue(sessionId, out var history))
        {
            var oldUserId = history.UserId;
            var updated = history with { UserId = userId };
            _sessions.TryUpdate(sessionId, updated, history);

            if (oldUserId is not null && !string.Equals(oldUserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                RemoveFromUserIndex(sessionId, oldUserId);
            }

            AddToUserIndex(sessionId, userId);
            await PersistSnapshotAsync(cancellationToken);
        }
    }

    public Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_userSessions.TryGetValue(userId, out var sessions))
        {
            return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
        }

        var active = sessions
            .Where(sessionId =>
                _sessions.TryGetValue(sessionId, out var history) &&
                !IsExpired(history))
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<string>>(active);
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
            var snapshot = JsonSerializer.Deserialize<ConversationStoreSnapshot>(stream, SerializerOptions);
            if (snapshot?.Sessions is null || snapshot.Sessions.Count == 0)
            {
                return;
            }

            foreach (var pair in snapshot.Sessions)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var history = pair.Value;
                if (history is null || IsExpired(history))
                {
                    continue;
                }

                _sessions[pair.Key] = history;
                if (!string.IsNullOrWhiteSpace(history.UserId))
                {
                    AddToUserIndex(pair.Key, history.UserId);
                }
            }

            if (_sessions.Count > _options.MaxSessions)
            {
                EvictOldestSessions(_sessions.Count - _options.MaxSessions);
            }

            _logger?.LogInformation(
                "Loaded {SessionCount} sessions from {Path}",
                _sessions.Count,
                _filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load conversation store snapshot from {Path}", _filePath);
        }
    }

    private async Task PersistSnapshotAsync(CancellationToken cancellationToken)
    {
        await _fileWriteLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = new ConversationStoreSnapshot(
                _sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase));

            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist conversation store snapshot to {Path}", _filePath);
        }
        finally
        {
            _fileWriteLock.Release();
        }
    }

    private bool IsExpired(ConversationHistory history) =>
        DateTime.UtcNow - history.LastActivityAt > _options.SessionTimeout;

    private void AddToUserIndex(string sessionId, string userId)
    {
        _userSessions.AddOrUpdate(
            userId,
            _ => [sessionId],
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(sessionId);
                }

                return existing;
            });
    }

    private void RemoveFromUserIndex(string sessionId, string? userId)
    {
        if (userId is null)
        {
            return;
        }

        if (_userSessions.TryGetValue(userId, out var sessions))
        {
            lock (sessions)
            {
                sessions.Remove(sessionId);
                if (sessions.Count == 0)
                {
                    _userSessions.TryRemove(userId, out _);
                }
            }
        }
    }

    private void EvictOldestSessions(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var toEvict = _sessions
            .OrderBy(kvp => kvp.Value.LastActivityAt)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToArray();

        foreach (var sessionId in toEvict)
        {
            if (_sessions.TryRemove(sessionId, out var removed))
            {
                RemoveFromUserIndex(sessionId, removed.UserId);
            }
        }

        if (toEvict.Length > 0)
        {
            _logger?.LogInformation("Evicted {Count} oldest sessions due to capacity limit", toEvict.Length);
        }
    }

    private async Task CleanupExpiredSessionsAsync()
    {
        if (!await _cleanupLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var expired = _sessions
                .Where(kvp => IsExpired(kvp.Value))
                .Select(kvp => kvp.Key)
                .ToArray();

            foreach (var sessionId in expired)
            {
                if (_sessions.TryRemove(sessionId, out var removed))
                {
                    RemoveFromUserIndex(sessionId, removed.UserId);
                }
            }

            if (expired.Length > 0)
            {
                await PersistSnapshotAsync(CancellationToken.None);
                _logger?.LogInformation("Cleaned up {Count} expired sessions", expired.Length);
            }
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cleanupTimer?.Dispose();
        _cleanupLock.Dispose();
        _fileWriteLock.Dispose();
        _disposed = true;
    }
}
