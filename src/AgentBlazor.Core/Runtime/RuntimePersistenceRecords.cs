using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimePersistenceRecords
{
    public static Conversation.ConversationTurn CreateConversationTurn(
        string userMessage,
        AgentTurnResponse response)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            UserMessage = userMessage,
            AgentResponse = response.ResponseText,
            PlannedActions = response.PlannedActions,
            ExecutionResults = response.ExecutionResults,
            ExecutionPlan = response.ExecutionPlan,
            GeneratedUi = response.GeneratedUi
        };

    public static InspectorRunRecord CreateInspectorRunRecord(
        string runId,
        string sessionId,
        string agentName,
        DateTimeOffset startedAt,
        string? systemPrompt,
        string? rawPlanResponse,
        IReadOnlyList<InspectorEvent> events,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        bool succeeded,
        string? errorMessage)
    {
        var allEvents = new List<InspectorEvent>(events.Count + executionResults.Count);
        allEvents.AddRange(events);

        foreach (var result in executionResults)
        {
            allEvents.Add(new InspectorEvent(
                DateTimeOffset.UtcNow,
                result.Succeeded ? "ToolCallResult" : "ToolCallFailed",
                result.ComponentId,
                result.ActionId,
                result.Message));
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
            ErrorMessage: errorMessage);
    }
}
