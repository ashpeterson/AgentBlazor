namespace AgentBlazor.Runtime;

public sealed record ComponentActionExecutionResult(
    string ComponentId,
    string ActionId,
    bool Succeeded,
    string Message);
