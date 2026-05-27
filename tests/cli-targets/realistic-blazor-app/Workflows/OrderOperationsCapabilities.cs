using AgentBlazor.App;
using AgentBlazor.Attributes;

[AgentCapability("order_operations", Name = "Order operations", Description = "Find delayed orders and prepare customer-safe updates.")]
public sealed class OrderOperationsCapabilities
{
    [AgentAction("Find delayed orders")]
    public Task<CapabilityResult> FindDelayedOrdersAsync()
        => Task.FromResult(CapabilityResult.Success("Found delayed orders."));

    [AgentAction("Release an order hold", RequiresApproval = true)]
    public Task<CapabilityResult> ReleaseOrderHoldAsync()
        => Task.FromResult(CapabilityResult.Success("Released order hold."));
}
