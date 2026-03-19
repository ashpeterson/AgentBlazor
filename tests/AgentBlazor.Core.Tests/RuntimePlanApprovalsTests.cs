using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Planning;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimePlanApprovalsTests
{
    [Fact]
    public void BuildApprovedActions_ReturnsOnlyApprovedApprovalGatedSteps()
    {
        var plan = new ActionPlan
        {
            Steps =
            [
                CreateStep("AgentForm", "submit"),
                CreateStep("AgentDialog", "open")
            ]
        };

        var allowedComponents = new[]
        {
            CreateComponent("AgentForm", CreateAction("submit", requiresApproval: true)),
            CreateComponent("AgentDialog", CreateAction("open", requiresApproval: false))
        };

        var approved = RuntimePlanApprovals.BuildApprovedActions(
            plan,
            allowedComponents,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentblazor.approval.AgentForm.submit"] = "true"
            });

        Assert.Single(approved);
        Assert.Contains("AgentForm.submit", approved);
    }

    [Fact]
    public void BuildPendingApprovals_ReturnsOnlyUnapprovedApprovalGatedSteps()
    {
        var plan = new ActionPlan
        {
            Steps =
            [
                CreateStep("AgentForm", "submit", ("field", "Status")),
                CreateStep("AgentDialog", "open")
            ]
        };

        var allowedComponents = new[]
        {
            CreateComponent("AgentForm", CreateAction("submit", requiresApproval: true, description: "Submit form")),
            CreateComponent("AgentDialog", CreateAction("open", requiresApproval: false, description: "Open dialog"))
        };

        var pending = RuntimePlanApprovals.BuildPendingApprovals(
            plan,
            allowedComponents,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var approval = Assert.Single(pending);
        Assert.Equal("AgentForm", approval.ComponentId);
        Assert.Equal("submit", approval.ActionId);
        Assert.Equal("Submit form", approval.Description);
        Assert.Equal("Status", Assert.IsType<string>(approval.Parameters["field"]));
        Assert.NotNull(approval.PolicyDecision);
        Assert.True(approval.PolicyDecision!.Allowed);
        Assert.Equal(AgentRiskClass.SignificantMutation, approval.PolicyDecision.RiskClass);
        Assert.Equal(AgentApprovalMode.ExplicitPlanApproval, approval.PolicyDecision.ApprovalMode);
    }

    [Fact]
    public void BuildValidationContext_PopulatesExpectedFields()
    {
        var allowedComponents = new[] { CreateComponent("AgentTabs", CreateAction("switch")) };
        var mountedComponents = new[]
        {
            new MountedComponentState
            {
                AgentId = "tabs-1",
                ComponentType = "AgentTabs"
            }
        };
        var approvedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AgentTabs.switch"
        };

        var context = RuntimePlanApprovals.BuildValidationContext(
            allowedComponents,
            mountedComponents,
            approvedActions);

        Assert.Same(allowedComponents, context.AllowedComponents);
        Assert.Same(mountedComponents, context.MountedComponents);
        Assert.Same(approvedActions, context.ApprovedActions);
    }

    [Fact]
    public void BuildApprovalRequiredResponseText_FormatsSingleAndMultipleCounts()
    {
        var single = RuntimePlanApprovals.BuildApprovalRequiredResponseText(
            [new PendingApproval("AgentForm", "submit", "Submit form", new Dictionary<string, object?>())]);
        var multiple = RuntimePlanApprovals.BuildApprovalRequiredResponseText(
        [
            new PendingApproval("AgentForm", "submit", "Submit form", new Dictionary<string, object?>()),
            new PendingApproval("AgentDialog", "confirm", "Confirm dialog", new Dictionary<string, object?>())
        ]);

        Assert.Equal("Approval required for AgentForm.submit.", single);
        Assert.Equal("Approval required for 2 actions.", multiple);
    }

    private static ActionPlan CreatePlan(params PlannedStep[] steps) => new()
    {
        Steps = steps
    };

    private static PlannedStep CreateStep(string componentId, string actionId, params (string Key, object? Value)[] arguments)
        => new()
        {
            ComponentId = componentId,
            ActionId = actionId,
            Arguments = arguments.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };

    private static AvailableComponent CreateComponent(string componentId, params AvailableAction[] actions)
        => new()
        {
            ComponentId = componentId,
            Description = componentId,
            Actions = actions
        };

    private static AvailableAction CreateAction(string actionId, bool requiresApproval = false, string? description = null)
        => new()
        {
            ActionId = actionId,
            Description = description ?? actionId,
            Parameters = [],
            RequiresApproval = requiresApproval
        };
}
