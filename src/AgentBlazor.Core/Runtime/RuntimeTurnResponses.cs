using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeTurnResponses
{
    public static AgentTurnResponse Build(
        string agentName,
        string responseText,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        AgentUiDocument? generatedUi = null,
        string? clarificationQuestion = null,
        IReadOnlyList<PendingApproval>? pendingApprovals = null,
        bool? requiresApproval = null,
        AgentExecutionPlan? executionPlan = null)
    {
        var effectiveClarification = clarificationQuestion
            ?? executionResults.FirstOrDefault(static result => result.Outcome is ActionOutcome.NeedsClarification)?.Message;
        var effectivePendingApprovals = pendingApprovals ?? [];

        var response = new AgentTurnResponse(
            agentName,
            responseText,
            plannedActions,
            executionResults)
        {
            RequiresClarification = !string.IsNullOrWhiteSpace(effectiveClarification),
            ClarificationQuestion = effectiveClarification,
            RequiresApproval = requiresApproval ?? effectivePendingApprovals.Count > 0,
            PendingApprovals = effectivePendingApprovals,
            ExecutionPlan = executionPlan
        };

        return RuntimeGeneratedUi.Attach(response, generatedUi);
    }
}
