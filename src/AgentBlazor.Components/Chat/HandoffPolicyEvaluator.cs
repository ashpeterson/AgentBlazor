namespace AgentBlazor.Components.Chat;

public sealed record HandoffTransition(
    string FromAgent,
    string ToAgent,
    DateTimeOffset TimestampUtc);

public sealed record HandoffPolicyDecision(
    bool Allowed,
    string? ViolationMessage);

public static class HandoffPolicyEvaluator
{
    public static HandoffPolicyDecision Evaluate(
        string fromAgent,
        string toAgent,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? handoffPolicy,
        IReadOnlyList<HandoffTransition>? history,
        int? maxHandoffsPerSession,
        int? maxHandoffsPerPair,
        bool blockImmediateReturn,
        int? maxHandoffsPerWindow,
        int? handoffWindowMinutes,
        int? maxPairHandoffsPerWindow,
        DateTimeOffset? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(fromAgent) || string.IsNullOrWhiteSpace(toAgent))
        {
            return new HandoffPolicyDecision(false, "Handoff policy: source and target agent names are required.");
        }

        var transitions = history ?? [];

        if (maxHandoffsPerSession.HasValue && maxHandoffsPerSession.Value >= 0 && transitions.Count >= maxHandoffsPerSession.Value)
        {
            return new HandoffPolicyDecision(
                false,
                $"Handoff policy: session handoff limit ({maxHandoffsPerSession.Value}) has been reached.");
        }

        if (blockImmediateReturn && transitions.Count > 0)
        {
            var last = transitions[^1];
            if (string.Equals(last.FromAgent, toAgent, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(last.ToAgent, fromAgent, StringComparison.OrdinalIgnoreCase))
            {
                return new HandoffPolicyDecision(
                    false,
                    $"Handoff policy: immediate return handoff {fromAgent} -> {toAgent} is blocked to prevent ping-pong loops.");
            }
        }

        if (maxHandoffsPerPair.HasValue && maxHandoffsPerPair.Value >= 0)
        {
            var pairCount = transitions.Count(transition =>
                string.Equals(transition.FromAgent, fromAgent, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(transition.ToAgent, toAgent, StringComparison.OrdinalIgnoreCase));
            if (pairCount >= maxHandoffsPerPair.Value)
            {
                return new HandoffPolicyDecision(
                    false,
                    $"Handoff policy: pair limit reached for {fromAgent} -> {toAgent} ({maxHandoffsPerPair.Value}).");
            }
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var recentWindowTransitions = GetRecentWindowTransitions(transitions, now, handoffWindowMinutes);
        if (maxHandoffsPerWindow.HasValue &&
            maxHandoffsPerWindow.Value >= 0 &&
            handoffWindowMinutes.HasValue &&
            handoffWindowMinutes.Value > 0 &&
            recentWindowTransitions.Count >= maxHandoffsPerWindow.Value)
        {
            return new HandoffPolicyDecision(
                false,
                $"Handoff policy: {maxHandoffsPerWindow.Value} handoffs per {handoffWindowMinutes.Value} minute window reached.");
        }

        if (maxPairHandoffsPerWindow.HasValue &&
            maxPairHandoffsPerWindow.Value >= 0 &&
            handoffWindowMinutes.HasValue &&
            handoffWindowMinutes.Value > 0)
        {
            var pairWindowCount = recentWindowTransitions.Count(transition =>
                string.Equals(transition.FromAgent, fromAgent, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(transition.ToAgent, toAgent, StringComparison.OrdinalIgnoreCase));
            if (pairWindowCount >= maxPairHandoffsPerWindow.Value)
            {
                return new HandoffPolicyDecision(
                    false,
                    $"Handoff policy: {fromAgent} -> {toAgent} exceeded {maxPairHandoffsPerWindow.Value} handoffs in {handoffWindowMinutes.Value} minutes.");
            }
        }

        if (handoffPolicy is null || handoffPolicy.Count == 0)
        {
            return new HandoffPolicyDecision(true, null);
        }

        if (TryGetPolicyDecision(fromAgent, toAgent, handoffPolicy, out var policyDecision))
        {
            return policyDecision;
        }

        return new HandoffPolicyDecision(true, null);
    }

    private static IReadOnlyList<HandoffTransition> GetRecentWindowTransitions(
        IReadOnlyList<HandoffTransition> transitions,
        DateTimeOffset now,
        int? handoffWindowMinutes)
    {
        if (!handoffWindowMinutes.HasValue || handoffWindowMinutes.Value <= 0)
        {
            return [];
        }

        var windowStart = now.AddMinutes(-handoffWindowMinutes.Value);
        return transitions
            .Where(transition => transition.TimestampUtc >= windowStart)
            .ToArray();
    }

    private static bool TryGetPolicyDecision(
        string fromAgent,
        string toAgent,
        IReadOnlyDictionary<string, IReadOnlyList<string>> handoffPolicy,
        out HandoffPolicyDecision decision)
    {
        decision = new HandoffPolicyDecision(true, null);

        if (TryGetTargetsForSource(handoffPolicy, fromAgent, out var explicitTargets))
        {
            decision = EvaluateTargets(fromAgent, toAgent, explicitTargets);
            return true;
        }

        if (TryGetTargetsForSource(handoffPolicy, "*", out var wildcardTargets))
        {
            decision = EvaluateTargets(fromAgent, toAgent, wildcardTargets);
            return true;
        }

        return false;
    }

    private static bool TryGetTargetsForSource(
        IReadOnlyDictionary<string, IReadOnlyList<string>> handoffPolicy,
        string sourceAgent,
        out IReadOnlyList<string> targets)
    {
        foreach (var entry in handoffPolicy)
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

    private static HandoffPolicyDecision EvaluateTargets(
        string fromAgent,
        string toAgent,
        IReadOnlyList<string> rawTargets)
    {
        var tokens = rawTargets
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(static token => token.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var denyTargets = tokens
            .Where(static token => token.StartsWith('!'))
            .Select(static token => token[1..].Trim())
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allowTargets = tokens
            .Where(static token => !token.StartsWith('!'))
            .ToArray();

        var denyAll = denyTargets.Any(static token => string.Equals(token, "*", StringComparison.OrdinalIgnoreCase));
        if (denyAll || denyTargets.Any(target => string.Equals(target, toAgent, StringComparison.OrdinalIgnoreCase)))
        {
            return new HandoffPolicyDecision(
                false,
                $"Handoff policy: '{fromAgent}' is blocked from handing off to '{toAgent}'.");
        }

        var allowAll = allowTargets.Any(static token => string.Equals(token, "*", StringComparison.OrdinalIgnoreCase));
        if (allowAll)
        {
            return new HandoffPolicyDecision(true, null);
        }

        if (allowTargets.Any(target => string.Equals(target, toAgent, StringComparison.OrdinalIgnoreCase)))
        {
            return new HandoffPolicyDecision(true, null);
        }

        if (allowTargets.Length == 0)
        {
            return new HandoffPolicyDecision(
                false,
                $"Handoff policy: '{fromAgent}' cannot hand off to other agents.");
        }

        return new HandoffPolicyDecision(
            false,
            $"Handoff policy: '{fromAgent}' can hand off only to {string.Join(", ", allowTargets)}.");
    }
}
