using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Services;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class IncidentEscalationWorkflowIntegrationTests
{
    [Fact]
    public async Task RuntimeAdapter_IncidentEscalationWorkflow_UsesContextBoundSessionParameter_ForSummary()
    {
        var services = CreateServices();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        chatClient.SetNextTool("summarize_incident_triage");

        AgentTurnResponse response;
        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            response = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Summarize the current incident triage workflow",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                    [AgentRuntimeContextKeys.ContextVersion] = "ctx-incident-1"
                }));
        }

        Assert.False(response.RequiresApproval);
        var plan = response.ExecutionPlan;
        Assert.NotNull(plan);
        Assert.Equal("/demo/workflows/incident-escalation", plan!.Context.Route);
        Assert.Equal("ctx-incident-1", plan.Context.ContextVersion);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
        Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
        Assert.Contains("triage", step.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(step.Outputs);
        Assert.Equal("overview", step.Outputs!["selectedNodeId"]);
        Assert.Equal(2, step.Outputs["missingEvidenceCount"]);
        Assert.Contains(step.Warnings ?? [], static warning =>
            warning.Contains("policy escalation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeAdapter_IncidentEscalationWorkflow_BlockedBrief_IsReportedAfterApproval()
    {
        var services = CreateServices();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();

        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            chatClient.SetNextTool("prepare_escalation_brief");
            var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare an escalation brief for the current incident",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-blocked",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            Assert.True(approvalResponse.RequiresApproval);
            var pendingApproval = Assert.Single(approvalResponse.PendingApprovals);
            Assert.Equal("incident_escalation", pendingApproval.ComponentId);
            Assert.Equal("prepare_escalation_brief", pendingApproval.ActionId);

            chatClient.SetNextTool("prepare_escalation_brief");
            var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare an escalation brief for the current incident",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-blocked",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                    ["agentblazor.approvals"] = "incident_escalation.prepare_escalation_brief"
                }));

            Assert.False(blockedResponse.RequiresApproval);
            var step = Assert.Single(blockedResponse.ExecutionPlan!.Steps);
            Assert.Equal(AgentExecutionStepStatus.Blocked, step.Status);
            Assert.Contains("blocked", step.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(step.Outputs?["blockers"] as string[] ?? [], static blocker =>
                blocker.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
                blocker.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task RuntimeAdapter_IncidentEscalationWorkflow_ApprovalGatedBrief_ExecutesAfterReadinessActions()
    {
        var services = CreateServices();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IncidentEscalationWorkflowService>();

        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            chatClient.SetNextTool("assign_triage_owner");
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Assign the default triage owner for the current incident",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-approved",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            chatClient.SetNextTool("complete_evidence_review");
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Mark the current evidence review as complete",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-approved",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            chatClient.SetNextTool("prepare_escalation_brief");
            var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare an escalation brief for the current incident",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-approved",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            Assert.True(approvalResponse.RequiresApproval);

            chatClient.SetNextTool("prepare_escalation_brief");
            var approvedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare an escalation brief for the current incident",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-approved",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                    ["agentblazor.approvals"] = "incident_escalation.prepare_escalation_brief"
                }));

            Assert.False(approvedResponse.RequiresApproval);
            Assert.NotNull(workflow.CurrentBrief);
            Assert.True(workflow.IsEscalationDialogOpen);
            Assert.Equal("escalation", workflow.Snapshot.SelectedNodeId);
            Assert.Equal(3, workflow.Snapshot.ActiveTabIndex);
            Assert.Equal(2, workflow.Snapshot.CurrentStepIndex);
            var step = Assert.Single(approvedResponse.ExecutionPlan!.Steps);
            Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_IncidentEscalationWorkflow_SubmitHandoff_BlocksUntilRecoveryPlaybookRuns()
    {
        var services = CreateServices();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IncidentEscalationWorkflowService>();

        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            await PrepareReadyBriefAsync(chatClient, adapter, "incident-escalation-recovery");

            chatClient.SetNextTool("submit_escalation_handoff");
            var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Submit the current escalation brief to the review board",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            Assert.True(approvalResponse.RequiresApproval);

            chatClient.SetNextTool("submit_escalation_handoff");
            var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Submit the current escalation brief to the review board",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                    ["agentblazor.approvals"] = "incident_escalation.submit_escalation_handoff"
                }));

            var step = Assert.Single(blockedResponse.ExecutionPlan!.Steps);
            Assert.Equal(AgentExecutionStepStatus.Blocked, step.Status);
            Assert.Equal("Recovery required", workflow.Snapshot.Incident.EscalationStatus);
            Assert.False(workflow.Snapshot.Incident.CommunicationsLeadConfirmed);
            Assert.Contains(step.Outputs?["blockers"] as string[] ?? [], static blocker =>
                blocker.Contains("communications lead", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task RuntimeAdapter_IncidentEscalationWorkflow_RecoveryPlaybook_AllowsSuccessfulHandoffSubmission()
    {
        var services = CreateServices();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IncidentEscalationWorkflowService>();

        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            await PrepareReadyBriefAsync(chatClient, adapter, "incident-escalation-submit");

            chatClient.SetNextTool("submit_escalation_handoff");
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Submit the current escalation brief to the review board",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-submit",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            chatClient.SetNextTool("submit_escalation_handoff");
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Submit the current escalation brief to the review board",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-submit",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                    ["agentblazor.approvals"] = "incident_escalation.submit_escalation_handoff"
                }));

            chatClient.SetNextTool("apply_recovery_playbook");
            var recoveryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Apply the escalation recovery playbook for the current incident",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-submit",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(recoveryResponse.ExecutionPlan!.Steps).Status);
            Assert.True(workflow.Snapshot.Incident.CommunicationsLeadConfirmed);
            Assert.Equal("Ready to submit", workflow.Snapshot.Incident.EscalationStatus);

            chatClient.SetNextTool("submit_escalation_handoff");
            var finalApproval = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Submit the current escalation brief to the review board",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-submit",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
                }));

            Assert.True(finalApproval.RequiresApproval);

            chatClient.SetNextTool("submit_escalation_handoff");
            var finalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Submit the current escalation brief to the review board",
                AgentName: "Incident Escalation Agent",
                SessionId: "incident-escalation-submit",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                    ["agentblazor.approvals"] = "incident_escalation.submit_escalation_handoff"
                }));

            var finalStep = Assert.Single(finalResponse.ExecutionPlan!.Steps);
            Assert.Equal(AgentExecutionStepStatus.Completed, finalStep.Status);
            Assert.Equal("Submitted", workflow.Snapshot.Incident.EscalationStatus);
            Assert.False(workflow.IsEscalationDialogOpen);
        }
    }

    private static async Task PrepareReadyBriefAsync(
        WorkflowToolChatClient chatClient,
        IAgentRuntimeAdapter adapter,
        string sessionId)
    {
        chatClient.SetNextTool("assign_triage_owner");
        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Assign the default triage owner for the current incident",
            AgentName: "Incident Escalation Agent",
            SessionId: sessionId,
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
            }));

        chatClient.SetNextTool("complete_evidence_review");
        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Mark the current evidence review as complete",
            AgentName: "Incident Escalation Agent",
            SessionId: sessionId,
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
            }));

        chatClient.SetNextTool("prepare_escalation_brief");
        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Prepare an escalation brief for the current incident",
            AgentName: "Incident Escalation Agent",
            SessionId: sessionId,
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation"
            }));

        chatClient.SetNextTool("prepare_escalation_brief");
        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Prepare an escalation brief for the current incident",
            AgentName: "Incident Escalation Agent",
            SessionId: sessionId,
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/incident-escalation",
                ["agentblazor.approvals"] = "incident_escalation.prepare_escalation_brief"
            }));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowToolChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<WorkflowToolChatClient>());
        services.AddScoped<IncidentEscalationWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<IncidentEscalationCapabilities>()
            .AddAgent("Incident Escalation Agent", agent =>
            {
                agent.WithAllowedActions(
                    "incident_escalation.summarize_incident_triage",
                    "incident_escalation.focus_evidence_review",
                    "incident_escalation.assign_triage_owner",
                    "incident_escalation.complete_evidence_review",
                    "incident_escalation.prepare_escalation_brief",
                    "incident_escalation.submit_escalation_handoff",
                    "incident_escalation.apply_recovery_playbook",
                    "incident_escalation.reset_incident_workflow");
            });

        return services;
    }

    private sealed class WorkflowToolChatClient : IChatClient
    {
        private string? _toolNameContains;
        private IReadOnlyDictionary<string, object?> _arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        public void SetNextTool(string toolNameContains, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _toolNameContains = toolNameContains;
            _arguments = arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;

            var toolNameContains = _toolNameContains
                ?? throw new InvalidOperationException("No tool was configured for the next response.");
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(function =>
                    function.Name.Contains(toolNameContains, StringComparison.OrdinalIgnoreCase)) ??
                []);
            await tool.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?>(_arguments, StringComparer.OrdinalIgnoreCase)),
                cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"invoked {toolNameContains}"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
