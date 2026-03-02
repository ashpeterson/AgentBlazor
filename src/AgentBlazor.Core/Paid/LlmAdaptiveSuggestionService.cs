using System.Text.Json;
using AgentBlazor.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Paid;

/// <summary>
/// Paid-tier suggestion service that calls the LLM to generate contextual follow-up suggestions
/// based on recent action history and the current user context.
/// </summary>
public sealed class LlmAdaptiveSuggestionService : IAdaptiveSuggestionService
{
    private readonly IChatClient? _chatClient;
    private readonly IActionHistoryStore _store;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LlmAdaptiveSuggestionService(IActionHistoryStore store, IChatClient? chatClient = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _chatClient = chatClient;
    }

    public async Task<IReadOnlyList<AgentSuggestion>> GetSuggestionsAsync(
        string sessionId,
        string? userId,
        string? currentContext,
        CancellationToken ct = default)
    {
        if (_chatClient is null)
            return [];

        try
        {
            var history = await _store.GetRecentAsync(sessionId, limit: 3, ct);
            var recentSummary = history.Count > 0
                ? string.Join(", ", history.Select(h => h.ActionId))
                : "none";

            var userContext = string.IsNullOrWhiteSpace(currentContext) ? "general usage" : currentContext;

            var prompt = $$"""
                You suggest follow-up actions for a Blazor web app user.
                Recent actions: {{recentSummary}}
                User said: {{userContext}}
                Return ONLY a JSON array of 3 short action phrases under 60 chars: ["...", "...", "..."]
                """;

            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Temperature = 0.6f, MaxOutputTokens = 150 },
                ct);

            var text = response.Text?.Trim() ?? string.Empty;

            // Extract JSON array from response
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start < 0 || end <= start)
                return [];

            var jsonArray = text[start..(end + 1)];
            var suggestions = JsonSerializer.Deserialize<string[]>(jsonArray, JsonOptions);
            if (suggestions is null || suggestions.Length == 0)
                return [];

            return suggestions
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(3)
                .Select(s => new AgentSuggestion(s.Trim(), 0.8f, "llm"))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
