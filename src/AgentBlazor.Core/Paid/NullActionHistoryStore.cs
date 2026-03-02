namespace AgentBlazor.Core.Paid;

/// <summary>
/// No-op implementation used on the Free tier. All operations are instant and return empty results.
/// </summary>
internal sealed class NullActionHistoryStore : IActionHistoryStore
{
    public Task RecordAsync(ActionHistoryEntry entry, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ActionHistoryEntry>> GetRecentAsync(
        string sessionId, int limit = 50, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ActionHistoryEntry>>([]);

    public Task<IReadOnlyList<ActionHistoryEntry>> GetByUserAsync(
        string userId, int limit = 200, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ActionHistoryEntry>>([]);
}
