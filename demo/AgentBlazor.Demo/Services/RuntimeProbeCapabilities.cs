using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace AgentBlazor.Demo.Services;

[AgentCapability("runtime_probe", Name = "Runtime Probe")]
public sealed class RuntimeProbeCapabilities
{
    [AgentAction("Run the runtime approval probe", ActionId = "run_approval_probe", RequiresApproval = true)]
    public async Task<CapabilityResult> RunApprovalProbeAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        return CapabilityResult.Success("Runtime approval probe completed.")
            .WithOutput("probe", "APPROVED");
    }

    [AgentAction("Run the runtime cancellation probe", ActionId = "run_cancellation_probe")]
    public async Task<CapabilityResult> RunCancellationProbeAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

        return CapabilityResult.Success("Runtime cancellation probe completed.")
            .WithOutput("probe", "COMPLETED");
    }

    [AgentAction("Run the runtime reconnect probe", ActionId = "run_reconnect_probe")]
    public async Task<CapabilityResult> RunReconnectProbeAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);

        return CapabilityResult.Success("Runtime reconnect probe completed.")
            .WithOutput("probe", "RECONNECTED");
    }
}
