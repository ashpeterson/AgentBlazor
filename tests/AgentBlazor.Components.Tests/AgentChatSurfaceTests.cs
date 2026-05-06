using AgentBlazor.Agents;
using AgentBlazor.Components.Chat;
using AgentBlazor.Components.Render;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Models;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AgentBlazor.Components.Tests;

public sealed class AgentChatSurfaceTests : TestContext
{
    [Fact]
    public void StopButton_CancelsActiveStreamingRun_AndRendersCanceledOutcome()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, TestActionRenderRegistry>();

        var runtimeAdapter = new CancellableStreamingRuntimeAdapter();
        Services.AddSingleton<IAgentRuntimeAdapter>(runtimeAdapter);

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent"));

        cut.Find("textarea[aria-label='Message input']").Input("Run a long operation");
        cut.Find("button[aria-label='Send message']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("button[aria-label='Stop active run']"));
        });

        cut.Find("button[aria-label='Stop active run']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("test-run-1", runtimeAdapter.LastStoppedRunId);
            Assert.Contains("Run canceled.", cut.Markup);
            Assert.DoesNotContain("Something went wrong", cut.Markup);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Surface_ReconnectsActiveRun_FromSharedRunStore_OnFreshRender()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, TestActionRenderRegistry>();

        var runtimeAdapter = new ReconnectStreamingRuntimeAdapter();
        Services.AddSingleton<IAgentRuntimeAdapter>(runtimeAdapter);

        var conversationSessionId = AgentConversationScope.BuildSessionKey(
            "session-1",
            "Test Agent",
            isolateByAgent: false);
        Services.GetRequiredService<IAgentChatActiveRunStore>().Track(new AgentChatActiveRun(
            conversationSessionId,
            "reconnect-run-1",
            "Test Agent",
            "Resume the pending run",
            DateTimeOffset.UtcNow));

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent")
            .Add(static surface => surface.SessionId, "session-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("reconnect-run-1", runtimeAdapter.ConnectedRunId);
            Assert.Contains("Resume the pending run", cut.Markup);
            Assert.Contains("Reconnected response.", cut.Markup);
        });
    }

    [Fact]
    public async Task ApprovalOutcome_ReplacesPersistedPlaceholderResponse_ForFreshRender()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, TestActionRenderRegistry>();
        Services.AddSingleton<IAgentRuntimeAdapter>(sp =>
            new PersistingApprovalRuntimeAdapter(sp.GetRequiredService<IConversationStore>()));

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent")
            .Add(static surface => surface.SessionId, "approval-session"));

        cut.Find("textarea[aria-label='Message input']").Input("run the runtime approval probe");
        cut.Find("button[aria-label='Send message']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".ab-chat-surface__item--approval"));
        });

        cut.Find(".ab-chat-surface__submit--approve").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Runtime approval probe completed.", cut.Markup);
        });

        var sessionId = AgentConversationScope.BuildSessionKey("approval-session", "Test Agent", isolateByAgent: false);
        var history = await Services.GetRequiredService<IConversationStore>().GetHistoryAsync(sessionId);
        Assert.NotNull(history);
        Assert.Equal(2, history.Turns.Count);
        Assert.Equal("Runtime approval probe completed.", history.Turns[^1].AgentResponse);

        var fresh = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent")
            .Add(static surface => surface.SessionId, "approval-session"));

        fresh.WaitForAssertion(() =>
        {
            Assert.Contains("Runtime approval probe completed.", fresh.Markup);
            Assert.DoesNotContain(">Approved.<", fresh.Markup);
        });
    }

    [Fact]
    public async Task CanceledOutcome_PersistsToConversationHistory_ForFreshRender()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, TestActionRenderRegistry>();

        var runtimeAdapter = new CancellableStreamingRuntimeAdapter();
        Services.AddSingleton<IAgentRuntimeAdapter>(runtimeAdapter);

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent")
            .Add(static surface => surface.SessionId, "cancel-session"));

        cut.Find("textarea[aria-label='Message input']").Input("Run a long operation");
        cut.Find("button[aria-label='Send message']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("button[aria-label='Stop active run']"));
        });

        cut.Find("button[aria-label='Stop active run']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Run canceled.", cut.Markup);
        }, TimeSpan.FromSeconds(10));

        var sessionId = AgentConversationScope.BuildSessionKey("cancel-session", "Test Agent", isolateByAgent: false);
        var history = await Services.GetRequiredService<IConversationStore>().GetHistoryAsync(sessionId);
        Assert.NotNull(history);
        Assert.Single(history.Turns);
        Assert.Equal("Run a long operation", history.Turns[0].UserMessage);
        Assert.Equal("Run canceled.", history.Turns[0].AgentResponse);

        var fresh = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent")
            .Add(static surface => surface.SessionId, "cancel-session"));

        fresh.WaitForAssertion(() =>
        {
            Assert.Contains("Run canceled.", fresh.Markup);
        });
    }

    private sealed class CancellableStreamingRuntimeAdapter : IAgentRuntimeAdapter
    {
        private readonly CancellationTokenSource _runCancellation = new();
        private int _runCount;

        public bool SupportsStreaming => true;

        public bool SupportsReconnect => false;

        public bool SupportsCancellation => true;

        public string? LastStoppedRunId { get; private set; }

        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = request;
            var runId = $"test-run-{Interlocked.Increment(ref _runCount)}";

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunStarted,
                RunId = runId,
                Sequence = 1,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "Test Agent"
            };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _runCancellation.Token);
            await Task.Delay(TimeSpan.FromSeconds(30), linkedCts.Token);
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastStoppedRunId = runId;
            _runCancellation.Cancel();
            return Task.FromResult(true);
        }
    }

    private sealed class ReconnectStreamingRuntimeAdapter : IAgentRuntimeAdapter
    {
        public bool SupportsStreaming => true;

        public bool SupportsReconnect => true;

        public bool SupportsCancellation => false;

        public string? ConnectedRunId { get; private set; }

        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ConnectedRunId = runId;

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunStarted,
                RunId = runId,
                Sequence = 1,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "Test Agent",
                IsReplay = true
            };

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageContent,
                RunId = runId,
                Sequence = 2,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "Test Agent",
                TextDelta = "Reconnected response.",
                IsReplay = true
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunFinished,
                RunId = runId,
                Sequence = 3,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "Test Agent",
                Response = new AgentTurnResponse(
                    "Test Agent",
                    "Reconnected response.",
                    [],
                    [])
            };
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            return Task.FromResult(false);
        }
    }

    private sealed class PersistingApprovalRuntimeAdapter(IConversationStore conversationStore) : IAgentRuntimeAdapter
    {
        public bool SupportsStreaming => false;

        public bool SupportsReconnect => false;

        public bool SupportsCancellation => false;

        public async Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            if (IsApprovalMessage(request.UserMessage))
            {
                Assert.NotNull(request.Context);
                Assert.True(
                    request.Context.TryGetValue("agentblazor.approvalArgs.runtime_probe.run_approval_probe", out var rawArguments),
                    "Approval continuation should include the original pending approval parameters.");
                using var document = JsonDocument.Parse(rawArguments);
                Assert.Equal("TCK-1042", document.RootElement.GetProperty("ticketId").GetString());
            }

            var response = IsApprovalMessage(request.UserMessage)
                ? CreateApprovedResponse(request.GetEffectiveSessionId())
                : CreateApprovalRequiredResponse(request.GetEffectiveSessionId());

            var sessionId = AgentConversationScope.BuildSessionKey(
                request.GetEffectiveSessionId(),
                response.AgentName,
                isolateByAgent: false);
            await conversationStore.AppendTurnAsync(
                sessionId,
                new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = request.UserMessage,
                    AgentResponse = response.ResponseText,
                    PlannedActions = response.LegacyPlannedActions,
                    ExecutionResults = response.LegacyExecutionResults,
                    ExecutionPlan = response.ExecutionPlan,
                    GeneratedUi = response.GeneratedUi
                },
                cancellationToken);

            return response;
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            return Task.FromResult(false);
        }

        private static bool IsApprovalMessage(string message)
            => message.StartsWith("Approved. Continue by invoking the approved action(s):", StringComparison.Ordinal) &&
               message.Contains("runtime_probe.run_approval_probe", StringComparison.Ordinal);

        private static AgentTurnResponse CreateApprovalRequiredResponse(string sessionId)
        {
            var executionPlan = new AgentExecutionPlan(
                "Test Agent",
                new AgentExecutionContext(sessionId, "approval-run-1"),
                [
                    new AgentExecutionStep(
                        "step-1",
                        0,
                        AgentExecutionStepKind.SemanticCapability,
                        "runtime_probe",
                        "run_approval_probe",
                        AgentExecutionStepStatus.ApprovalRequired,
                        false,
                        new AgentPolicyDecision(true, AgentRiskClass.SensitiveMutation, AgentApprovalMode.StepApproval),
                        Message: "Approval required for semantic capability runtime_probe.run_approval_probe.")
                ]);

            return new AgentTurnResponse("Test Agent", "Approval required for semantic capability runtime_probe.run_approval_probe.", [], [])
            {
                RequiresApproval = true,
                PendingApprovals =
                [
                    new PendingApproval(
                        "runtime_probe",
                        "run_approval_probe",
                        "Run the runtime approval probe",
                        new Dictionary<string, object?>
                        {
                            ["ticketId"] = "TCK-1042"
                        },
                        new AgentPolicyDecision(true, AgentRiskClass.SensitiveMutation, AgentApprovalMode.StepApproval))
                ],
                ExecutionPlan = executionPlan
            };
        }

        private static AgentTurnResponse CreateApprovedResponse(string sessionId)
        {
            var executionPlan = new AgentExecutionPlan(
                "Test Agent",
                new AgentExecutionContext(sessionId, "approval-run-2"),
                [
                    new AgentExecutionStep(
                        "step-1",
                        0,
                        AgentExecutionStepKind.SemanticCapability,
                        "runtime_probe",
                        "run_approval_probe",
                        AgentExecutionStepStatus.Completed,
                        false,
                        new AgentPolicyDecision(true, AgentRiskClass.SensitiveMutation, AgentApprovalMode.StepApproval),
                        Message: "Runtime approval probe completed.")
                    {
                        Outputs = new Dictionary<string, object?>
                        {
                            ["probe"] = "APPROVED"
                        }
                    }
                ]);

            return new AgentTurnResponse("Test Agent", "Approved.", [], [])
            {
                ExecutionPlan = executionPlan
            };
        }
    }

    private sealed class TestActionRenderRegistry : IAgentActionRenderRegistry
    {
        public void Register(string agentId, string actionId, ActionRenderFragments fragments)
        {
            _ = agentId;
            _ = actionId;
            _ = fragments;
        }

        public void Unregister(string agentId, string actionId)
        {
            _ = agentId;
            _ = actionId;
        }

        public ActionRenderFragments? TryGet(string agentId, string actionId)
        {
            _ = agentId;
            _ = actionId;
            return null;
        }
    }
}
