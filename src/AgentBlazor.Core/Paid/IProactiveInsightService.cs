namespace AgentBlazor.Core.Paid;

public interface IProactiveInsightService
{
    Task<string?> GetInsightAsync(
        string sessionId,
        string lastUserMessage,
        IReadOnlyList<string> executedActionSummaries,
        string? componentContext,
        CancellationToken ct = default);
}
