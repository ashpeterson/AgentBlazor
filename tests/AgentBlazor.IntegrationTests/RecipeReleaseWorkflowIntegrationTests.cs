using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class RecipeReleaseWorkflowIntegrationTests
{
    [Fact]
    public async Task RuntimeAdapter_RecipeReleaseWorkflow_UsesContextBoundSessionParameter_ForReadinessAssessment()
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
            chatClient.SetNextTool("assess_release_readiness");

            AgentTurnResponse response;
            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                response = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Assess the current dojo recipe for release readiness",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release",
                        [AgentRuntimeContextKeys.ContextVersion] = "ctx-recipe-1"
                    }));
            }

            Assert.False(response.RequiresApproval);
            var plan = response.ExecutionPlan;
            Assert.NotNull(plan);
            Assert.Equal("/demo/workflows/recipe-release", plan!.Context.Route);
            Assert.Equal("ctx-recipe-1", plan.Context.ContextVersion);
            var step = Assert.Single(plan.Steps);
            Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
            Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
            Assert.Contains("release-ready", step.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(step.Outputs);
            Assert.Equal(4, step.Outputs!["ingredientCount"]);
            Assert.Equal(false, step.Outputs["vegan"]);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_RecipeReleaseWorkflow_BlockedDraft_IsReportedAfterApproval()
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
            var workspace = scope.ServiceProvider.GetRequiredService<DojoWorkspaceService>();
            var workflow = scope.ServiceProvider.GetRequiredService<DojoRecipeReleaseWorkflowService>();

            var snapshot = await workspace.GetSnapshotAsync("recipe-release-blocked", CancellationToken.None);
            snapshot.Recipe.Vegan = true;
            snapshot.Recipe.Vegetarian = true;
            _ = await workspace.SaveRecipeAsync("recipe-release-blocked", snapshot.Recipe, CancellationToken.None);

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_release_draft");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare a publish-ready release draft for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_release_draft");
                var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare a publish-ready release draft for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release",
                        ["agentblazor.approvals"] = "recipe_release.prepare_release_draft"
                    }));

                Assert.False(blockedResponse.RequiresApproval);
                Assert.Null(workflow.CurrentDraft);
                var step = Assert.Single(blockedResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Blocked, step.Status);
                Assert.Contains("vegan", step.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(step.Outputs?["blockers"] as string[] ?? [], static blocker =>
                    blocker.Contains("vegan", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_RecipeReleaseWorkflow_ApprovalGatedDraft_ExecutesAfterApproval()
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
            var workflow = scope.ServiceProvider.GetRequiredService<DojoRecipeReleaseWorkflowService>();

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_release_draft");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare a publish-ready release draft for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-approval",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release"
                    }));

                Assert.True(approvalResponse.RequiresApproval);
                var pendingApproval = Assert.Single(approvalResponse.PendingApprovals);
                Assert.Equal("recipe_release", pendingApproval.ComponentId);
                Assert.Equal("prepare_release_draft", pendingApproval.ActionId);

                chatClient.SetNextTool("prepare_release_draft");
                var approvedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare a publish-ready release draft for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-approval",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release",
                        ["agentblazor.approvals"] = "recipe_release.prepare_release_draft"
                    }));

                Assert.False(approvedResponse.RequiresApproval);
                Assert.NotNull(workflow.CurrentDraft);
                Assert.True(workflow.IsReleaseDialogOpen);
                var step = Assert.Single(approvedResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_RecipeReleaseWorkflow_RecoveryPlaybook_ClearsAutomaticBlocker_AndAllowsDraftPreparation()
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
            var workspace = scope.ServiceProvider.GetRequiredService<DojoWorkspaceService>();
            var workflow = scope.ServiceProvider.GetRequiredService<DojoRecipeReleaseWorkflowService>();

            var snapshot = await workspace.GetSnapshotAsync("recipe-release-recovery", CancellationToken.None);
            snapshot.Recipe.Vegan = true;
            snapshot.Recipe.Vegetarian = true;
            _ = await workspace.SaveRecipeAsync("recipe-release-recovery", snapshot.Recipe, CancellationToken.None);

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("apply_release_recovery_playbook");
                var recoveryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Apply the recipe release recovery playbook for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release"
                    }));

                var recoveryStep = Assert.Single(recoveryResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Completed, recoveryStep.Status);
                Assert.Contains("recovery", recoveryStep.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.False(workflow.Snapshot!.Recipe.Vegan);

                chatClient.SetNextTool("prepare_release_draft");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare a publish-ready release draft for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_release_draft");
                var approvedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare a publish-ready release draft for the current dojo recipe",
                    AgentName: "Recipe Release Agent",
                    SessionId: "recipe-release-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/recipe-release",
                        ["agentblazor.approvals"] = "recipe_release.prepare_release_draft"
                    }));

                Assert.False(approvedResponse.RequiresApproval);
                Assert.NotNull(workflow.CurrentDraft);
                Assert.True(workflow.IsReleaseDialogOpen);
                Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(approvedResponse.ExecutionPlan!.Steps).Status);
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
        services.AddSingleton<WorkflowToolChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<WorkflowToolChatClient>());
        services.AddDbContextFactory<DemoWorkflowDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<DojoWorkspaceService>();
        services.AddScoped<DojoRecipeReleaseWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<DojoRecipeReleaseCapabilities>()
            .AddAgent("Recipe Release Agent", agent =>
            {
                agent.WithAllowedActions(
                    "recipe_release.assess_release_readiness",
                    "recipe_release.prepare_release_draft",
                    "recipe_release.apply_release_recovery_playbook",
                    "recipe_release.reset_release_workflow");
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
