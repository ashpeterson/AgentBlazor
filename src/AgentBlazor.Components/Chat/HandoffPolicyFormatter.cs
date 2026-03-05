using System.Text;

namespace AgentBlazor.Components.Chat;

public static class HandoffPolicyFormatter
{
    public static bool TryParsePolicyCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        return string.Equals(trimmed, "/handoff-policy", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "handoff policy", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildSummary(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? handoffPolicy,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? handoffApprovalPolicy,
        bool defaultRequireHandoffApproval,
        IReadOnlyList<HandoffTransition>? history,
        int? maxHandoffsPerSession,
        int? maxHandoffsPerPair,
        bool blockImmediateReturn,
        int? maxHandoffsPerWindow,
        int? handoffWindowMinutes,
        int? maxPairHandoffsPerWindow,
        DateTimeOffset? nowUtc = null)
    {
        var transitions = history ?? [];
        var sb = new StringBuilder();
        sb.AppendLine("Handoff policy summary:");
        sb.AppendLine($"- Recorded transitions: {transitions.Count}");

        if (maxHandoffsPerSession.HasValue)
        {
            sb.AppendLine($"- Session limit: {maxHandoffsPerSession.Value}");
        }

        if (maxHandoffsPerPair.HasValue)
        {
            sb.AppendLine($"- Pair lifetime limit: {maxHandoffsPerPair.Value}");
        }

        if (handoffWindowMinutes.HasValue && handoffWindowMinutes.Value > 0)
        {
            var now = nowUtc ?? DateTimeOffset.UtcNow;
            var windowStart = now.AddMinutes(-handoffWindowMinutes.Value);
            var recentCount = transitions.Count(transition => transition.TimestampUtc >= windowStart);
            sb.AppendLine($"- Active window: {handoffWindowMinutes.Value} minutes");
            sb.AppendLine($"- Handoffs in active window: {recentCount}");

            if (maxHandoffsPerWindow.HasValue)
            {
                sb.AppendLine($"- Session window limit: {maxHandoffsPerWindow.Value}");
            }

            if (maxPairHandoffsPerWindow.HasValue)
            {
                sb.AppendLine($"- Pair window limit: {maxPairHandoffsPerWindow.Value}");
            }
        }

        sb.AppendLine($"- Immediate return blocked: {(blockImmediateReturn ? "yes" : "no")}");
        sb.AppendLine($"- Default handoff approval required: {(defaultRequireHandoffApproval ? "yes" : "no")}");
        if (handoffApprovalPolicy is null || handoffApprovalPolicy.Count == 0)
        {
            sb.AppendLine("- Approval rules: none");
        }
        else
        {
            sb.AppendLine("- Approval rules:");
            foreach (var (source, rawTargets) in handoffApprovalPolicy
                         .OrderBy(static rule => rule.Key, StringComparer.OrdinalIgnoreCase))
            {
                var tokens = rawTargets
                    .Where(static token => !string.IsNullOrWhiteSpace(token))
                    .Select(static token => token.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var displayTargets = tokens.Length == 0 ? ["(none)"] : tokens;
                sb.AppendLine($"  - {source} -> {string.Join(", ", displayTargets)}");
            }
        }

        if (handoffPolicy is null || handoffPolicy.Count == 0)
        {
            sb.Append("Rules: none (all agent pairs allowed unless limits block).");
            return sb.ToString();
        }

        var normalizedRules = handoffPolicy
            .OrderBy(static rule => rule.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        sb.AppendLine("Rules:");
        foreach (var (source, rawTargets) in normalizedRules)
        {
            var tokens = rawTargets
                .Where(static token => !string.IsNullOrWhiteSpace(token))
                .Select(static token => token.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var displayTargets = tokens.Length == 0 ? ["(none)"] : tokens;
            sb.AppendLine($"- {source} -> {string.Join(", ", displayTargets)}");
        }

        return sb.ToString().TrimEnd();
    }
}
