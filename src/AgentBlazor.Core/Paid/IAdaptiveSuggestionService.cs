namespace AgentBlazor.Core.Paid;

public interface IAdaptiveSuggestionService
{
    Task<IReadOnlyList<AgentSuggestion>> GetSuggestionsAsync(
        string sessionId,
        string? userId,
        string? currentContext,
        CancellationToken ct = default);
}
