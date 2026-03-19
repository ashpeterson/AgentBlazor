namespace AgentBlazor.App;

public sealed record AgentCapabilityDescriptor(
    string CapabilityId,
    string Name,
    string? Description,
    string? Category,
    IReadOnlyList<AgentCapabilityActionDescriptor> Actions);

public sealed record AgentCapabilityActionDescriptor(
    string CapabilityId,
    string ActionId,
    string LocalActionId,
    string Name,
    string Description,
    string? Category,
    bool RequiresApproval,
    bool IsAvailable,
    string InputSchema,
    IReadOnlyList<AgentCapabilityParameterDescriptor> Parameters);

public sealed record AgentCapabilityParameterDescriptor(
    string Name,
    string Type,
    bool Required,
    string? Description,
    IReadOnlyList<string>? AllowedValues);
