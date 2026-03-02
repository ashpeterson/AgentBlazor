namespace AgentBlazor.Core.Runtime.Tools;

public sealed record AgentToolParameter(
    string Name,
    string Description,
    string Type = "string",
    bool Required = true);
