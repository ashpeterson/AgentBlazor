namespace AgentBlazor.Runtime;

public sealed record AgentTurnRequest(
    string UserMessage,
    string? AgentName = null,
    IDictionary<string, string>? Context = null);
