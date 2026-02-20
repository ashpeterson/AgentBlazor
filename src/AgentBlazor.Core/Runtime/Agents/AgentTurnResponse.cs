using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Agents;

public sealed record AgentTurnResponse(
    string AgentName,
    string ResponseText,
    IReadOnlyList<PlannedComponentAction> PlannedActions,
    IReadOnlyList<ComponentActionExecutionResult> ExecutionResults);
