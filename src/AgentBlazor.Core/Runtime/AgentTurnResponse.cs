namespace AgentBlazor.Runtime;

public sealed record AgentTurnResponse(
    string AgentName,
    string ResponseText,
    IReadOnlyList<PlannedComponentAction> PlannedActions,
    IReadOnlyList<ComponentActionExecutionResult> ExecutionResults);
