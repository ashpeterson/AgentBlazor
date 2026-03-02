using System.Collections.Concurrent;

namespace AgentBlazor.Core.Paid;

/// <summary>
/// In-memory action history store for Paid/Premium tiers.
/// Caps each session at 200 entries (oldest dropped when over limit).
/// </summary>
public sealed class InMemoryActionHistoryStore : IActionHistoryStore
{
    private const int MaxPerSession = 200;

    // session-keyed queues
    private readonly ConcurrentDictionary<string, Queue<ActionHistoryEntry>> _bySession =
        new(StringComparer.OrdinalIgnoreCase);

    // user-keyed queues (userId may be null, so keyed by non-null userId only)
    private readonly ConcurrentDictionary<string, Queue<ActionHistoryEntry>> _byUser =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    public Task RecordAsync(ActionHistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            Enqueue(_bySession, entry.SessionId, entry, MaxPerSession);

            if (!string.IsNullOrWhiteSpace(entry.UserId))
                Enqueue(_byUser, entry.UserId, entry, MaxPerSession);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActionHistoryEntry>> GetRecentAsync(
        string sessionId, int limit = 50, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_bySession.TryGetValue(sessionId, out var queue))
                return Task.FromResult<IReadOnlyList<ActionHistoryEntry>>([]);

            var result = queue
                .OrderByDescending(static e => e.Timestamp)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<ActionHistoryEntry>>(result);
        }
    }

    public Task<IReadOnlyList<ActionHistoryEntry>> GetByUserAsync(
        string userId, int limit = 200, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_byUser.TryGetValue(userId, out var queue))
                return Task.FromResult<IReadOnlyList<ActionHistoryEntry>>([]);

            var result = queue
                .OrderByDescending(static e => e.Timestamp)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<ActionHistoryEntry>>(result);
        }
    }

    private static void Enqueue(
        ConcurrentDictionary<string, Queue<ActionHistoryEntry>> store,
        string key,
        ActionHistoryEntry entry,
        int cap)
    {
        var queue = store.GetOrAdd(key, static _ => new Queue<ActionHistoryEntry>());
        queue.Enqueue(entry);
        while (queue.Count > cap)
            queue.Dequeue();
    }
}
