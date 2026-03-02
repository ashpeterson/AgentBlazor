namespace AgentBlazor.Core.Paid;

/// <summary>
/// Free-tier suggestion service — always returns an empty list.
/// </summary>
internal sealed class StaticSuggestionService : IAdaptiveSuggestionService
{
    public Task<IReadOnlyList<AgentSuggestion>> GetSuggestionsAsync(
        string sessionId,
        string? userId,
        string? currentContext,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentSuggestion>>([]);
}
