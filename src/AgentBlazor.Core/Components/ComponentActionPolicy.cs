using AgentBlazor.Licensing;

namespace AgentBlazor.Components;

public sealed record ComponentActionPolicyEvaluation(
    IReadOnlyList<ComponentCapability> AllowedComponents,
    IReadOnlyList<string> BlockedActionKeys,
    int TotalActionCount,
    int AllowedActionCount)
{
    public bool HasAllowedActions => AllowedActionCount > 0;
}

public static class ComponentActionPolicy
{
    public static ComponentActionPolicyEvaluation EvaluateAllowedCapabilities(
        IEnumerable<ComponentCapability> components,
        IReadOnlySet<string> allowedComponents,
        IReadOnlySet<string> allowedActions)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(allowedComponents);
        ArgumentNullException.ThrowIfNull(allowedActions);

        var filtered = new List<ComponentCapability>();
        var blockedActionKeys = new List<string>();
        var totalActionCount = 0;
        var allowedActionCount = 0;

        foreach (var component in components)
        {
            var isComponentAllowed = allowedComponents.Count == 0 || allowedComponents.Contains(component.ComponentId);
            var componentCopy = isComponentAllowed
                ? new ComponentCapability(component.ComponentId, component.Description)
                : null;

            foreach (var action in component.Actions)
            {
                totalActionCount++;
                if (!isComponentAllowed || !IsActionAllowed(allowedActions, component.ComponentId, action.ActionId))
                {
                    blockedActionKeys.Add(ToActionKey(component.ComponentId, action.ActionId));
                    continue;
                }

                componentCopy!.UpsertAction(action);
                allowedActionCount++;
            }

            if (componentCopy is not null && componentCopy.Actions.Count > 0)
            {
                filtered.Add(componentCopy);
            }
        }

        return new ComponentActionPolicyEvaluation(
            AllowedComponents: filtered,
            BlockedActionKeys: blockedActionKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TotalActionCount: totalActionCount,
            AllowedActionCount: allowedActionCount);
    }

    public static IReadOnlyList<ComponentCapability> FilterAllowedCapabilities(
        IEnumerable<ComponentCapability> components,
        IReadOnlySet<string> allowedComponents,
        IReadOnlySet<string> allowedActions)
        => EvaluateAllowedCapabilities(components, allowedComponents, allowedActions).AllowedComponents;

    public static ComponentActionPolicyEvaluation EvaluateEntitledCapabilities(
        IEnumerable<ComponentCapability> components,
        IAgentBlazorEntitlementService? entitlementService)
    {
        ArgumentNullException.ThrowIfNull(components);

        var filtered = new List<ComponentCapability>();
        var blockedActionKeys = new List<string>();
        var totalActionCount = 0;
        var allowedActionCount = 0;

        foreach (var component in components)
        {
            var componentCopy = new ComponentCapability(component.ComponentId, component.Description);

            foreach (var action in component.Actions)
            {
                totalActionCount++;
                var requiredTier = AgentComponentTierBoundaries.GetRequiredTier(component.ComponentId, action.ActionId);
                if (entitlementService is not null && !entitlementService.IsEnabled(requiredTier))
                {
                    blockedActionKeys.Add(ToActionKey(component.ComponentId, action.ActionId));
                    continue;
                }

                componentCopy.UpsertAction(action);
                allowedActionCount++;
            }

            if (componentCopy.Actions.Count > 0)
            {
                filtered.Add(componentCopy);
            }
        }

        return new ComponentActionPolicyEvaluation(
            AllowedComponents: filtered,
            BlockedActionKeys: blockedActionKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TotalActionCount: totalActionCount,
            AllowedActionCount: allowedActionCount);
    }

    public static bool IsActionAllowed(
        IReadOnlySet<string> allowedActions,
        string componentId,
        string actionId)
    {
        ArgumentNullException.ThrowIfNull(allowedActions);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        if (allowedActions.Count == 0)
        {
            return true;
        }

        return allowedActions.Contains(ToActionKey(componentId, actionId));
    }

    public static string SummarizeBlockedActions(IReadOnlyList<string> blockedActionKeys, int maxItems = 5)
    {
        ArgumentNullException.ThrowIfNull(blockedActionKeys);
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "maxItems must be greater than zero.");
        }

        if (blockedActionKeys.Count == 0)
        {
            return "none";
        }

        var preview = blockedActionKeys
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Take(maxItems)
            .ToArray();

        var summary = string.Join(", ", preview);
        var remaining = blockedActionKeys.Count - preview.Length;
        return remaining > 0
            ? $"{summary}, +{remaining} more"
            : summary;
    }

    public static string ToActionKey(string componentId, string actionId) =>
        $"{componentId}.{actionId}";
}
