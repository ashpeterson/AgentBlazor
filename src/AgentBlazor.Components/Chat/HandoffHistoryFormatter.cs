namespace AgentBlazor.Components.Chat;

public static class HandoffHistoryFormatter
{
    public static bool TryParseHistoryCommand(string message, out int limit)
    {
        limit = 10;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var trimmed = message.Trim();
        if (string.Equals(trimmed, "/handoff-history", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "handoff history", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.StartsWith("/handoff-history ", StringComparison.OrdinalIgnoreCase))
        {
            var rawLimit = trimmed["/handoff-history ".Length..].Trim();
            if (int.TryParse(rawLimit, out var parsedLimit))
            {
                limit = Math.Clamp(parsedLimit, 1, 25);
                return true;
            }
        }

        return false;
    }

    public static string BuildSummary(IReadOnlyList<HandoffTransition> transitions, int limit)
    {
        if (transitions.Count == 0)
        {
            return "No handoff transitions recorded yet.";
        }

        var normalizedLimit = Math.Clamp(limit, 1, 25);
        var recent = transitions
            .TakeLast(normalizedLimit)
            .Reverse()
            .ToArray();

        var lines = new List<string>(recent.Length + 2)
        {
            $"Recent handoffs ({recent.Length} of {transitions.Count}):"
        };

        for (var i = 0; i < recent.Length; i++)
        {
            var handoff = recent[i];
            lines.Add($"{i + 1}. {handoff.TimestampUtc.ToLocalTime():HH:mm:ss} {handoff.FromAgent} -> {handoff.ToAgent}");
        }

        var topPaths = transitions
            .GroupBy(static handoff => $"{handoff.FromAgent} -> {handoff.ToAgent}", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(static group => $"{group.Key} x{group.Count()}")
            .ToArray();

        if (topPaths.Length > 0)
        {
            lines.Add($"Top paths: {string.Join(", ", topPaths)}.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

