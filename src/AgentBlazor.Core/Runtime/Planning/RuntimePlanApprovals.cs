using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Components;

namespace AgentBlazor.Core.Runtime.Planning;

internal static class RuntimePlanApprovals
{
    public static IReadOnlySet<string> BuildApprovedActions(
        ActionPlan plan,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyDictionary<string, string>? context)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null || context.Count == 0)
        {
            return approved;
        }

        foreach (var step in plan.Steps)
        {
            var action = FindAllowedAction(step, allowedComponents);
            if (action is null || !action.RequiresApproval)
            {
                continue;
            }

            if (ComponentActionApprovalPolicy.IsApprovalGranted(step.ComponentId, step.ActionId, context))
            {
                approved.Add($"{step.ComponentId}.{step.ActionId}");
            }
        }

        return approved;
    }

    public static IReadOnlyList<PendingApproval> BuildPendingApprovals(
        ActionPlan plan,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlySet<string> approvedActions)
    {
        var pending = new List<PendingApproval>();
        foreach (var step in plan.Steps)
        {
            var action = FindAllowedAction(step, allowedComponents);
            if (action is null || !action.RequiresApproval)
            {
                continue;
            }

            if (approvedActions.Contains($"{step.ComponentId}.{step.ActionId}"))
            {
                continue;
            }

            pending.Add(new PendingApproval(
                step.ComponentId,
                step.ActionId,
                action.Description,
                step.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                RuntimeTrustDecisions.BuildPolicyDecision(step.ComponentId, step.ActionId, requiresApproval: true)));
        }

        return pending;
    }

    public static PlanValidationContext BuildValidationContext(
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlySet<string> approvedActions)
        => new()
        {
            AllowedComponents = allowedComponents,
            MountedComponents = mountedComponents,
            ApprovedActions = approvedActions
        };

    public static string BuildApprovalRequiredResponseText(IReadOnlyList<PendingApproval> pending)
        => pending.Count == 1
            ? $"Approval required for {pending[0].ComponentId}.{pending[0].ActionId}."
            : $"Approval required for {pending.Count} actions.";

    private static AvailableAction? FindAllowedAction(
        PlannedStep step,
        IReadOnlyList<AvailableComponent> allowedComponents)
    {
        return allowedComponents
            .FirstOrDefault(component => string.Equals(component.ComponentId, step.ComponentId, StringComparison.OrdinalIgnoreCase))
            ?.Actions.FirstOrDefault(action => string.Equals(action.ActionId, step.ActionId, StringComparison.OrdinalIgnoreCase));
    }
}
