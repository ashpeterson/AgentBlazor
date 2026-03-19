using AgentBlazor.Components;
using AgentBlazor.Licensing;

namespace AgentBlazor.Core.Runtime;

internal sealed record RuntimeCapabilityPolicyResult(
    IReadOnlyList<ComponentCapability> AllowedCapabilities,
    IReadOnlyList<string> BlockedByAgentPolicy,
    IReadOnlyList<string> BlockedByTier);

internal static class RuntimeCapabilityPolicy
{
    public static RuntimeCapabilityPolicyResult Evaluate(
        IEnumerable<ComponentCapability> components,
        IReadOnlySet<string> allowedComponents,
        IReadOnlySet<string> allowedActions,
        AgentBlazorTier effectiveTier)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(allowedComponents);
        ArgumentNullException.ThrowIfNull(allowedActions);

        var agentPolicyEvaluation = ComponentActionPolicy.EvaluateAllowedCapabilities(
            components,
            allowedComponents,
            allowedActions);

        var tierFiltered = new List<ComponentCapability>();
        var blockedByTier = new List<string>();

        foreach (var component in agentPolicyEvaluation.AllowedComponents)
        {
            var componentCopy = new ComponentCapability(component.ComponentId, component.Description);
            foreach (var action in component.Actions)
            {
                var requiredTier = AgentComponentTierBoundaries.GetRequiredTier(component.ComponentId, action.ActionId);
                if (effectiveTier < requiredTier)
                {
                    blockedByTier.Add(ComponentActionPolicy.ToActionKey(component.ComponentId, action.ActionId));
                    continue;
                }

                componentCopy.UpsertAction(action);
            }

            if (componentCopy.Actions.Count > 0)
            {
                tierFiltered.Add(componentCopy);
            }
        }

        return new RuntimeCapabilityPolicyResult(
            tierFiltered,
            agentPolicyEvaluation.BlockedActionKeys,
            blockedByTier
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static actionKey => actionKey, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
