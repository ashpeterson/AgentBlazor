namespace AgentBlazor.Core.Paid;

public interface IActionHistoryStore
{
    Task RecordAsync(ActionHistoryEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<ActionHistoryEntry>> GetRecentAsync(
        string sessionId, int limit = 50, CancellationToken ct = default);

    Task<IReadOnlyList<ActionHistoryEntry>> GetByUserAsync(
        string userId, int limit = 200, CancellationToken ct = default);
}
