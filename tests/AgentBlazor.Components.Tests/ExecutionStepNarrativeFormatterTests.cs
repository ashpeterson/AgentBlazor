using AgentBlazor.Components.Chat;
using AgentBlazor.Execution;

namespace AgentBlazor.Components.Tests;

public sealed class ExecutionStepNarrativeFormatterTests
{
    [Fact]
    public void BuildResultTexts_IncludesPolicyReasonAndFallbackMessage_ForBlockedSteps()
    {
        var step = new AgentExecutionStep(
            "step-1",
            0,
            AgentExecutionStepKind.SemanticCapability,
            "supplier_compliance",
            "prepare_remediation",
            AgentExecutionStepStatus.Blocked,
            true,
            new AgentPolicyDecision(
                true,
                AgentRiskClass.SignificantMutation,
                AgentApprovalMode.PolicyDenied,
                "Compliance managers must approve remediation drafts."));

        var lines = ExecutionStepNarrativeFormatter.BuildResultTexts(step);

        Assert.Equal("Execution was blocked.", lines[0]);
        Assert.Equal("Policy: Compliance managers must approve remediation drafts.", lines[1]);
    }

    [Fact]
    public void BuildResultTexts_IncludesWarningsNextActionsAndOutputs()
    {
        var step = new AgentExecutionStep(
            "step-1",
            0,
            AgentExecutionStepKind.SemanticCapability,
            "file_audit_bundle",
            "prepare_audit_bundle",
            AgentExecutionStepStatus.Completed,
            false,
            new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.None),
            Message: "Bundle prepared.")
        {
            Warnings = ["Remote verification is still pending."],
            NextActions = ["Review the generated audit bundle."],
            Outputs = new Dictionary<string, object?>
            {
                ["mode"] = "Remote",
                ["files"] = 3
            }
        };

        var lines = ExecutionStepNarrativeFormatter.BuildResultTexts(step);

        Assert.Contains("Bundle prepared.", lines);
        Assert.Contains("Warning: Remote verification is still pending.", lines);
        Assert.Contains("Next: Review the generated audit bundle.", lines);
        Assert.Contains("Output: mode=Remote", lines);
        Assert.Contains("Output: files=3", lines);
    }
}
