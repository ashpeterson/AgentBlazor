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

public class FileAuditBundleWorkflowIntegrationTests
{
    [Fact]
    public async Task RuntimeAdapter_FileWorkflow_UsesContextBoundSessionParameter_ForSemanticSummary()
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
            chatClient.SetNextTool("summarize_workflow");

            AgentTurnResponse response;
            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                response = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Summarize the current file workflow",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle",
                        [AgentRuntimeContextKeys.ContextVersion] = "ctx-file-1"
                    }));
            }

            Assert.False(response.RequiresApproval);
            Assert.NotNull(response.ExecutionPlan);
            Assert.Equal("/demo/workflows/file-audit-bundle", response.ExecutionPlan!.Context.Route);
            Assert.Equal("ctx-file-1", response.ExecutionPlan.Context.ContextVersion);
            var step = Assert.Single(response.ExecutionPlan.Steps);
            Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
            Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
            Assert.Contains("file audit workflow", step.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(step.Warnings ?? [], static warning =>
                warning.Contains("Local mode", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(step.NextActions ?? [], static next =>
                next.Contains("Switch the workflow to remote upload mode", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(step.Outputs);
            Assert.Equal("Local", step.Outputs!["uploadMode"]);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_FileWorkflow_ApprovalGatedAuditBundle_ExecutesAfterApproval()
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
            var workflow = scope.ServiceProvider.GetRequiredService<DemoFileWorkflowService>();

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_audit_bundle");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-approval",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle"
                    }));

                Assert.True(approvalResponse.RequiresApproval);
                var pendingApproval = Assert.Single(approvalResponse.PendingApprovals);
                Assert.Equal("file_audit_bundle", pendingApproval.ComponentId);
                Assert.Equal("prepare_audit_bundle", pendingApproval.ActionId);

                chatClient.SetNextTool("prepare_audit_bundle");
                var approvedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-approval",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle",
                        ["agentblazor.approvals"] = "file_audit_bundle.prepare_audit_bundle"
                    }));

                Assert.False(approvedResponse.RequiresApproval);
                var executionPlan = Assert.Single(approvedResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Completed, executionPlan.Status);

                var snapshot = await workflow.GetOrCreateAsync("file-workflow-approval", "Remote");
                Assert.Contains(snapshot.Jobs, static job =>
                    string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(job.Status, "Verified", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_FileWorkflow_BlocksAuditBundle_WhenRemoteProcessingFails()
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
            var workflow = scope.ServiceProvider.GetRequiredService<DemoFileWorkflowService>();
            _ = await workflow.SyncFilesAsync("file-workflow-blocked", ["policy-reject.pdf"], "Remote");

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_audit_bundle");
                var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle"
                    }));

                Assert.True(approvalResponse.RequiresApproval);

                chatClient.SetNextTool("prepare_audit_bundle");
                var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-blocked",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle",
                        ["agentblazor.approvals"] = "file_audit_bundle.prepare_audit_bundle"
                    }));

                Assert.False(blockedResponse.RequiresApproval);
                var step = Assert.Single(blockedResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Blocked, step.Status);
                Assert.Contains("blocked", step.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(step.Warnings ?? [], static warning =>
                    warning.Contains("policy", StringComparison.OrdinalIgnoreCase) ||
                    warning.Contains("reject", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(step.NextActions ?? [], static next =>
                    next.Contains("replace or rename rejected files", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_FileWorkflow_RecoveryPlaybook_AllowsSuccessfulRetry()
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
            var workflow = scope.ServiceProvider.GetRequiredService<DemoFileWorkflowService>();
            _ = await workflow.SyncFilesAsync("file-workflow-recovery", ["policy-reject.pdf"], "Remote");

            using (scopeAccessor.Push(scope.ServiceProvider))
            {
                chatClient.SetNextTool("prepare_audit_bundle");
                _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle"
                    }));

                chatClient.SetNextTool("prepare_audit_bundle");
                var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle",
                        ["agentblazor.approvals"] = "file_audit_bundle.prepare_audit_bundle"
                    }));

                Assert.Equal(AgentExecutionStepStatus.Blocked, Assert.Single(blockedResponse.ExecutionPlan!.Steps).Status);

                chatClient.SetNextTool("apply_audit_recovery_playbook");
                var recoveryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Apply the file audit recovery playbook for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle"
                    }));

                var recoveryStep = Assert.Single(recoveryResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Completed, recoveryStep.Status);

                var recoveredSnapshot = await workflow.GetOrCreateAsync("file-workflow-recovery", "Remote");
                Assert.Contains(recoveredSnapshot.Files, static file =>
                    file.Contains("-recovered", StringComparison.OrdinalIgnoreCase));

                chatClient.SetNextTool("prepare_audit_bundle");
                var retryApproval = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle"
                    }));

                Assert.True(retryApproval.RequiresApproval);

                chatClient.SetNextTool("prepare_audit_bundle");
                var retryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                    "Prepare an audit-ready remote bundle for the current files",
                    AgentName: "File Workflow Agent",
                    SessionId: "file-workflow-recovery",
                    Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/file-audit-bundle",
                        ["agentblazor.approvals"] = "file_audit_bundle.prepare_audit_bundle"
                    }));

                var retryStep = Assert.Single(retryResponse.ExecutionPlan!.Steps);
                Assert.Equal(AgentExecutionStepStatus.Completed, retryStep.Status);
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
        services.AddScoped<DemoFileWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<DemoFileWorkflowCapabilities>()
            .AddAgent("File Workflow Agent", agent =>
            {
                agent.WithAllowedActions(
                    "file_audit_bundle.summarize_workflow",
                    "file_audit_bundle.switch_to_remote_mode",
                    "file_audit_bundle.prepare_audit_bundle",
                    "file_audit_bundle.apply_audit_recovery_playbook");
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
