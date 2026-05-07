using AgentBlazor.Components.Chat;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Components.Tests;

public sealed class ExecutionPlanNarrativeFormatterTests
{
    [Fact]
    public void TryBuildPlanSummary_IncludesMutationApprovalRouteAndContext_ForExecutionPlans()
    {
        var plan = new AgentExecutionPlan(
            "RiskAgent",
            new AgentExecutionContext(
                "session-1",
                "run-1",
                Route: "/demo/suppliers",
                Freshness: AgentContextFreshness.Current),
            [
                new AgentExecutionStep(
                    "step-1",
                    0,
                    AgentExecutionStepKind.SemanticCapability,
                    "supplier_compliance",
                    "show_at_risk_suppliers",
                    AgentExecutionStepStatus.Completed,
                    false,
                    new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.None)),
                new AgentExecutionStep(
                    "step-2",
                    1,
                    AgentExecutionStepKind.UiAction,
                    "supplier-grid",
                    "highlight_rows",
                    AgentExecutionStepStatus.ApprovalRequired,
                    true,
                    new AgentPolicyDecision(
                        true,
                        AgentRiskClass.SignificantMutation,
                        AgentApprovalMode.ExplicitPlanApproval))
            ]);

        var success = ExecutionPlanNarrativeFormatter.TryBuildPlanSummary(plan, [], out var summary);

        Assert.True(success);
        Assert.Equal("Plan: 2 steps • 1 mutating • 1 approval • route /demo/suppliers • context current", summary);
    }

    [Fact]
    public void TryBuildPlanSummary_FallsBackToPlannedActions_WhenExecutionPlanIsMissing()
    {
        var plannedActions = new[]
        {
            new PlannedComponentAction("grid", "filter", "Filter the current grid."),
            new PlannedComponentAction("dialog", "open", "Open the details dialog.")
        };

        var success = ExecutionPlanNarrativeFormatter.TryBuildPlanSummary(null, plannedActions, out var summary);

        Assert.True(success);
        Assert.Equal("Plan: 2 actions.", summary);
    }

    [Fact]
    public void BuildStepLabels_UsesSharedExecutionStepNarrative()
    {
        var plan = new AgentExecutionPlan(
            "RiskAgent",
            new AgentExecutionContext("session-1", "run-1"),
            [
                new AgentExecutionStep(
                    "step-1",
                    0,
                    AgentExecutionStepKind.SemanticCapability,
                    "supplier_compliance",
                    "prepare_remediation",
                    AgentExecutionStepStatus.ApprovalRequired,
                    true,
                    new AgentPolicyDecision(
                        true,
                        AgentRiskClass.SignificantMutation,
                        AgentApprovalMode.ExplicitPlanApproval))
            ]);

        var labels = ExecutionPlanNarrativeFormatter.BuildStepLabels(plan, []);

        Assert.Equal(["Approve: supplier_compliance.prepare_remediation (awaiting approval)"], labels);
    }

    [Fact]
    public void BuildStepLabels_LabelsQueuedSteps()
    {
        var plan = new AgentExecutionPlan(
            "RiskAgent",
            new AgentExecutionContext("session-1", "run-1"),
            [
                new AgentExecutionStep(
                    "step-1",
                    0,
                    AgentExecutionStepKind.UiAction,
                    "AgentDialog",
                    "open",
                    AgentExecutionStepStatus.Queued,
                    false,
                    new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.None))
            ]);

        var labels = ExecutionPlanNarrativeFormatter.BuildStepLabels(plan, []);

        Assert.Equal(["UI: AgentDialog.open (queued)"], labels);
    }

    [Fact]
    public void ApprovalHelpers_BuildSharedSummaryTitleAndPolicyNarrative()
    {
        var summary = ExecutionPlanNarrativeFormatter.BuildApprovalSummary(
        [
            new AgentPolicyDecision(
                true,
                AgentRiskClass.SensitiveMutation,
                AgentApprovalMode.StepApproval,
                "Batch release requires supervisor review."),
            new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.InlineConfirm)
        ]);

        var title = ExecutionPlanNarrativeFormatter.BuildApprovalDisplayTitle(
            "supplier-grid",
            "apply_filter",
            "");

        var policy = ExecutionPlanNarrativeFormatter.BuildApprovalPolicySummary(
            new AgentPolicyDecision(
                true,
                AgentRiskClass.SensitiveMutation,
                AgentApprovalMode.StepApproval,
                "Batch release requires supervisor review."));

        Assert.Equal("2 steps are waiting for approval. highest risk: sensitive mutation.", summary);
        Assert.Equal("supplier-grid.apply_filter", title);
        Assert.Equal("sensitive mutation • step approval • Batch release requires supervisor review.", policy);
    }

    [Fact]
    public void TryGetApprovalStepLabel_ResolvesNormalizedStepLabel_FromExecutionPlan()
    {
        var plan = new AgentExecutionPlan(
            "RiskAgent",
            new AgentExecutionContext("session-1", "run-1"),
            [
                new AgentExecutionStep(
                    "step-1",
                    0,
                    AgentExecutionStepKind.SemanticCapability,
                    "recipe_release",
                    "prepare_release_draft",
                    AgentExecutionStepStatus.ApprovalRequired,
                    true,
                    new AgentPolicyDecision(
                        true,
                        AgentRiskClass.SignificantMutation,
                        AgentApprovalMode.ExplicitPlanApproval))
            ]);

        var success = ExecutionPlanNarrativeFormatter.TryGetApprovalStepLabel(
            plan,
            "recipe_release",
            "prepare_release_draft",
            out var label);

        Assert.True(success);
        Assert.Equal("Approve: recipe_release.prepare_release_draft (awaiting approval)", label);
    }
}
