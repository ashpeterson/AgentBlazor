namespace AgentBlazor.Core.Runtime.Components;

public sealed record ComponentActionExecutionResult(
    string ComponentId,
    string ActionId,
    bool Succeeded,
    string Message);
