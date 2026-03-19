using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Tests;

public class RuntimeExecutionPlansTests
{
    [Fact]
    public void Build_MapsSemanticAndUiStepStatuses()
    {
        var plan = RuntimeExecutionPlans.Build(
            agentName: "supplier-agent",
            sessionId: "session-1",
            runId: "run-1",
            userId: "user-1",
            route: "/suppliers",
            contextVersion: "ctx-1",
            plannedActions:
            [
                new PlannedComponentAction(
                    "supplier_compliance",
                    "show_at_risk_suppliers",
                    "semantic"),
                new PlannedComponentAction(
                    "AgentForm",
                    "submit",
                    "ui")
            ],
            executionResults:
            [
                new ComponentActionExecutionResult(
                    "supplier_compliance",
                    "show_at_risk_suppliers",
                    ActionOutcome.Applied,
                    "Prepared a review.")
            ],
            pendingApprovals:
            [
                new PendingApproval(
                    "AgentForm",
                    "submit",
                    "Submit the form",
                    new Dictionary<string, object?>())
            ]);

        Assert.Equal("supplier-agent", plan.AgentName);
        Assert.Equal("session-1", plan.Context.SessionId);
        Assert.Equal("run-1", plan.Context.RunId);
        Assert.Equal("user-1", plan.Context.UserId);
        Assert.Equal("/suppliers", plan.Context.Route);
        Assert.Equal("ctx-1", plan.Context.ContextVersion);
        Assert.Equal(AgentContextFreshness.Current, plan.Context.Freshness);
        Assert.True(plan.RequiresApproval);

        Assert.Collection(
            plan.Steps,
            semantic =>
            {
                Assert.Equal(AgentExecutionStepKind.SemanticCapability, semantic.Kind);
                Assert.Equal(AgentExecutionStepStatus.Completed, semantic.Status);
                Assert.True(semantic.PolicyDecision.Allowed);
                Assert.Equal(AgentRiskClass.ReadOnly, semantic.PolicyDecision.RiskClass);
                Assert.Equal(AgentApprovalMode.None, semantic.PolicyDecision.ApprovalMode);
                Assert.Equal("supplier_compliance", semantic.TargetId);
                Assert.Equal("show_at_risk_suppliers", semantic.ActionId);
            },
            ui =>
            {
                Assert.Equal(AgentExecutionStepKind.UiAction, ui.Kind);
                Assert.Equal(AgentExecutionStepStatus.ApprovalRequired, ui.Status);
                Assert.True(ui.RequiresApproval);
                Assert.True(ui.PolicyDecision.Allowed);
                Assert.Equal(AgentRiskClass.SignificantMutation, ui.PolicyDecision.RiskClass);
                Assert.Equal(AgentApprovalMode.ExplicitPlanApproval, ui.PolicyDecision.ApprovalMode);
                Assert.Equal("AgentForm", ui.TargetId);
                Assert.Equal("submit", ui.ActionId);
            });
    }
}
