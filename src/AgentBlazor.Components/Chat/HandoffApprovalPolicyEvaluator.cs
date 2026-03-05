namespace AgentBlazor.Components.Chat;

public static class HandoffApprovalPolicyEvaluator
{
    public static bool ShouldRequireApproval(
        bool defaultRequireApproval,
        string fromAgent,
        string toAgent,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? handoffApprovalPolicy)
    {
        if (string.IsNullOrWhiteSpace(fromAgent) || string.IsNullOrWhiteSpace(toAgent))
        {
            return defaultRequireApproval;
        }

        if (handoffApprovalPolicy is null || handoffApprovalPolicy.Count == 0)
        {
            return defaultRequireApproval;
        }

        if (TryGetTargetsForSource(handoffApprovalPolicy, fromAgent, out var explicitTargets))
        {
            return EvaluateTargets(defaultRequireApproval, toAgent, explicitTargets);
        }

        if (TryGetTargetsForSource(handoffApprovalPolicy, "*", out var wildcardTargets))
        {
            return EvaluateTargets(defaultRequireApproval, toAgent, wildcardTargets);
        }

        return defaultRequireApproval;
    }

    private static bool TryGetTargetsForSource(
        IReadOnlyDictionary<string, IReadOnlyList<string>> handoffApprovalPolicy,
        string sourceAgent,
        out IReadOnlyList<string> targets)
    {
        foreach (var entry in handoffApprovalPolicy)
        {
            if (!string.Equals(entry.Key, sourceAgent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            targets = entry.Value;
            return true;
        }

        targets = [];
        return false;
    }

    private static bool EvaluateTargets(
        bool defaultRequireApproval,
        string toAgent,
        IReadOnlyList<string> rawTargets)
    {
        var tokens = rawTargets
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(static token => token.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tokens.Length == 0)
        {
            return false;
        }

        var denyTargets = tokens
            .Where(static token => token.StartsWith('!'))
            .Select(static token => token[1..].Trim())
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requireTargets = tokens
            .Where(static token => !token.StartsWith('!'))
            .ToArray();

        var denyAll = denyTargets.Any(static token => string.Equals(token, "*", StringComparison.OrdinalIgnoreCase));
        if (denyAll || denyTargets.Any(target => string.Equals(target, toAgent, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var requireAll = requireTargets.Any(static token => string.Equals(token, "*", StringComparison.OrdinalIgnoreCase));
        if (requireAll)
        {
            return true;
        }

        if (requireTargets.Any(target => string.Equals(target, toAgent, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Explicit policy for this source exists but target did not match.
        // Treat as not requiring approval unless default behavior is desired by caller.
        return requireTargets.Length == 0 ? defaultRequireApproval : false;
    }
}
