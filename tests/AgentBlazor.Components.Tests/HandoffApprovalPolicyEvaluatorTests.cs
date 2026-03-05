using AgentBlazor.Components.Chat;

namespace AgentBlazor.Components.Tests;

public class HandoffApprovalPolicyEvaluatorTests
{
    [Fact]
    public void ShouldRequireApproval_UsesDefault_WhenPolicyMissing()
    {
        var required = HandoffApprovalPolicyEvaluator.ShouldRequireApproval(
            defaultRequireApproval: true,
            fromAgent: "A",
            toAgent: "B",
            handoffApprovalPolicy: null);

        Assert.True(required);
    }

    [Fact]
    public void ShouldRequireApproval_WildcardRule_RequiresApproval()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["*"] = ["*"]
            };

        var required = HandoffApprovalPolicyEvaluator.ShouldRequireApproval(
            defaultRequireApproval: false,
            fromAgent: "Dojo",
            toAgent: "Workflow",
            handoffApprovalPolicy: policy);

        Assert.True(required);
    }

    [Fact]
    public void ShouldRequireApproval_ExplicitTarget_RequiresApproval()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Workflow"] = ["Supplier"]
            };

        var required = HandoffApprovalPolicyEvaluator.ShouldRequireApproval(
            defaultRequireApproval: false,
            fromAgent: "Workflow",
            toAgent: "Supplier",
            handoffApprovalPolicy: policy);

        Assert.True(required);
    }

    [Fact]
    public void ShouldRequireApproval_DenyRule_OverridesRequirement()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Workflow"] = ["*", "!Dojo"]
            };

        var dojoRequired = HandoffApprovalPolicyEvaluator.ShouldRequireApproval(
            defaultRequireApproval: true,
            fromAgent: "Workflow",
            toAgent: "Dojo",
            handoffApprovalPolicy: policy);
        var supplierRequired = HandoffApprovalPolicyEvaluator.ShouldRequireApproval(
            defaultRequireApproval: true,
            fromAgent: "Workflow",
            toAgent: "Supplier",
            handoffApprovalPolicy: policy);

        Assert.False(dojoRequired);
        Assert.True(supplierRequired);
    }
}
