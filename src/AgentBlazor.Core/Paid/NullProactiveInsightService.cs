namespace AgentBlazor.Core.Paid;

/// <summary>Free-tier no-op: never generates proactive insights.</summary>
public sealed class NullProactiveInsightService : IProactiveInsightService
{
    public Task<string?> GetInsightAsync(
        string sessionId,
        string lastUserMessage,
        IReadOnlyList<string> executedActionSummaries,
        string? componentContext,
        CancellationToken ct = default) => Task.FromResult<string?>(null);
}
