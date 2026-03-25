using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Starter.Services;

namespace AgentBlazor.Starter.Workflows;

[AgentCapability("ops_review", Name = "Ops Review", Description = "Starter workflow for a route-scoped review flow.")]
public sealed class OpsReviewCapabilities(OpsReviewService workflow)
{
    [AgentAction("Assess the current review workflow")]
    public CapabilityResult AssessReview()
        => workflow.Assess();

    [AgentAction("Apply the recovery playbook")]
    public CapabilityResult ApplyRecoveryPlaybook()
        => workflow.ApplyRecoveryPlaybook();

    [AgentAction("Prepare the review draft", RequiresApproval = true)]
    public CapabilityResult PrepareReviewDraft()
        => workflow.PrepareDraft();

    [AgentAction("Reset the starter workflow")]
    public CapabilityResult ResetWorkflow()
        => workflow.Reset();
}
