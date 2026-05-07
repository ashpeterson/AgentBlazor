using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeExecutionPlans
{
    public static AgentExecutionPlan Build(
        string agentName,
        string sessionId,
        string runId,
        string? userId,
        string? route,
        string? contextVersion,
        IReadOnlyList<AgentExecutionStep> steps)
    {
        return new AgentExecutionPlan(
            agentName,
            new AgentExecutionContext(
                sessionId,
                runId,
                userId,
                route,
                contextVersion,
                RuntimeTrustDecisions.ResolveFreshness(contextVersion)),
            steps);
    }

    public static AgentExecutionPlan Build(
        string agentName,
        string sessionId,
        string runId,
        string? userId,
        string? route,
        string? contextVersion,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<PendingApproval> pendingApprovals)
    {
        var steps = new List<AgentExecutionStep>(plannedActions.Count);
        var remainingResults = executionResults.ToList();

        for (var index = 0; index < plannedActions.Count; index++)
        {
            var action = plannedActions[index];
            var approval = pendingApprovals.FirstOrDefault(p =>
                string.Equals(p.ComponentId, action.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ActionId, action.ActionId, StringComparison.OrdinalIgnoreCase));
            var resultIndex = remainingResults.FindIndex(result =>
                string.Equals(result.ComponentId, action.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(result.ActionId, action.ActionId, StringComparison.OrdinalIgnoreCase));
            var result = resultIndex >= 0 ? remainingResults[resultIndex] : null;
            if (resultIndex >= 0)
            {
                remainingResults.RemoveAt(resultIndex);
            }

            steps.Add(new AgentExecutionStep(
                StepId: $"{index + 1}:{action.ComponentId}.{action.ActionId}",
                Order: index + 1,
                Kind: ResolveStepKind(action.ComponentId),
                TargetId: action.ComponentId,
                ActionId: action.ActionId,
                Status: ResolveStatus(result, approval is not null),
                RequiresApproval: approval is not null,
                PolicyDecision: RuntimeTrustDecisions.BuildPolicyDecision(action.ComponentId, action.ActionId, approval is not null),
                Arguments: action.Arguments,
                Message: approval?.Description ?? result?.Message,
                Outputs: null,
                Warnings: null,
                NextActions: null));
        }

        return new AgentExecutionPlan(
            agentName,
            new AgentExecutionContext(
                sessionId,
                runId,
                userId,
                route,
                contextVersion,
                RuntimeTrustDecisions.ResolveFreshness(contextVersion)),
            steps);
    }

    private static AgentExecutionStepKind ResolveStepKind(string targetId)
    {
        if (targetId.StartsWith("Agent", StringComparison.OrdinalIgnoreCase))
        {
            return AgentExecutionStepKind.UiAction;
        }

        return AgentExecutionStepKind.SemanticCapability;
    }

    private static AgentExecutionStepStatus ResolveStatus(
        ComponentActionExecutionResult? result,
        bool requiresApproval)
    {
        if (requiresApproval)
        {
            return AgentExecutionStepStatus.ApprovalRequired;
        }

        if (result is null)
        {
            return AgentExecutionStepStatus.Pending;
        }

        return result.Outcome switch
        {
            ActionOutcome.Applied => AgentExecutionStepStatus.Completed,
            ActionOutcome.Queued => AgentExecutionStepStatus.Queued,
            ActionOutcome.NeedsClarification => AgentExecutionStepStatus.NeedsClarification,
            ActionOutcome.Blocked => AgentExecutionStepStatus.Blocked,
            ActionOutcome.Failed => AgentExecutionStepStatus.Failed,
            _ => AgentExecutionStepStatus.Pending
        };
    }
}
