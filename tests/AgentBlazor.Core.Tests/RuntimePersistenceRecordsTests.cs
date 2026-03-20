using AgentBlazor.Core.Components;
using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimePersistenceRecordsTests
{
    [Fact]
    public void CreateConversationTurn_MapsResponseFields()
    {
        var response = new AgentTurnResponse(
            "agent",
            "done",
            [new PlannedComponentAction("AgentTabs", "switch", "desc", new Dictionary<string, object?>())],
            [new ComponentActionExecutionResult("AgentTabs", "switch", ActionOutcome.Applied, "Applied.")])
        {
            ExecutionPlan = new AgentExecutionPlan(
                "agent",
                new AgentExecutionContext("session-1", "run-1"),
                [
                    new AgentExecutionStep(
                        "step-1",
                        0,
                        AgentExecutionStepKind.UiAction,
                        "AgentTabs",
                        "switch",
                        AgentExecutionStepStatus.Completed,
                        false,
                        new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.None),
                        Message: "Applied.")
                ]),
            GeneratedUi = new AgentUiDocument
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
            }
        };

        var turn = RuntimePersistenceRecords.CreateConversationTurn("switch tabs", response);

        Assert.Equal("switch tabs", turn.UserMessage);
        Assert.Equal("done", turn.AgentResponse);
        Assert.False(turn.UsesLegacyCompatibilityPayload);
        Assert.Empty(turn.LegacyPlannedActions);
        Assert.Empty(turn.LegacyExecutionResults);
        Assert.NotNull(turn.ExecutionPlan);
        Assert.Single(turn.ExecutionPlan!.Steps);
        Assert.NotNull(turn.GeneratedUi);
    }

    [Fact]
    public void CreateConversationTurn_PreservesLegacyPayloads_WhenExecutionPlanIsMissing()
    {
        var response = new AgentTurnResponse(
            "agent",
            "done",
            [new PlannedComponentAction("AgentTabs", "switch", "desc", new Dictionary<string, object?>())],
            [new ComponentActionExecutionResult("AgentTabs", "switch", ActionOutcome.Applied, "Applied.")]);

        var turn = RuntimePersistenceRecords.CreateConversationTurn("switch tabs", response);

        Assert.True(turn.UsesLegacyCompatibilityPayload);
        Assert.Single(turn.LegacyPlannedActions);
        Assert.Single(turn.LegacyExecutionResults);
        Assert.Null(turn.ExecutionPlan);
    }

    [Fact]
    public void CreateInspectorRunRecord_AppendsNormalizedStepEvents_WhenExecutionPlanExists()
    {
        var record = RuntimePersistenceRecords.CreateInspectorRunRecord(
            runId: "run-1",
            sessionId: "session-1",
            agentName: "agent",
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            systemPrompt: "prompt",
            rawPlanResponse: "raw",
            events:
            [
                new InspectorEvent(DateTimeOffset.UtcNow, "RunStarted", null, null, "hello")
            ],
            executionResults:
            [
                new ComponentActionExecutionResult("AgentTabs", "switch", ActionOutcome.Applied, "Applied."),
                new ComponentActionExecutionResult("AgentDialog", "open", ActionOutcome.Failed, "Failed.")
            ],
            executionPlan: new AgentExecutionPlan(
                "agent",
                new AgentExecutionContext("session-1", "run-1", Route: "/demo", Freshness: AgentContextFreshness.Current),
                [
                    new AgentExecutionStep(
                        "step-1",
                        0,
                        AgentExecutionStepKind.SemanticCapability,
                        "supplier-compliance",
                        "identify",
                        AgentExecutionStepStatus.Completed,
                        false,
                        new AgentPolicyDecision(true, AgentRiskClass.ReadOnly, AgentApprovalMode.None),
                        Message: "Identified suppliers.")
                ]),
            succeeded: false,
            errorMessage: "Failed.");

        Assert.Equal("run-1", record.RunId);
        Assert.Equal("session-1", record.SessionId);
        Assert.Equal("prompt", record.SystemPrompt);
        Assert.Equal("raw", record.RawPlanResponse);
        Assert.NotNull(record.ExecutionPlan);
        Assert.Single(record.ExecutionPlan!.Steps);
        Assert.Equal(2, record.Events.Count);
        Assert.Contains(record.Events, static e => e.Kind == "StepCompleted" && e.ComponentId == "supplier-compliance");
        Assert.DoesNotContain(record.Events, static e => e.Kind == "ToolCallResult" || e.Kind == "ToolCallFailed");
    }

    [Fact]
    public void CreateInspectorRunRecord_PreservesLegacyExecutionEvents_WhenExecutionPlanIsMissing()
    {
        var record = RuntimePersistenceRecords.CreateInspectorRunRecord(
            runId: "run-1",
            sessionId: "session-1",
            agentName: "agent",
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            systemPrompt: "prompt",
            rawPlanResponse: "raw",
            events:
            [
                new InspectorEvent(DateTimeOffset.UtcNow, "RunStarted", null, null, "hello")
            ],
            executionResults:
            [
                new ComponentActionExecutionResult("AgentTabs", "switch", ActionOutcome.Applied, "Applied."),
                new ComponentActionExecutionResult("AgentDialog", "open", ActionOutcome.Failed, "Failed.")
            ],
            executionPlan: null,
            succeeded: false,
            errorMessage: "Failed.");

        Assert.Equal(3, record.Events.Count);
        Assert.Contains(record.Events, static e => e.Kind == "ToolCallResult" && e.ComponentId == "AgentTabs");
        Assert.Contains(record.Events, static e => e.Kind == "ToolCallFailed" && e.ComponentId == "AgentDialog");
    }
}
