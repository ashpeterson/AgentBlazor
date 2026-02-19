namespace AgentBlazor.Licensing;

public interface IAgentBlazorEntitlementService
{
    AgentBlazorTier CurrentTier { get; }

    bool IsEnabled(AgentBlazorTier requiredTier);
}
