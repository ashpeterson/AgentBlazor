namespace AgentBlazor.Core.Runtime.Agents;

public sealed record AgentTurnRequest(
    string UserMessage,
    string? AgentName = null,
    IDictionary<string, string>? Context = null);
