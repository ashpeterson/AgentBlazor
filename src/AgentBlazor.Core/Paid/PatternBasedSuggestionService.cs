using System.Text;

namespace AgentBlazor.Core.Paid;

/// <summary>
/// Paid-tier suggestion service. Uses pure frequency analysis over action history —
/// no LLM call required. Returns up to 3 suggestions based on the most frequently
/// executed (actionId, args-snapshot) pairs for the current session and user.
/// </summary>
public sealed class PatternBasedSuggestionService : IAdaptiveSuggestionService
{
    private const int MaxSuggestions = 3;
    private readonly IActionHistoryStore _store;

    public PatternBasedSuggestionService(IActionHistoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async Task<IReadOnlyList<AgentSuggestion>> GetSuggestionsAsync(
        string sessionId,
        string? userId,
        string? currentContext,
        CancellationToken ct = default)
    {
        // Load recent history for both session and user, de-duplicating by id
        var sessionHistory = await _store.GetRecentAsync(sessionId, limit: 50, ct);

        IReadOnlyList<ActionHistoryEntry> userHistory = [];
        if (!string.IsNullOrWhiteSpace(userId))
            userHistory = await _store.GetByUserAsync(userId, limit: 200, ct);

        // Build frequency map: key = (actionId, stable-args-snapshot) → count + last entry
        var frequency = new Dictionary<string, (int Count, ActionHistoryEntry Last)>(StringComparer.OrdinalIgnoreCase);
        var totalCount = 0;

        foreach (var entry in sessionHistory.Concat(userHistory))
        {
            var key = BuildFrequencyKey(entry.ActionId, entry.Args);
            frequency.TryGetValue(key, out var existing);
            frequency[key] = (existing.Count + 1, entry);
            totalCount++;
        }

        if (frequency.Count == 0) return [];

        // Get top-N by frequency, excluding empty suggestions
        var candidates = frequency
            .OrderByDescending(kv => kv.Value.Count)
            .Take(MaxSuggestions)
            .Select(kv =>
            {
                var text = FormatSuggestion(kv.Value.Last);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var confidence = totalCount > 0 ? (float)kv.Value.Count / totalCount : 0f;
                return new AgentSuggestion(text, MathF.Min(confidence, 1f), "pattern");
            })
            .Where(s => s is not null)
            .Cast<AgentSuggestion>()
            .ToList();

        return candidates;
    }

    private static string BuildFrequencyKey(string actionId, IReadOnlyDictionary<string, object?> args)
    {
        var sb = new StringBuilder(actionId);
        foreach (var kv in args.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|');
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value ?? "null");
        }
        return sb.ToString();
    }

    private static string FormatSuggestion(ActionHistoryEntry entry)
    {
        // Use original user message if short enough; otherwise synthesize from action + args
        if (!string.IsNullOrWhiteSpace(entry.UserMessage) && entry.UserMessage.Length <= 80)
            return entry.UserMessage;

        if (entry.Args.Count == 0)
            return $"{entry.ActionId}";

        var argParts = entry.Args
            .Where(kv => kv.Value is not null)
            .Take(3)
            .Select(kv => $"{kv.Key}: {kv.Value}");

        return $"{entry.ActionId} ({string.Join(", ", argParts)})";
    }
}
