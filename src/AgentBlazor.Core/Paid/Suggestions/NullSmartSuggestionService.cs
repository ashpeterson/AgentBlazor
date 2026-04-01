namespace AgentBlazor.Core.Paid.Suggestions;

/// <summary>
/// No-op smart suggestion service for free tier.
/// Returns empty results for all queries.
/// </summary>
public sealed class NullSmartSuggestionService : ISmartSuggestionService
{
    public static NullSmartSuggestionService Instance { get; } = new();

    public Task<IReadOnlyList<SmartSuggestion>> GetSuggestionsAsync(
        SuggestionContext context,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<SmartSuggestion>>([]);
    }

    public Task<IReadOnlyList<ActionSequencePattern>> GetPatternsAsync(
        string? userId = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ActionSequencePattern>>([]);
    }

    public Task<IReadOnlyList<SmartSuggestion>> GetPopularForRouteAsync(
        string route,
        int limit = 5,
        CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<SmartSuggestion>>([]);
    }
}
