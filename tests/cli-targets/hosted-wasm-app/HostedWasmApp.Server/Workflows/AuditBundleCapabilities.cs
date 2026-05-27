using AgentBlazor.App;
using AgentBlazor.Attributes;

[AgentCapability("audit_bundle", Name = "Audit bundle", Description = "Prepare audit bundles for remote handoff.")]
public sealed class AuditBundleCapabilities
{
    [AgentAction("Summarize audit bundle")]
    public Task<CapabilityResult> SummarizeAuditBundleAsync()
        => Task.FromResult(CapabilityResult.Success("Audit bundle summarized."));

    [AgentAction("Prepare remote bundle", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareRemoteBundleAsync()
        => Task.FromResult(CapabilityResult.Success("Remote bundle prepared."));
}
