using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Services;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class SupplierComplianceWorkflowIntegrationTests
{
    [Fact]
    public async Task RuntimeAdapter_SupplierWorkflow_UsesScopedWorkflowState_ForSemanticCapability()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowToolChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<WorkflowToolChatClient>());
        services.AddScoped<SupplierComplianceWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierComplianceCapabilities>()
            .AddAgent("Supplier Compliance Agent", agent =>
            {
                agent.WithAllowedActions(
                    "supplier_compliance.show_at_risk_suppliers",
                    "supplier_compliance.explain_at_risk_suppliers",
                    "supplier_compliance.prepare_remediation_draft",
                    "supplier_compliance.apply_supplier_recovery_playbook",
                    "supplier_compliance.reset_supplier_workflow");
            });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<SupplierComplianceWorkflowService>();
        chatClient.SetNextTool("show_at_risk_suppliers", new Dictionary<string, object?>
        {
            ["days"] = 30
        });

        AgentTurnResponse response;
        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            response = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Show suppliers likely to fail compliance review",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance",
                    [AgentRuntimeContextKeys.ContextVersion] = "ctx-supplier-1"
                }));
        }

        Assert.False(response.RequiresApproval);
        Assert.NotEmpty(workflow.HighlightedSupplierIds);
        Assert.NotNull(workflow.LatestInsight);
        Assert.False(response.UsesLegacyCompatibilityPayload);
        Assert.Empty(response.LegacyExecutionResults);

        var plan = response.ExecutionPlan;
        Assert.NotNull(plan);
        Assert.Equal("/demo/workflows/supplier-compliance", plan.Context.Route);
        Assert.Equal("ctx-supplier-1", plan.Context.ContextVersion);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
        Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
        Assert.Contains("Highlighted", step.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeAdapter_SupplierWorkflow_ApprovalGatedCapability_ExecutesAfterApproval()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowToolChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<WorkflowToolChatClient>());
        services.AddScoped<SupplierComplianceWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierComplianceCapabilities>()
            .AddAgent("Supplier Compliance Agent", agent =>
            {
                agent.WithAllowedActions(
                    "supplier_compliance.show_at_risk_suppliers",
                    "supplier_compliance.prepare_remediation_draft");
            });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<SupplierComplianceWorkflowService>();

        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            chatClient.SetNextTool("show_at_risk_suppliers", new Dictionary<string, object?>
            {
                ["days"] = 30
            });
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Show suppliers likely to fail compliance review",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-approval",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance"
                }));

            chatClient.SetNextTool("prepare_remediation_draft");
            var approvalResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare a remediation draft for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-approval",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance"
                }));

            Assert.True(approvalResponse.RequiresApproval);
            Assert.Null(workflow.CurrentDraft);
            var pendingApproval = Assert.Single(approvalResponse.PendingApprovals);
            Assert.Equal("supplier_compliance", pendingApproval.ComponentId);
            Assert.Equal("prepare_remediation_draft", pendingApproval.ActionId);

            chatClient.SetNextTool("prepare_remediation_draft");
            var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare a remediation draft for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-approval",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance",
                    ["agentblazor.approvals"] = "supplier_compliance.prepare_remediation_draft"
                }));

            Assert.False(blockedResponse.RequiresApproval);
            Assert.Null(workflow.CurrentDraft);
            Assert.NotEmpty(workflow.LatestDraftBlockers);
            var executionPlan = blockedResponse.ExecutionPlan;
            Assert.NotNull(executionPlan);
            var step = Assert.Single(executionPlan.Steps);
            Assert.Equal(AgentExecutionStepStatus.Blocked, step.Status);
        }
    }

    [Fact]
    public async Task RuntimeAdapter_SupplierWorkflow_RecoveryPlaybook_AllowsDraftPreparation_AfterBlockerClearance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowToolChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<WorkflowToolChatClient>());
        services.AddScoped<SupplierComplianceWorkflowService>();
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierComplianceCapabilities>()
            .AddAgent("Supplier Compliance Agent", agent =>
            {
                agent.WithAllowedActions(
                    "supplier_compliance.show_at_risk_suppliers",
                    "supplier_compliance.prepare_remediation_draft",
                    "supplier_compliance.apply_supplier_recovery_playbook");
            });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var chatClient = provider.GetRequiredService<WorkflowToolChatClient>();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<SupplierComplianceWorkflowService>();

        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            chatClient.SetNextTool("show_at_risk_suppliers", new Dictionary<string, object?>
            {
                ["days"] = 30
            });
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Show suppliers likely to fail compliance review",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance"
                }));

            chatClient.SetNextTool("prepare_remediation_draft");
            _ = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare a remediation draft for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance"
                }));

            chatClient.SetNextTool("prepare_remediation_draft");
            var blockedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare a remediation draft for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance",
                    ["agentblazor.approvals"] = "supplier_compliance.prepare_remediation_draft"
                }));

            Assert.Equal(AgentExecutionStepStatus.Blocked, Assert.Single(blockedResponse.ExecutionPlan!.Steps).Status);

            chatClient.SetNextTool("apply_supplier_recovery_playbook");
            var recoveryResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Apply the supplier recovery playbook for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance"
                }));

            Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(recoveryResponse.ExecutionPlan!.Steps).Status);
            Assert.NotEmpty(workflow.RecoveredSupplierIds);
            Assert.Empty(workflow.LatestDraftBlockers);

            chatClient.SetNextTool("prepare_remediation_draft");
            var retryApproval = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare a remediation draft for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance"
                }));

            Assert.True(retryApproval.RequiresApproval);

            chatClient.SetNextTool("prepare_remediation_draft");
            var approvedResponse = await adapter.RunTurnAsync(new AgentTurnRequest(
                "Prepare a remediation draft for the highlighted suppliers",
                AgentName: "Supplier Compliance Agent",
                SessionId: "supplier-workflow-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [AgentRuntimeContextKeys.CurrentRoute] = "/demo/workflows/supplier-compliance",
                    ["agentblazor.approvals"] = "supplier_compliance.prepare_remediation_draft"
                }));

            Assert.False(approvedResponse.RequiresApproval);
            Assert.NotNull(workflow.CurrentDraft);
            Assert.True(workflow.IsRemediationDialogOpen);
            Assert.Equal(AgentExecutionStepStatus.Completed, Assert.Single(approvedResponse.ExecutionPlan!.Steps).Status);
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
