namespace AgentBlazor.Components;

public sealed record ComponentActionCapability(
    string ActionId,
    string Description,
    bool RequiresApproval = false,
    string? InputSchema = null);
