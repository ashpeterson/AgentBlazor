using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Execution;

namespace AgentBlazor.Components.Chat;

internal static class ExecutionPlanNarrativeFormatter
{
    public static IReadOnlyList<string> BuildStepLabels(
        AgentExecutionPlan? plan,
        IReadOnlyList<PlannedComponentAction> plannedActions)
    {
        if (plan?.Steps.Count > 0)
        {
            return plan.Steps
                .Select(FormatExecutionStepLabel)
                .ToArray();
        }

        if (plannedActions.Count == 0)
        {
            return [];
        }

        return plannedActions
            .Select(static action => $"{action.ComponentId}.{action.ActionId}")
            .ToArray();
    }

    public static bool TryBuildPlanSummary(
        AgentExecutionPlan? plan,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        out string summary)
    {
        if (plan is { Steps.Count: > 0 })
        {
            summary = BuildExecutionPlanSummary(plan);
            return true;
        }

        if (plannedActions.Count > 0)
        {
            summary = plannedActions.Count == 1
                ? "Plan: 1 action."
                : $"Plan: {plannedActions.Count} actions.";
            return true;
        }

        summary = string.Empty;
        return false;
    }

    public static string BuildApprovalSummary(IEnumerable<AgentPolicyDecision?> policyDecisions)
    {
        var decisions = policyDecisions.ToArray();
        if (decisions.Length == 0)
        {
            return "No approvals pending.";
        }

        var highestRisk = decisions
            .Select(static decision => decision?.RiskClass ?? AgentRiskClass.Unknown)
            .Max();

        var riskSummary = highestRisk switch
        {
            AgentRiskClass.SensitiveMutation => "highest risk: sensitive mutation",
            AgentRiskClass.SignificantMutation => "highest risk: significant mutation",
            AgentRiskClass.LowRiskMutation => "highest risk: low-risk mutation",
            AgentRiskClass.ReadOnly => "read-only approval",
            AgentRiskClass.RestrictedAction => "restricted action",
            _ => null
        };

        var baseSummary = decisions.Length == 1
            ? "1 step is waiting for approval."
            : $"{decisions.Length} steps are waiting for approval.";

        return string.IsNullOrWhiteSpace(riskSummary)
            ? baseSummary
            : $"{baseSummary} {riskSummary}.";
    }

    public static string BuildApprovalDisplayTitle(
        string componentId,
        string actionId,
        string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? $"{componentId}.{actionId}"
            : description;

    public static bool TryGetApprovalStepLabel(
        AgentExecutionPlan? plan,
        string targetId,
        string actionId,
        out string label)
    {
        var step = FindStep(plan, targetId, actionId);
        if (step is null)
        {
            label = string.Empty;
            return false;
        }

        label = FormatExecutionStepLabel(step);
        return true;
    }

    public static AgentExecutionStep? FindStep(
        AgentExecutionPlan? plan,
        string targetId,
        string actionId)
    {
        return plan?.Steps.FirstOrDefault(step =>
            string.Equals(step.TargetId, targetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(step.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildApprovalPolicySummary(AgentPolicyDecision policyDecision)
    {
        var parts = new List<string>(2)
        {
            policyDecision.RiskClass switch
            {
                AgentRiskClass.ReadOnly => "read-only",
                AgentRiskClass.LowRiskMutation => "low-risk mutation",
                AgentRiskClass.SignificantMutation => "significant mutation",
                AgentRiskClass.SensitiveMutation => "sensitive mutation",
                AgentRiskClass.RestrictedAction => "restricted action",
                _ => "unknown risk"
            },
            policyDecision.ApprovalMode switch
            {
                AgentApprovalMode.InlineConfirm => "inline confirm",
                AgentApprovalMode.ExplicitPlanApproval => "plan approval",
                AgentApprovalMode.StepApproval => "step approval",
                AgentApprovalMode.PolicyDenied => "policy denied",
                _ => "no approval mode"
            }
        };

        if (!string.IsNullOrWhiteSpace(policyDecision.Reason))
        {
            parts.Add(policyDecision.Reason);
        }

        return string.Join(" • ", parts);
    }

    private static string BuildExecutionPlanSummary(AgentExecutionPlan plan)
    {
        var approvalCount = plan.Steps.Count(static step => step.RequiresApproval);
        var mutationCount = plan.Steps.Count(static step =>
            step.PolicyDecision.RiskClass is not AgentRiskClass.ReadOnly);

        var parts = new List<string>
        {
            plan.Steps.Count == 1
                ? "Plan: 1 step"
                : $"Plan: {plan.Steps.Count} steps"
        };

        if (mutationCount > 0)
        {
            parts.Add(mutationCount == 1 ? "1 mutating" : $"{mutationCount} mutating");
        }

        if (approvalCount > 0)
        {
            parts.Add(approvalCount == 1 ? "1 approval" : $"{approvalCount} approvals");
        }

        if (!string.IsNullOrWhiteSpace(plan.Context.Route))
        {
            parts.Add($"route {plan.Context.Route}");
        }

        parts.Add($"context {plan.Context.Freshness.ToString().ToLowerInvariant()}");
        return string.Join(" • ", parts);
    }

    private static string FormatExecutionStepLabel(AgentExecutionStep step)
    {
        var prefix = step.RequiresApproval
            ? "Approve"
            : step.Kind switch
            {
                AgentExecutionStepKind.SemanticCapability => "Capability",
                AgentExecutionStepKind.ServiceTool => "Tool",
                AgentExecutionStepKind.GeneratedUiTool => "UI Block",
                _ => "UI"
            };

        var status = step.Status switch
        {
            AgentExecutionStepStatus.ApprovalRequired => "awaiting approval",
            AgentExecutionStepStatus.Completed => "done",
            AgentExecutionStepStatus.NeedsClarification => "needs clarification",
            AgentExecutionStepStatus.Blocked => "blocked",
            AgentExecutionStepStatus.Failed => "failed",
            _ => "planned"
        };

        return $"{prefix}: {step.TargetId}.{step.ActionId} ({status})";
    }
}
