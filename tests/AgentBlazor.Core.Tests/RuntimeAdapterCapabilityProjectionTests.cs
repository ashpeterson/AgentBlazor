using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Adapters;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Paid.Audit;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace AgentBlazor.Core.Tests;

public class RuntimeAdapterCapabilityProjectionTests
{
    [Fact]
    public async Task ChatClientRuntimeAdapter_ProjectsSemanticCapabilities_AheadOfUiTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityToolCatalogChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<CapabilityToolCatalogChatClient>());
        services.AddSingleton(new CapabilityRecorder());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierCapabilities>()
            .AddAgent("supplier-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions(
                    "supplier_compliance.show_at_risk_suppliers",
                    "AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<CapabilityToolCatalogChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Show at-risk suppliers",
            AgentName: "supplier-agent",
            SessionId: "capability-tool-snapshot"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        var capabilityIndex = snapshot.FindIndex(static name =>
            name.Contains("capability_supplier_compliance_show_at_risk_suppliers", StringComparison.OrdinalIgnoreCase));
        var uiIndex = snapshot.FindIndex(static name =>
            name.Contains("ui_AgentGrid_filter", StringComparison.OrdinalIgnoreCase));

        Assert.True(capabilityIndex >= 0);
        Assert.True(uiIndex >= 0);
        Assert.True(capabilityIndex < uiIndex);
    }

    [Fact]
    public async Task AddWorkflow_PreservesComponentTools_ForMixedWorkflowAgents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityToolCatalogChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<CapabilityToolCatalogChatClient>());
        services.AddSingleton(new CapabilityRecorder());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<SupplierCapabilities>("supplier-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithRoutePrefixes("/suppliers");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<CapabilityToolCatalogChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Show at-risk suppliers",
            AgentName: "supplier-agent",
            SessionId: "mixed-workflow-agent"));

        var snapshot = Assert.Single(chatClient.ToolSnapshots);
        Assert.Contains(
            snapshot,
            static name => name.Contains("capability_supplier_compliance_show_at_risk_suppliers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            snapshot,
            static name => name.Contains("ui_AgentGrid_filter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ExecutesProjectedSemanticCapability()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<CapabilityInvokingChatClient>());
        services.AddSingleton(new CapabilityRecorder());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierCapabilities>()
            .AddAgent("supplier-agent", agent =>
            {
                agent.WithAllowedActions("supplier_compliance.show_at_risk_suppliers");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var recorder = provider.GetRequiredService<CapabilityRecorder>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Show at-risk suppliers",
            AgentName: "supplier-agent",
            SessionId: "capability-execution",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.CurrentRoute] = "/suppliers",
                [AgentRuntimeContextKeys.ContextVersion] = "ctx-21"
            }));

        Assert.Equal(21, recorder.LastDays);
        Assert.False(response.UsesLegacyCompatibilityPayload);
        Assert.Empty(response.LegacyPlannedActions);
        Assert.Empty(response.LegacyExecutionResults);
        Assert.Equal("capability-invoked", response.ResponseText);
        Assert.NotNull(response.ExecutionPlan);
        var plan = response.ExecutionPlan!;
        Assert.Equal("supplier-agent", plan.AgentName);
        Assert.Equal("capability-execution", plan.Context.SessionId);
        Assert.Equal("/suppliers", plan.Context.Route);
        Assert.Equal("ctx-21", plan.Context.ContextVersion);
        Assert.Equal(AgentContextFreshness.Current, plan.Context.Freshness);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(AgentExecutionStepKind.SemanticCapability, step.Kind);
        Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
        Assert.Equal("supplier_compliance", step.TargetId);
        Assert.Equal("show_at_risk_suppliers", step.ActionId);
        Assert.Equal("Prepared a 21-day at-risk supplier review.", step.Message);
        Assert.Equal(AgentApprovalMode.None, step.PolicyDecision.ApprovalMode);
        Assert.Equal(AgentRiskClass.ReadOnly, step.PolicyDecision.RiskClass);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_RecordsSemanticCapabilityHistory_FromExecutionPlan()
    {
        var services = new ServiceCollection();
        var historyStore = new InMemoryActionHistoryStore();

        services.AddSingleton(historyStore);
        services.AddSingleton<IActionHistoryStore>(historyStore);
        services.AddSingleton<CapabilityInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<CapabilityInvokingChatClient>());
        services.AddSingleton(new CapabilityRecorder());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierCapabilities>()
            .AddAgent("supplier-agent", agent =>
            {
                agent.WithAllowedActions("supplier_compliance.show_at_risk_suppliers");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Show at-risk suppliers",
            AgentName: "supplier-agent",
            SessionId: "capability-history",
            UserId: "user-42"));

        var history = await historyStore.GetRecentAsync("capability-history");
        var entry = Assert.Single(history);
        Assert.Equal("user-42", entry.UserId);
        Assert.Equal("Show at-risk suppliers", entry.UserMessage);
        Assert.Equal("show_at_risk_suppliers", entry.ActionId);
        Assert.Equal("supplier_compliance", entry.AgentId);
        Assert.Equal(21, entry.Args["days"]);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StoresPromptTrace_FromNormalizedSemanticExecutionPlan()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<CapabilityInvokingChatClient>());
        services.AddSingleton(new CapabilityRecorder());
        services.AddAgentBlazorServices()
            .EnablePromptTracing()
            .UseChatClientRuntimeAdapter()
            .AddCapability<SupplierCapabilities>()
            .AddAgent("supplier-agent", agent =>
            {
                agent.WithAllowedActions("supplier_compliance.show_at_risk_suppliers");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Show at-risk suppliers",
            AgentName: "supplier-agent",
            SessionId: "capability-trace"));

        var trace = Assert.Single(await traceStore.GetBySessionAsync("capability-trace", 1));
        Assert.NotNull(trace.Classification);
        Assert.Equal("semantic_capability", trace.Classification!.PrimaryIntent);
        Assert.NotNull(trace.Planning);
        Assert.Contains(trace.Planning!.WorkflowSteps, action =>
            string.Equals(action.ComponentId, "supplier_compliance", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, "show_at_risk_suppliers", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(trace.Execution);
        Assert.Contains(trace.Execution!.ExecutionSteps, result =>
            string.Equals(result.ComponentId, "supplier_compliance", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, "show_at_risk_suppliers", StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_MapsBlockedCapabilityResult_ToBlockedExecutionStep()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BlockingCapabilityChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<BlockingCapabilityChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<BlockingCapabilities>()
            .AddAgent("blocking-agent", agent =>
            {
                agent.WithAllowedActions("recipe_release.prepare_release_draft");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Prepare the recipe release draft",
            AgentName: "blocking-agent",
            SessionId: "blocked-capability"));

        var step = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Blocked, step.Status);
        Assert.Contains("blocked", step.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_LogsApprovalRequestedAuditEvent_WhenCapabilityApprovalMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingAuditLogService>();
        services.AddSingleton<IAuditLogService>(static sp => sp.GetRequiredService<RecordingAuditLogService>());
        services.AddSingleton<ApprovalCapabilityChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalCapabilityChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ApprovalCapabilities>()
            .AddAgent("approval-agent", agent =>
            {
                agent.WithAllowedActions("approval_workflow.create_change_request");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var auditLog = provider.GetRequiredService<RecordingAuditLogService>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Create the change request",
            AgentName: "approval-agent",
            SessionId: "approval-missing"));

        Assert.True(response.RequiresApproval);
        var auditEvent = Assert.Single(auditLog.Events);
        Assert.Equal(AuditEventType.ActionApprovalRequested, auditEvent.EventType);
        Assert.Equal("approval-missing", auditEvent.UserId);
        Assert.Equal("approval_workflow.create_change_request", auditEvent.TargetId);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_LogsApprovalAndExecutionAuditEvents_ForApprovedCapability()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingAuditLogService>();
        services.AddSingleton<IAuditLogService>(static sp => sp.GetRequiredService<RecordingAuditLogService>());
        services.AddSingleton<ApprovalCapabilityChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalCapabilityChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ApprovalCapabilities>()
            .AddAgent("approval-agent", agent =>
            {
                agent.WithAllowedActions("approval_workflow.create_change_request");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var auditLog = provider.GetRequiredService<RecordingAuditLogService>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Create the change request",
            AgentName: "approval-agent",
            SessionId: "approval-granted",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.False(response.RequiresApproval);
        Assert.Equal("approval-capability-invoked", response.ResponseText);
        Assert.Collection(
            auditLog.Events,
            evt =>
            {
                Assert.Equal(AuditEventType.ActionApproved, evt.EventType);
                Assert.Equal("approval-granted", evt.UserId);
                Assert.Equal("approval_workflow.create_change_request", evt.TargetId);
            },
            evt =>
            {
                Assert.Equal(AuditEventType.ActionExecuted, evt.EventType);
                Assert.Equal("approval-granted", evt.UserId);
                Assert.Equal("approval_workflow.create_change_request", evt.TargetId);
            });
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ExecutesLegacyComponentToolAlias()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LegacyAliasInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<LegacyAliasInvokingChatClient>());
        services.AddSingleton<LegacyAliasRecordingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<LegacyAliasRecordingExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("dialog-agent", agent =>
            {
                agent.WithAllowedComponents("AgentDialog");
                agent.WithAllowedActions("AgentDialog.open");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentDialog",
                "Dialog surface.",
                new ComponentActionCapability(
                    "open",
                    "Open the dialog.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "target": { "type": "string" }
                          }
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var executor = provider.GetRequiredService<LegacyAliasRecordingExecutor>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Open the dialog",
            AgentName: "dialog-agent",
            SessionId: "legacy-alias-session",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.ProjectLegacyComponentToolAliases] = bool.TrueString
            }));

        var step = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal("AgentDialog", step.TargetId);
        Assert.Equal("open", step.ActionId);
        Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);

        var execution = Assert.Single(executor.Executions);
        Assert.Equal("AgentDialog", execution.ComponentId);
        Assert.Equal("open", execution.ActionId);
        var executionArguments = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(execution.Arguments);
        Assert.Equal("confirm-dialog", executionArguments["target"]);
        Assert.Equal("legacy-alias-invoked", response.ResponseText);
    }

    [AgentCapability(
        "supplier_compliance",
        Name = "Supplier Compliance",
        Description = "Semantic supplier workflows.",
        Category = "Compliance")]
    public sealed class SupplierCapabilities(CapabilityRecorder recorder)
    {
        [AgentAction(
            "Show suppliers likely to fail compliance review",
            ActionId = "show_at_risk_suppliers")]
        public CapabilityResult ShowAtRiskSuppliers([AgentParam("Days to look ahead")] int days = 30)
        {
            recorder.LastDays = days;
            return CapabilityResult.Success($"Prepared a {days}-day at-risk supplier review.");
        }
    }

    public sealed class CapabilityRecorder
    {
        public int LastDays { get; set; }
    }

    [AgentCapability("approval_workflow", Name = "Approval Workflow", Description = "Approval-gated change workflow.")]
    public sealed class ApprovalCapabilities
    {
        [AgentAction("Create a change request", ActionId = "create_change_request", RequiresApproval = true)]
        public CapabilityResult CreateChangeRequest([AgentParam("Change request name")] string name = "Default")
        {
            return CapabilityResult.Success($"Created change request '{name}'.");
        }
    }

    [AgentCapability("recipe_release", Name = "Recipe Release", Description = "Release workflow checks.")]
    public sealed class BlockingCapabilities
    {
        [AgentAction("Prepare a release draft", ActionId = "prepare_release_draft")]
        public CapabilityResult PrepareReleaseDraft()
        {
            return CapabilityResult.Blocked("Release is blocked because the recipe metadata conflicts with the ingredient list.");
        }
    }

    private sealed class CapabilityToolCatalogChatClient : IChatClient
    {
        public List<List<string>> ToolSnapshots { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = cancellationToken;
            ToolSnapshots.Add(
                [.. (options?.Tools?.Select(static tool => tool is AIFunction function ? function.Name : tool.GetType().Name) ?? [])]);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "catalog-recorded")));
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

    private sealed class CapabilityInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    function.Name.Contains("capability_supplier_compliance_show_at_risk_suppliers", StringComparison.OrdinalIgnoreCase)) ??
                []);
            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["days"] = 21
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "capability-invoked"));
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

    private sealed class ApprovalCapabilityChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    function.Name.Contains("capability_approval_workflow_create_change_request", StringComparison.OrdinalIgnoreCase)) ??
                []);

            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["name"] = "CAB-17"
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "approval-capability-invoked"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

    private sealed class RecordingAuditLogService : IAuditLogService
    {
        public List<AuditEvent> Events { get; } = [];

        public Task LogAsync(AuditEvent evt, CancellationToken ct = default)
        {
            _ = ct;
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task LogActionAsync(
            string userId,
            string? userEmail,
            string actionId,
            string agentId,
            bool succeeded,
            string? errorMessage = null,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            _ = ct;
            Events.Add(new AuditEvent(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                userId,
                userEmail,
                succeeded ? AuditEventType.ActionExecuted : AuditEventType.ActionFailed,
                "action",
                actionId,
                succeeded
                    ? $"Executed action '{actionId}' via agent '{agentId}'"
                    : $"Failed to execute action '{actionId}' via agent '{agentId}': {errorMessage}",
                ipAddress,
                new Dictionary<string, object?>
                {
                    ["actionId"] = actionId,
                    ["agentId"] = agentId,
                    ["succeeded"] = succeeded,
                    ["errorMessage"] = errorMessage
                }));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
        {
            _ = ct;
            IEnumerable<AuditEvent> filtered = Events;
            if (query.EventType is { } eventType)
            {
                filtered = filtered.Where(evt => evt.EventType == eventType);
            }

            if (!string.IsNullOrWhiteSpace(query.UserId))
            {
                filtered = filtered.Where(evt => string.Equals(evt.UserId, query.UserId, StringComparison.Ordinal));
            }

            return Task.FromResult<IReadOnlyList<AuditEvent>>(filtered.Take(query.Limit).ToArray());
        }

        public Task<IReadOnlyList<AuditEvent>> GetByUserAsync(string userId, int limit = 100, CancellationToken ct = default)
            => QueryAsync(new AuditQuery { UserId = userId, Limit = limit }, ct);

        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult<IReadOnlyList<AuditEvent>>(Events.TakeLast(limit).ToArray());
        }

        public Task<Stream> ExportAsync(AuditQuery query, AuditExportFormat format, CancellationToken ct = default)
        {
            _ = query;
            _ = format;
            _ = ct;
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task PruneAsync(int retentionDays = 365, CancellationToken ct = default)
        {
            _ = retentionDays;
            _ = ct;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingCapabilityChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    function.Name.Contains("capability_recipe_release_prepare_release_draft", StringComparison.OrdinalIgnoreCase)) ??
                []);
            await tool.InvokeAsync(new AIFunctionArguments(), cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "blocked-capability-invoked"));
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

    private sealed class LegacyAliasInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    string.Equals(function.Name, "agentblazor_agentdialog_open", StringComparison.OrdinalIgnoreCase)) ??
                []);

            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["target"] = "confirm-dialog"
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "legacy-alias-invoked"));
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

    private sealed class LegacyAliasRecordingExecutor : IComponentActionExecutor
    {
        public List<PlannedComponentAction> Executions { get; } = [];

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Executions.Add(action);
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: "legacy-alias-executed"));
        }
    }
}
