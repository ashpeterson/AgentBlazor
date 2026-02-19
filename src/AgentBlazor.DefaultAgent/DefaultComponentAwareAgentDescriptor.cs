namespace AgentBlazor.DefaultAgent;

public sealed record DefaultComponentAwareAgentDescriptor(
    string Name,
    string Description,
    IReadOnlyCollection<string> KnownComponents);
