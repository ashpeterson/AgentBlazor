using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Adapters;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.Empty(response.PlannedActions);

        var execution = Assert.Single(response.ExecutionResults);
        Assert.Equal("supplier_compliance", execution.ComponentId);
        Assert.Equal("show_at_risk_suppliers", execution.ActionId);
        Assert.Equal("Prepared a 21-day at-risk supplier review.", execution.Message);
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
        Assert.Equal(AgentApprovalMode.None, step.PolicyDecision.ApprovalMode);
        Assert.Equal(AgentRiskClass.ReadOnly, step.PolicyDecision.RiskClass);
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
}
