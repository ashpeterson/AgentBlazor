using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace AgentBlazor.Core.Paid.Suggestions;

/// <summary>
/// SQLite-backed smart suggestion service for Pro/Enterprise tiers.
/// Uses pattern-based learning from historical action sequences.
/// </summary>
public sealed class SqliteSmartSuggestionService : ISmartSuggestionService, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IChatClient? _llmFallback;
    private readonly float _llmConfidenceThreshold;
    private bool _connectionOpened;

    private const int SequenceLength = 2; // Look at last N actions to predict next
    private const int MaxSuggestions = 5;

    public SqliteSmartSuggestionService(
        string? connectionString = null,
        IChatClient? llmFallback = null,
        float llmConfidenceThreshold = 0.3f)
    {
        connectionString ??= "Data Source=agentblazor-history.db";
        _connection = new SqliteConnection(connectionString);
        _llmFallback = llmFallback;
        _llmConfidenceThreshold = llmConfidenceThreshold;
    }

    public static SqliteSmartSuggestionService CreateWithPath(
        string dbPath,
        IChatClient? llmFallback = null)
    {
        return new SqliteSmartSuggestionService($"Data Source={dbPath}", llmFallback);
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connectionOpened) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connectionOpened) return;
            await _connection.OpenAsync(ct).ConfigureAwait(false);
            _connectionOpened = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SmartSuggestion>> GetSuggestionsAsync(
        SuggestionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var suggestions = new List<SmartSuggestion>();

        // 1. Try sequence pattern matching first (fast, no LLM cost)
        if (context.RecentActions.Count > 0)
        {
            var patternSuggestions = await GetSequenceBasedSuggestionsAsync(
                context.RecentActions, context.UserId, ct).ConfigureAwait(false);
            suggestions.AddRange(patternSuggestions);
        }

        // 2. If low confidence or no results, try popularity by route
        if (suggestions.Count < MaxSuggestions && !string.IsNullOrEmpty(context.CurrentRoute))
        {
            var popularSuggestions = await GetPopularForRouteAsync(
                context.CurrentRoute, MaxSuggestions - suggestions.Count, ct).ConfigureAwait(false);

            // Add only those not already suggested
            var existingIds = suggestions.Select(s => s.ActionId).ToHashSet();
            foreach (var s in popularSuggestions)
            {
                if (!existingIds.Contains(s.ActionId))
                {
                    suggestions.Add(s);
                    existingIds.Add(s.ActionId);
                }
            }
        }

        // 3. If still low confidence, use LLM fallback
        var maxConfidence = suggestions.Count > 0 ? suggestions.Max(s => s.Confidence) : 0f;
        if (maxConfidence < _llmConfidenceThreshold && _llmFallback != null)
        {
            var llmSuggestions = await GetLlmSuggestionsAsync(context, ct).ConfigureAwait(false);
            var existingIds = suggestions.Select(s => s.ActionId).ToHashSet();

            foreach (var s in llmSuggestions)
            {
                if (!existingIds.Contains(s.ActionId))
                {
                    suggestions.Add(s);
                }
            }
        }

        return suggestions.Take(MaxSuggestions).ToList();
    }

    private async Task<IReadOnlyList<SmartSuggestion>> GetSequenceBasedSuggestionsAsync(
        IReadOnlyList<string> recentActions,
        string? userId,
        CancellationToken ct)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        // Build sequence pattern from recent actions
        var lastActions = recentActions.TakeLast(SequenceLength).ToList();
        if (lastActions.Count == 0) return [];

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Query for action sequences: what typically follows these actions?
            // We look for sessions where the same sequence occurred and see what came next
            using var cmd = _connection.CreateCommand();

            var sequenceConditions = new List<string>();
            for (int i = 0; i < lastActions.Count; i++)
            {
                var paramName = $"@action{i}";
                sequenceConditions.Add($"LAG(action_id, {lastActions.Count - i}) OVER (PARTITION BY session_id ORDER BY timestamp) = {paramName}");
                cmd.Parameters.AddWithValue(paramName, lastActions[i]);
            }

            // This query finds what action typically follows the given sequence
            cmd.CommandText = $"""
                WITH sequenced AS (
                    SELECT
                        session_id,
                        action_id,
                        timestamp,
                        {string.Join(",\n                        ", Enumerable.Range(0, lastActions.Count).Select(i => $"LAG(action_id, {lastActions.Count - i}) OVER (PARTITION BY session_id ORDER BY timestamp) as prev_{i}"))}
                    FROM action_history
                    WHERE timestamp >= DATE('now', '-30 days')
                )
                SELECT action_id, COUNT(*) as follow_count
                FROM sequenced
                WHERE {string.Join(" AND ", Enumerable.Range(0, lastActions.Count).Select(i => $"prev_{i} = @action{i}"))}
                GROUP BY action_id
                ORDER BY follow_count DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@limit", MaxSuggestions);

            var suggestions = new List<SmartSuggestion>();
            var totalCount = 0;

            // First pass: get total for confidence calculation
            var results = new List<(string ActionId, int Count)>();
            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var actionId = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    results.Add((actionId, count));
                    totalCount += count;
                }
            }

            // Build suggestions with confidence scores
            foreach (var (actionId, count) in results)
            {
                var confidence = totalCount > 0 ? (float)count / totalCount : 0f;
                suggestions.Add(new SmartSuggestion(
                    FormatSuggestionText(actionId),
                    actionId,
                    confidence,
                    SuggestionSource.Pattern));
            }

            return suggestions;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SmartSuggestion>> GetPopularForRouteAsync(
        string route,
        int limit = 5,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT action_id, COUNT(*) as usage_count
                FROM action_history
                WHERE route = @route
                  AND timestamp >= DATE('now', '-30 days')
                GROUP BY action_id
                ORDER BY usage_count DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@route", route);
            cmd.Parameters.AddWithValue("@limit", limit);

            var suggestions = new List<SmartSuggestion>();
            var totalCount = 0;
            var results = new List<(string ActionId, int Count)>();

            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var actionId = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    results.Add((actionId, count));
                    totalCount += count;
                }
            }

            foreach (var (actionId, count) in results)
            {
                var confidence = totalCount > 0 ? (float)count / totalCount : 0f;
                suggestions.Add(new SmartSuggestion(
                    FormatSuggestionText(actionId),
                    actionId,
                    confidence * 0.8f, // Slightly lower confidence than pattern-based
                    SuggestionSource.Popular));
            }

            return suggestions;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActionSequencePattern>> GetPatternsAsync(
        string? userId = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();

            var whereClause = userId != null ? "WHERE user_id = @userId" : "";
            if (userId != null)
            {
                cmd.Parameters.AddWithValue("@userId", userId);
            }

            cmd.CommandText = $"""
                WITH action_pairs AS (
                    SELECT
                        LAG(action_id, 1) OVER (PARTITION BY session_id ORDER BY timestamp) as prev_action,
                        action_id as next_action
                    FROM action_history
                    {whereClause}
                )
                SELECT prev_action, next_action, COUNT(*) as pair_count
                FROM action_pairs
                WHERE prev_action IS NOT NULL
                GROUP BY prev_action, next_action
                HAVING pair_count >= 3
                ORDER BY pair_count DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@limit", limit);

            var patterns = new List<ActionSequencePattern>();
            var totalPairs = 0;
            var results = new List<(string Prev, string Next, int Count)>();

            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var prev = reader.GetString(0);
                    var next = reader.GetString(1);
                    var count = reader.GetInt32(2);
                    results.Add((prev, next, count));
                    totalPairs += count;
                }
            }

            foreach (var (prev, next, count) in results)
            {
                var confidence = totalPairs > 0 ? (float)count / totalPairs : 0f;
                patterns.Add(new ActionSequencePattern(
                    [prev],
                    next,
                    count,
                    confidence));
            }

            return patterns;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<SmartSuggestion>> GetLlmSuggestionsAsync(
        SuggestionContext context,
        CancellationToken ct)
    {
        if (_llmFallback == null) return [];

        try
        {
            var recentStr = context.RecentActions.Count > 0
                ? string.Join(" → ", context.RecentActions.TakeLast(3))
                : "none";

            var routeStr = context.CurrentRoute ?? "unknown";

            var prompt = $"""
                You are suggesting next actions for a Blazor app user.
                Recent actions: {recentStr}
                Current route: {routeStr}

                Return ONLY a JSON array of 3 action IDs (snake_case, like "filter_data", "submit_form"):
                ["action_id_1", "action_id_2", "action_id_3"]
                """;

            var response = await _llmFallback.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { Temperature = 0.5f, MaxOutputTokens = 100 },
                ct).ConfigureAwait(false);

            var text = response.Text?.Trim() ?? "";
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');

            if (start >= 0 && end > start)
            {
                var json = text[start..(end + 1)];
                var actions = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);

                if (actions != null)
                {
                    return actions
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Take(3)
                        .Select(a => new SmartSuggestion(
                            FormatSuggestionText(a),
                            a,
                            0.5f, // Fixed confidence for LLM suggestions
                            SuggestionSource.Llm))
                        .ToList();
                }
            }
        }
        catch
        {
            // Swallow LLM errors - suggestions are not critical
        }

        return [];
    }

    private static string FormatSuggestionText(string actionId)
    {
        // Convert snake_case to readable text
        return actionId.Replace('_', ' ').Replace('-', ' ');
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
