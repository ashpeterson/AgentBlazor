using AgentBlazor.Components;
using AgentBlazor.Licensing;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeTurnPreflight
{
    public static bool TryGetContextAgentName(
        IDictionary<string, string>? context,
        out string? agentName)
    {
        agentName = null;
        return context is not null &&
               context.TryGetValue(AgentRuntimeContextKeys.AgentName, out agentName) &&
               !string.IsNullOrWhiteSpace(agentName);
    }

    public static bool IsAgentLockRequested(IDictionary<string, string>? context)
    {
        if (context is null ||
            !context.TryGetValue(AgentRuntimeContextKeys.AgentLock, out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        return rawValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildNoAgentResponseText(
        int registeredCount,
        string? requestedAgentName,
        IDictionary<string, string>? context)
    {
        if (registeredCount == 0)
        {
            return "No agents are registered.";
        }

        if (!string.IsNullOrWhiteSpace(requestedAgentName))
        {
            return $"Requested agent '{requestedAgentName}' is not registered.";
        }

        if (context is not null &&
            context.TryGetValue(AgentRuntimeContextKeys.AgentName, out var contextAgentName) &&
            !string.IsNullOrWhiteSpace(contextAgentName))
        {
            return $"Requested agent '{contextAgentName}' is not registered.";
        }

        if (IsAgentLockRequested(context))
        {
            if (context is not null &&
                context.TryGetValue(AgentRuntimeContextKeys.CurrentRoute, out var currentRoute) &&
                !string.IsNullOrWhiteSpace(currentRoute))
            {
                return $"No route-locked agent is configured for '{currentRoute}'.";
            }

            return "Agent lock is enabled, but no matching registered agent could be resolved.";
        }

        return "No registered agent could be resolved for this request.";
    }

    public static string BuildNoAllowedActionsResponseText(
        IReadOnlyList<string> blockedByAgentPolicy,
        IReadOnlyList<string> blockedByTier,
        AgentBlazorTier effectiveTier,
        string actionLabel)
    {
        var allBlocked = blockedByAgentPolicy
            .Concat(blockedByTier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allBlocked.Length == 0)
        {
            return $"No allowed {actionLabel} are available for this agent policy.\n\nCurrent tier: {effectiveTier}";
        }

        var summary = ComponentActionPolicy.SummarizeBlockedActions(allBlocked);
        return
            $"No allowed {actionLabel} are available for this agent policy.\n\n" +
            $"Current tier: {effectiveTier}\n" +
            $"Filtered actions: {summary}";
    }
}
