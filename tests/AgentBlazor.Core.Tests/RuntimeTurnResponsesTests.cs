using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;

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
}
