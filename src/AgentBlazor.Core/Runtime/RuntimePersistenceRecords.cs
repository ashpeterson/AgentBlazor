using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimePersistenceRecords
{
    public static Conversation.ConversationTurn CreateConversationTurn(
        string userMessage,
        AgentTurnResponse response)
    {
        return new()
        {
            Timestamp = DateTime.UtcNow,
            UserMessage = userMessage,
            AgentResponse = response.ResponseText,
            PlannedActions = response.LegacyPlannedActions,
            ExecutionResults = response.LegacyExecutionResults,
            ExecutionPlan = response.ExecutionPlan,
            GeneratedUi = response.GeneratedUi
        };
    }

    public static InspectorRunRecord CreateInspectorRunRecord(
        string runId,
        string sessionId,
        string agentName,
        DateTimeOffset startedAt,
        string? systemPrompt,
        string? rawPlanResponse,
        IReadOnlyList<InspectorEvent> events,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        AgentExecutionPlan? executionPlan,
        bool succeeded,
        string? errorMessage)
    {
        var planSteps = executionPlan?.Steps ?? [];
        var allEvents = new List<InspectorEvent>(events.Count + Math.Max(planSteps.Count, executionResults.Count));
        allEvents.AddRange(events);

        if (planSteps.Count > 0)
        {
            foreach (var step in planSteps)
            {
                allEvents.Add(new InspectorEvent(
                    DateTimeOffset.UtcNow,
                    MapExecutionStepEventKind(step.Status),
                    step.TargetId,
                    step.ActionId,
                    step.Message));
            }
        }
        else
        {
            foreach (var result in executionResults)
            {
                allEvents.Add(new InspectorEvent(
                    DateTimeOffset.UtcNow,
                    result.Succeeded ? "ToolCallResult" : "ToolCallFailed",
                    result.ComponentId,
                    result.ActionId,
                    result.Message));
            }
        }

        return new InspectorRunRecord(
            RunId: runId,
            SessionId: sessionId,
            AgentName: agentName,
            StartedAt: startedAt,
            FinishedAt: DateTimeOffset.UtcNow,
            SystemPrompt: systemPrompt,
            RawPlanResponse: rawPlanResponse,
            Events: allEvents,
            Succeeded: succeeded,
            ErrorMessage: errorMessage,
            ExecutionPlan: executionPlan);
    }

    private static string MapExecutionStepEventKind(AgentExecutionStepStatus status)
        => status switch
        {
            AgentExecutionStepStatus.Completed => "StepCompleted",
            AgentExecutionStepStatus.Failed => "StepFailed",
            AgentExecutionStepStatus.Blocked => "StepBlocked",
            AgentExecutionStepStatus.ApprovalRequired => "StepApprovalRequired",
            AgentExecutionStepStatus.NeedsClarification => "StepClarificationRequired",
            _ => "StepPending"
        };
}
