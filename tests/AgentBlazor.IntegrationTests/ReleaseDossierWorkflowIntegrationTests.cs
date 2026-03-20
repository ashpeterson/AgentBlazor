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

public class ReleaseDossierWorkflowIntegrationTests
{
    [Fact]
    public async Task RuntimeAdapter_ReleaseDossier_UsesContextBoundSessionParameter_ForReadinessAssessment()
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
            chatClient.SetNextTool("assess_release_dossier_readiness");

            AgentTurnResponse response;
            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                response = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Assess release dossier readiness",
                    AgentName: "Release Dossier Agent",
                    SessionId: "release-dossier",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier",
                        [AgentRuntimeContextKeys.ContextVersion] = "ctx-release-1"
                    }));
            }

            Assert.False(response.RequiresApproval);
            var plan = Assert.IsType<AgentExecutionPlan>(response.ExecutionPlan);
            Assert.Equal("/demo/workflows/release-dossier", plan.Context.Route);
            Assert.Equal("ctx-release-1", plan.Context.ContextVersion);
            var step = Assert.Single(plan.Steps);
            Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
            Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
            Assert.NotNull(step.Outputs);
            Assert.Equal("Classic Scrambled Eggs", Convert.ToString(step.Outputs!["recipeTitle"]));
            Assert.True(Convert.ToInt32(step.Outputs["fileCount"]) > 0);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_ReleaseDossier_RecoveryAndStageAdvancement_AllowApprovalGatedDossierPreparation()
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
            var workflow = scope.ServiceProvider.GetRequiredService<ReleaseDossierWorkflowService>();
            var recipeWorkflow = scope.ServiceProvider.GetRequiredService<DojoRecipeReleaseWorkflowService>();
            const string sessionId = "release-dossier-blocked";

            await recipeWorkflow.LoadAsync(sessionId);
            var snapshot = Assert.IsType<DojoWorkspaceSnapshot>(recipeWorkflow.Snapshot);
            snapshot.Recipe.Vegan = true;
            snapshot.Recipe.Vegetarian = true;
            await recipeWorkflow.SaveRecipeAsync(snapshot.Recipe);

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_release_dossier");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the release dossier",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_release_dossier");
                var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the release dossier",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier",
                        ["agentblazor.approvals"] = "release_dossier.prepare_release_dossier"
                    }));

                Assert.False(blockedResponse.RequiresApproval);
                Assert.Equal(AgentExecutionStepStatus.Blocked, Assert.Single(blockedResponse.ExecutionPlan!.Steps).Status);
                Assert.NotEmpty(workflow.LatestBlockers);
                Assert.Contains("/demo/workflows/recipe-release", workflow.GetRecipeWorkflowRoute(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("/demo/workflows/file-audit-bundle", workflow.GetFileWorkflowRoute(), StringComparison.OrdinalIgnoreCase);

                chatClient.SetNextTool("apply_release_dossier_recovery_playbook");
                var recoveryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Apply the release dossier recovery playbook",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(recoveryResponse.ExecutionPlan!.Steps).Status);

                chatClient.SetNextTool("advance_release_dossier_stage");
                var recipeStep = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Advance the next guided subsystem stage",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(recipeStep.ExecutionPlan!.Steps).Status);
                Assert.Contains(workflow.JourneyEvents, entry => entry.StageKey == "recipe");

                chatClient.SetNextTool("advance_release_dossier_stage");
                var fileStep = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Advance the next guided subsystem stage",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(fileStep.ExecutionPlan!.Steps).Status);
                Assert.Null(workflow.GetNextGuidedWorkflowRoute());

                chatClient.SetNextTool("prepare_release_dossier");
                var retryApproval = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the release dossier",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier"
                    }));

                Assert.True(retryApproval.RequiresApproval);

                chatClient.SetNextTool("prepare_release_dossier");
                var retryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the release dossier",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier",
                        ["agentblazor.approvals"] = "release_dossier.prepare_release_dossier"
                    }));

                Assert.False(retryResponse.RequiresApproval);
                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(retryResponse.ExecutionPlan!.Steps).Status);
                Assert.NotNull(workflow.CurrentDossier);
                Assert.True(workflow.IsDossierDialogOpen);
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task ReleaseDossier_GuidedCrossSurfaceProgression_CanCompleteFinalDossier()
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
            var workflow = scope.ServiceProvider.GetRequiredService<ReleaseDossierWorkflowService>();
            var recipeWorkflow = scope.ServiceProvider.GetRequiredService<DojoRecipeReleaseWorkflowService>();
            var fileWorkflow = scope.ServiceProvider.GetRequiredService<DemoFileWorkflowService>();
            const string sessionId = "release-dossier-guided";

            await workflow.LoadAsync(sessionId);
            await workflow.AssessReleaseDossierReadinessAsync(sessionId);

            Assert.Contains("/demo/workflows/recipe-release", workflow.GetNextGuidedWorkflowRoute(), StringComparison.OrdinalIgnoreCase);

            await recipeWorkflow.LoadAsync(sessionId);
            _ = await recipeWorkflow.AssessReleaseReadinessAsync(sessionId);
            _ = await recipeWorkflow.PrepareReleaseDraftAsync(sessionId);

            var recipeReturn = await workflow.ProcessGuidedReturnAsync(sessionId, "recipe", "draft-ready");
            Assert.Contains("audit evidence", recipeReturn.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(workflow.JourneyEvents, entry => entry.StageKey == "recipe" && entry.Title.Contains("Returned", StringComparison.OrdinalIgnoreCase));

            var fileSnapshot = await fileWorkflow.GetOrCreateAsync(sessionId, "Remote");
            fileSnapshot = await fileWorkflow.SyncFilesAsync(sessionId, fileSnapshot.Files, "Remote");
            fileSnapshot = await fileWorkflow.RunRemoteHandoffAsync(sessionId);
            _ = fileSnapshot;
            await fileWorkflow.ValidateRemoteTokensAsync(sessionId);

            var fileReturn = await workflow.ProcessGuidedReturnAsync(sessionId, "file", "verification");
            Assert.Contains("prepare the release dossier", fileReturn.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Null(workflow.GetNextGuidedWorkflowRoute("file"));

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_release_dossier");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the release dossier",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_release_dossier");
                var response = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare the release dossier",
                    AgentName: "Release Dossier Agent",
                    SessionId: sessionId,
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/release-dossier",
                        ["agentblazor.approvals"] = "release_dossier.prepare_release_dossier"
                    }));

                Assert.False(response.RequiresApproval);
                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(response.ExecutionPlan!.Steps).Status);
                Assert.NotNull(workflow.CurrentDossier);
            }
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
        services.AddScoped<DojoRecipeReleaseWorkflowService>();
        services.AddScoped<ReleaseDossierWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ReleaseDossierCapabilities>()
            .AddAgent("Release Dossier Agent", agent =>
            {
                agent.WithAllowedActions(
                    "release_dossier.assess_release_dossier_readiness",
                    "release_dossier.advance_release_dossier_stage",
                    "release_dossier.prepare_release_dossier",
                    "release_dossier.apply_release_dossier_recovery_playbook",
                    "release_dossier.reset_release_dossier_workflow");
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
