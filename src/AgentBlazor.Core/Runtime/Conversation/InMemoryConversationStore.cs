using System.Collections.Concurrent;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Conversation;

/// <summary>
/// In-memory implementation of IConversationStore with automatic TTL cleanup.
/// </summary>
internal sealed class InMemoryConversationStore : IConversationStore, IDisposable
{
    private readonly ConcurrentDictionary<string, ConversationHistory> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _userSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConversationOptions _options;
    private readonly ILogger<InMemoryConversationStore>? _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private Timer? _cleanupTimer;
    private bool _disposed;

    public InMemoryConversationStore(
        IOptions<ConversationOptions>? options = null,
        ILogger<InMemoryConversationStore>? logger = null)
    {
        _options = options?.Value ?? new ConversationOptions();
        _logger = logger;

        // Start cleanup timer if enabled
        if (_options.EnableAutoCleanup)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupExpiredSessionsAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);

            _logger?.LogInformation(
                "Started conversation store cleanup timer with interval {Interval}",
                _options.CleanupInterval);
        }
    }

    public Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_sessions.TryGetValue(sessionId, out var history))
        {
            return Task.FromResult<ConversationHistory?>(null);
        }

        // Check if expired
        if (IsExpired(history))
        {
            _sessions.TryRemove(sessionId, out _);
            RemoveFromUserIndex(sessionId, history.UserId);
            return Task.FromResult<ConversationHistory?>(null);
        }

        return Task.FromResult<ConversationHistory?>(history);
    }

    public Task AppendTurnAsync(
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

                // Trim old turns if over limit
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

        // Evict oldest sessions if over capacity
        if (_sessions.Count > _options.MaxSessions)
        {
            EvictOldestSessions(_options.MaxSessions / 10);
        }

        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessions.TryRemove(sessionId, out var removed))
        {
            RemoveFromUserIndex(sessionId, removed.UserId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        var activeSessions = _sessions
            .Where(kvp => !IsExpired(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<string>>(activeSessions);
    }

    public Task SetUserIdAsync(
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

            // Update user index
            if (oldUserId is not null && !string.Equals(oldUserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                RemoveFromUserIndex(sessionId, oldUserId);
            }

            AddToUserIndex(sessionId, userId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (_userSessions.TryGetValue(userId, out var sessions))
        {
            // Filter out expired sessions
            var active = sessions
                .Where(sessionId =>
                    _sessions.TryGetValue(sessionId, out var history) && !IsExpired(history))
                .ToArray();

            return Task.FromResult<IReadOnlyCollection<string>>(active);
        }

        return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
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
            return;

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

        _logger?.LogInformation("Evicted {Count} oldest sessions due to capacity limit", toEvict.Length);
    }

    private async Task CleanupExpiredSessionsAsync()
    {
        if (!await _cleanupLock.WaitAsync(0))
            return; // Another cleanup is running

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
            return;

        _cleanupTimer?.Dispose();
        _cleanupLock.Dispose();
        _disposed = true;
    }
}
