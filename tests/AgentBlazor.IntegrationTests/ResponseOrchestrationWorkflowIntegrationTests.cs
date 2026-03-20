using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.IntegrationTests;

public class ResponseOrchestrationWorkflowIntegrationTests
{
    [Fact]
    public async Task RuntimeAdapter_ResponseOrchestration_UsesContextBoundSessionParameter_ForReadinessAssessment()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var services = CreateServices(dbPath);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await EnsureDatabaseCreatedAsync(provider);

            var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
            var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
            var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

            await using var scope = provider.CreateAsyncScope();
            chatClient.SetNextTool("assess_response_readiness");

            AgentTurnResponse response;
            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                response = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Assess cross-system response readiness",
                    AgentName: "Response Orchestration Agent",
                    SessionId: "response-orchestration",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration",
                        [AgentRuntimeContextKeys.ContextVersion] = "ctx-response-1"
                    }));
            }

            Assert.False(response.RequiresApproval);
            var plan = Assert.IsType<AgentExecutionPlan>(response.ExecutionPlan);
            Assert.Equal("/demo/workflows/response-orchestration", plan.Context.Route);
            Assert.Equal("ctx-response-1", plan.Context.ContextVersion);
            var step = Assert.Single(plan.Steps);
            Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
            Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
            Assert.NotNull(step.Outputs);
            Assert.True(Convert.ToInt32(step.Outputs!["highlightedSupplierCount"]) > 0);
            Assert.Equal(0, Convert.ToInt32(step.Outputs["verifiedFileCount"]));
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_ResponseOrchestration_RecoveryPlaybook_AllowsApprovalGatedPacketPreparation()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var services = CreateServices(dbPath);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await EnsureDatabaseCreatedAsync(provider);

            var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
            var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
            var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

            await using var scope = provider.CreateAsyncScope();
            var workflow = scope.ServiceProvider.GetRequiredService<ResponseOrchestrationWorkflowService>();

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_response_packet");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the cross-system response packet",
                    AgentName: "Response Orchestration Agent",
                    SessionId: "response-orchestration-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_response_packet");
                var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the cross-system response packet",
                    AgentName: "Response Orchestration Agent",
                    SessionId: "response-orchestration-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration",
                        ["agentblazor.approvals"] = "response_orchestration.prepare_response_packet"
                    }));

                Assert.False(blockedResponse.RequiresApproval);
                Assert.Equal(AgentExecutionStepStatus.Blocked, Assert.Single(blockedResponse.ExecutionPlan!.Steps).Status);
                Assert.NotEmpty(workflow.LatestBlockers);
                Assert.Contains("/demo/workflows/supplier-compliance", workflow.GetSupplierWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("focus=recovery", workflow.GetSupplierWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("/demo/workflows/file-audit-bundle", workflow.GetFileWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("source=response-orchestration", workflow.GetFileWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("focus=", workflow.GetFileWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("/demo/workflows/incident-escalation", workflow.GetIncidentWorkflowRoute(), StringComparison.OrdinalIgnoreCase);

                chatClient.SetNextTool("apply_response_recovery_playbook");
                var recoveryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Apply the cross-system recovery playbook",
                    AgentName: "Response Orchestration Agent",
                    SessionId: "response-orchestration-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(recoveryResponse.ExecutionPlan!.Steps).Status);
                Assert.Contains("focus=remediation", workflow.GetSupplierWorkflowRoute(), StringComparison.OrdinalIgnoreCase);

                chatClient.SetNextTool("prepare_response_packet");
                var retryApproval = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the cross-system response packet",
                    AgentName: "Response Orchestration Agent",
                    SessionId: "response-orchestration-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration"
                    }));

                Assert.True(retryApproval.RequiresApproval);

                chatClient.SetNextTool("prepare_response_packet");
                var retryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the cross-system response packet",
                    AgentName: "Response Orchestration Agent",
                    SessionId: "response-orchestration-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration",
                        ["agentblazor.approvals"] = "response_orchestration.prepare_response_packet"
                    }));

                Assert.False(retryResponse.RequiresApproval);
                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(retryResponse.ExecutionPlan!.Steps).Status);
                Assert.NotNull(workflow.CurrentPacket);
                Assert.True(workflow.IsPacketDialogOpen);
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_ResponseOrchestration_AdvanceNextStage_ProgressesSharedState()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var services = CreateServices(dbPath);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await EnsureDatabaseCreatedAsync(provider);

            var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
            var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
            var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

            await using var scope = provider.CreateAsyncScope();
            var workflow = scope.ServiceProvider.GetRequiredService<ResponseOrchestrationWorkflowService>();
            const string sessionId = "response-orchestration-advance";

            await workflow.LoadAsync(sessionId);
            await workflow.AssessResponseReadinessAsync(sessionId);

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("advance_response_stage");
                var supplierStep = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Advance the next guided subsystem stage",
                    AgentName: "Response Orchestration Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(supplierStep.ExecutionPlan!.Steps).Status);
                Assert.Contains("/demo/workflows/file-audit-bundle", workflow.GetNextGuidedWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains(workflow.JourneyEvents, entry => entry.StageKey == "supplier");

                chatClient.SetNextTool("advance_response_stage");
                var fileStep = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Advance the next guided subsystem stage",
                    AgentName: "Response Orchestration Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(fileStep.ExecutionPlan!.Steps).Status);
                Assert.Contains("/demo/workflows/incident-escalation", workflow.GetNextGuidedWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains(workflow.JourneyEvents, entry => entry.StageKey == "file");
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task ResponseOrchestration_GuidedCrossSurfaceProgression_CanCompleteFinalPacket()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var services = CreateServices(dbPath);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await EnsureDatabaseCreatedAsync(provider);

            var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
            var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
            var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

            await using var scope = provider.CreateAsyncScope();
            var workflow = scope.ServiceProvider.GetRequiredService<ResponseOrchestrationWorkflowService>();
            var supplier = scope.ServiceProvider.GetRequiredService<SupplierComplianceWorkflowService>();
            var file = scope.ServiceProvider.GetRequiredService<DemoFileWorkflowService>();
            var incident = scope.ServiceProvider.GetRequiredService<IncidentEscalationWorkflowService>();
            const string sessionId = "response-orchestration-guided";

            await workflow.LoadAsync(sessionId);
            await workflow.AssessResponseReadinessAsync(sessionId);

            Assert.Contains("/demo/workflows/supplier-compliance", workflow.GetNextGuidedWorkflowRoute(), StringComparison.OrdinalIgnoreCase);

            supplier.FocusAtRiskSuppliers(30);
            supplier.ExplainFocusedSuppliers();
            supplier.ApplyRecoveryPlaybook();
            supplier.PrepareRemediationDraft();

            await workflow.LoadAsync(sessionId);
            Assert.Contains("/demo/workflows/file-audit-bundle", workflow.GetNextGuidedWorkflowRoute("supplier"), StringComparison.OrdinalIgnoreCase);

            var fileSnapshot = await file.GetOrCreateAsync(sessionId, "Remote");
            fileSnapshot = await file.SyncFilesAsync(sessionId, fileSnapshot.Files, "Remote");
            fileSnapshot = await file.RunRemoteHandoffAsync(sessionId);
            _ = fileSnapshot;
            await file.ValidateRemoteTokensAsync(sessionId);

            await workflow.LoadAsync(sessionId);
            Assert.Contains("/demo/workflows/incident-escalation", workflow.GetNextGuidedWorkflowRoute("file"), StringComparison.OrdinalIgnoreCase);

            await incident.LoadAsync(sessionId);
            await incident.AssignTriageOwnerAsync(sessionId);
            await incident.CompleteEvidenceReviewAsync(sessionId);
            await incident.PrepareEscalationBriefAsync(sessionId);

            await workflow.LoadAsync(sessionId);
            Assert.Null(workflow.GetNextGuidedWorkflowRoute("incident"));

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_response_packet");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the cross-system response packet",
                    AgentName: "Response Orchestration Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_response_packet");
                var response = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the cross-system response packet",
                    AgentName: "Response Orchestration Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/response-orchestration",
                        ["agentblazor.approvals"] = "response_orchestration.prepare_response_packet"
                    }));

                Assert.False(response.RequiresApproval);
                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(response.ExecutionPlan!.Steps).Status);
                Assert.NotNull(workflow.CurrentPacket);
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task ResponseOrchestration_ProcessGuidedReturn_RefreshesNarrativeAndNextRoute()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var services = CreateServices(dbPath);

            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            await EnsureDatabaseCreatedAsync(provider);

            await using var scope = provider.CreateAsyncScope();
            var workflow = scope.ServiceProvider.GetRequiredService<ResponseOrchestrationWorkflowService>();
            var supplier = scope.ServiceProvider.GetRequiredService<SupplierComplianceWorkflowService>();
            var file = scope.ServiceProvider.GetRequiredService<DemoFileWorkflowService>();
            var incident = scope.ServiceProvider.GetRequiredService<IncidentEscalationWorkflowService>();
            const string sessionId = "response-orchestration-return";

            await workflow.LoadAsync(sessionId);
            await workflow.AssessResponseReadinessAsync(sessionId);

            supplier.FocusAtRiskSuppliers(30);
            supplier.ExplainFocusedSuppliers();
            supplier.ApplyRecoveryPlaybook();
            supplier.PrepareRemediationDraft();

            var supplierReturn = await workflow.ProcessGuidedReturnAsync(sessionId, "supplier", "remediation");
            Assert.Contains("file audit", supplierReturn.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/demo/workflows/file-audit-bundle", workflow.GetNextGuidedWorkflowRoute("supplier"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(workflow.JourneyEvents, entry => entry.StageKey == "supplier" && entry.Title.Contains("Returned", StringComparison.OrdinalIgnoreCase));

            var fileSnapshot = await file.GetOrCreateAsync(sessionId, "Remote");
            fileSnapshot = await file.SyncFilesAsync(sessionId, fileSnapshot.Files, "Remote");
            fileSnapshot = await file.RunRemoteHandoffAsync(sessionId);
            _ = fileSnapshot;
            await file.ValidateRemoteTokensAsync(sessionId);

            await incident.LoadAsync(sessionId);
            await incident.AssignTriageOwnerAsync(sessionId);
            await incident.CompleteEvidenceReviewAsync(sessionId);
            await incident.PrepareEscalationBriefAsync(sessionId);

            var incidentReturn = await workflow.ProcessGuidedReturnAsync(sessionId, "incident", "escalation");
            Assert.Contains("prepare the response packet", incidentReturn.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Null(workflow.GetNextGuidedWorkflowRoute("incident"));
            Assert.Contains(workflow.JourneyEvents, entry => entry.StageKey == "incident" && entry.Title.Contains("Returned", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    private static ServiceCollection CreateServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging(static builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));
        services.AddHttpClient("demo-remote-storage");
        services.Configure<DemoRemoteStorageOptions>(_ => { });
        services.AddSingleton<IDemoRemoteStorageAdapter, DemoRemoteStorageAdapter>();
        services.AddSingleton<WorkflowToolChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<WorkflowToolChatClient>());
        services.AddDbContextFactory<DemoWorkflowDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<DojoWorkspaceService>();
        services.AddScoped<DemoFileWorkflowService>();
        services.AddScoped<IncidentEscalationWorkflowService>();
        services.AddScoped<SupplierComplianceWorkflowService>();
        services.AddScoped<ResponseOrchestrationWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ResponseOrchestrationCapabilities>()
            .AddAgent("Response Orchestration Agent", agent =>
            {
                agent.WithAllowedActions(
                    "response_orchestration.assess_response_readiness",
                    "response_orchestration.advance_response_stage",
                    "response_orchestration.prepare_response_packet",
                    "response_orchestration.apply_response_recovery_playbook",
                    "response_orchestration.reset_response_workflow");
            });

        return services;
    }

    private static async Task EnsureDatabaseCreatedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DemoWorkflowDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
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
