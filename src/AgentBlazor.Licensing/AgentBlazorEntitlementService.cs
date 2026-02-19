namespace AgentBlazor.Licensing;

internal sealed class AgentBlazorEntitlementService(AgentBlazorTier tier) : IAgentBlazorEntitlementService
{
    public AgentBlazorTier CurrentTier { get; } = tier;

    public bool IsEnabled(AgentBlazorTier requiredTier) => CurrentTier >= requiredTier;
}
