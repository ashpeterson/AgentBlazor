namespace AgentBlazor.Core.Paid.Suggestions;

/// <summary>
/// Smart suggestion service for Pro/Enterprise tiers.
/// Combines pattern-based learning, popularity analysis, and optional LLM enhancement.
/// </summary>
public interface ISmartSuggestionService
{
    /// <summary>
    /// Gets smart suggestions based on context, user history, and current state.
    /// </summary>
    Task<IReadOnlyList<SmartSuggestion>> GetSuggestionsAsync(
        SuggestionContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Gets discovered action sequence patterns for analysis.
    /// </summary>
    Task<IReadOnlyList<ActionSequencePattern>> GetPatternsAsync(
        string? userId = null,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Gets popular actions for a specific route.
    /// </summary>
    Task<IReadOnlyList<SmartSuggestion>> GetPopularForRouteAsync(
        string route,
        int limit = 5,
        CancellationToken ct = default);
}
