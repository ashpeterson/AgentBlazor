using AgentBlazor.Agents;
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

        if (IsAgentLockRequested(context) &&
            context is not null &&
            context.TryGetValue(AgentRuntimeContextKeys.CurrentRoute, out var currentRoute) &&
            !string.IsNullOrWhiteSpace(currentRoute))
        {
            if (!string.IsNullOrWhiteSpace(requestedAgentName))
            {
                return $"Requested agent '{requestedAgentName}' is not configured for route '{currentRoute}'.";
            }

            if (context.TryGetValue(AgentRuntimeContextKeys.AgentName, out var lockedAgentName) &&
                !string.IsNullOrWhiteSpace(lockedAgentName))
            {
                return $"Requested agent '{lockedAgentName}' is not configured for route '{currentRoute}'.";
            }

            return $"No route-locked agent is configured for '{currentRoute}'.";
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
            return "Agent lock is enabled, but no matching registered agent could be resolved.";
        }

        return "No registered agent could be resolved for this request.";
    }

    public static bool AllowsLockedRoute(AgentRegistration registration, IDictionary<string, string>? context)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!IsAgentLockRequested(context) ||
            context is null ||
            !context.TryGetValue(AgentRuntimeContextKeys.CurrentRoute, out var currentRoute) ||
            string.IsNullOrWhiteSpace(currentRoute))
        {
            return true;
        }

        var patterns = EnumerateRoutePatterns(registration).ToArray();
        if (patterns.Length == 0)
        {
            return true;
        }

        var normalizedRoute = NormalizeRoute(currentRoute);
        if (string.IsNullOrWhiteSpace(normalizedRoute))
        {
            return true;
        }

        return patterns.Any(pattern => RouteMatches(normalizedRoute, pattern));
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

    public static AgentRegistration? ResolveImplicitFallbackAgent(
        IEnumerable<AgentRegistration> registrations)
    {
        return registrations
            .OrderBy(static agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateRoutePatterns(AgentRegistration registration)
    {
        foreach (var key in new[] { "route", "routes", "route_prefix", "route_prefixes" })
        {
            if (!registration.Metadata.TryGetValue(key, out var rawValue) ||
                string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            foreach (var token in rawValue.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = NormalizeRoute(token);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    yield return normalized;
                }
            }
        }
    }

    private static bool RouteMatches(string route, string pattern)
        => string.Equals(route, pattern, StringComparison.OrdinalIgnoreCase) ||
           route.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var normalized = route.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
        {
            normalized = absolute.AbsolutePath;
        }

        var queryOrFragmentIndex = normalized.IndexOfAny(['?', '#']);
        if (queryOrFragmentIndex >= 0)
        {
            normalized = normalized[..queryOrFragmentIndex];
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        normalized = normalized.TrimEnd('/');
        return normalized.Length == 0
            ? "/"
            : normalized.ToLowerInvariant();
    }
}
