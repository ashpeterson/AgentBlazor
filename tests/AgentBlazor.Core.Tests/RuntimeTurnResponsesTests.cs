using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimeTurnResponsesTests
{
    [Fact]
    public void Build_DerivesClarificationFromExecutionResults()
    {
        var response = RuntimeTurnResponses.Build(
            "agent",
            "Need more info.",
            [],
            [new ComponentActionExecutionResult("AgentGrid", "filter", ActionOutcome.NeedsClarification, "Which column?")]);

        Assert.True(response.RequiresClarification);
        Assert.Equal("Which column?", response.ClarificationQuestion);
    }

    [Fact]
    public void Build_PreservesPendingApprovalsAndGeneratedUi()
    {
        var approvals = new[]
        {
            new PendingApproval("AgentForm", "submit", "Submit form", new Dictionary<string, object?>())
        };
        var generatedUi = new AgentUiDocument
        {
            Blocks =
            [
                new AgentUiBlock
                {
                    Id = "summary",
                    Kind = AgentUiBlockKind.Card,
                    Title = "Summary"
                }
            ]
        };

        var response = RuntimeTurnResponses.Build(
            "agent",
            "Approval required.",
            [],
            [],
            generatedUi: generatedUi,
            pendingApprovals: approvals,
            requiresApproval: true);

        Assert.True(response.RequiresApproval);
        Assert.Same(approvals, response.PendingApprovals);
        Assert.Same(generatedUi, response.GeneratedUi);
    }

    [Fact]
    public void Build_ExposesLegacyCompatibilityPayload_OnlyWhenExecutionPlanIsMissing()
    {
        var legacyResponse = RuntimeTurnResponses.Build(
            "agent",
            "Legacy.",
            [new PlannedComponentAction("AgentGrid", "filter", "desc", new Dictionary<string, object?>())],
            [new ComponentActionExecutionResult("AgentGrid", "filter", ActionOutcome.Applied, "Applied.")]);

        Assert.False(legacyResponse.HasNormalizedExecutionPlan);
        Assert.True(legacyResponse.UsesLegacyCompatibilityPayload);
        Assert.Single(legacyResponse.LegacyPlannedActions);
        Assert.Single(legacyResponse.LegacyExecutionResults);

        var normalizedResponse = RuntimeTurnResponses.Build(
            "agent",
            "Normalized.",
            [],
            [],
            executionPlan: new AgentExecutionPlan(
                "agent",
                new AgentExecutionContext("session-1", "run-1"),
                [
                    new AgentExecutionStep(
                        "step-1",
                        0,
                        AgentExecutionStepKind.UiAction,
                        "AgentGrid",
                        "filter",
                        AgentExecutionStepStatus.Completed,
                        false,
                        new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.None))
                ]));

        Assert.True(normalizedResponse.HasNormalizedExecutionPlan);
        Assert.False(normalizedResponse.UsesLegacyCompatibilityPayload);
        Assert.Empty(normalizedResponse.LegacyPlannedActions);
        Assert.Empty(normalizedResponse.LegacyExecutionResults);
    }
}
