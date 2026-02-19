using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgentBlazor.Components;
using AgentBlazor.Licensing;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using AgentBlazor.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class AgentRuntimeIntegrationTests
{
    [Fact]
    public async Task RunTurnAsync_WithComponentTool_RecordsPlannedAction_AndExecutionResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentchatwidget_open_widget"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open the widget"));

        Assert.Equal(1, executor.CallCount);
        Assert.Contains(response.PlannedActions, static action =>
            string.Equals(action.ComponentId, "AgentChatWidget", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, "open_widget", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, "AgentChatWidget", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, "open_widget", StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    [Fact]
    public async Task RunTurnAsync_ComponentTool_ForwardsToolArguments_ToExecutor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskScore",
                ["operator"] = "<=",
                ["value"] = 50
            }));
        services.AddSingleton<CapturingPlannedActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CapturingPlannedActionExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CapturingPlannedActionExecutor>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("filter low risk suppliers"));

        var action = Assert.IsType<PlannedComponentAction>(executor.LastAction);
        Assert.Equal(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, action.ComponentId);
        Assert.Equal(AgentComponentV1CapabilityProfile.DataGridFilterActionId, action.ActionId);
        Assert.NotNull(action.Arguments);
        Assert.Equal("RiskScore", action.Arguments["column"]?.ToString());
        Assert.Equal("<=", action.Arguments["operator"]?.ToString());
        Assert.Equal("50", action.Arguments["value"]?.ToString());
    }

    [Fact]
    public async Task RunTurnAsync_WhenActionIsMissingRequiredParameter_AppendsClarificationGuidance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdatagrid_sort"));
        services.AddSingleton<MissingParameterExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<MissingParameterExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("sort from highest to lowest"));

        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
            !result.Succeeded &&
            result.Message.Contains("requires 'column' parameter", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("I need one more detail", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Did you mean sort by 'RiskScore' descending?", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_WhenUserConfirmsClarification_ExecutesPendingAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdatagrid_sort"));
        services.AddSingleton<SortNeedsColumnExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<SortNeedsColumnExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<SortNeedsColumnExecutor>();
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentblazor.session_id"] = "integration-session-1"
        };

        var first = await runtime.RunTurnAsync(new AgentTurnRequest(
            "sort highest to lowest",
            Context: context));
        var second = await runtime.RunTurnAsync(new AgentTurnRequest(
            "yes",
            Context: context));

        Assert.Contains("Did you mean sort by 'RiskScore' descending?", first.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(second.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
        Assert.Equal(2, executor.CallCount);
        Assert.Equal("RiskScore", executor.LastColumn);
        Assert.Equal("desc", executor.LastDirection);
    }

    [Fact]
    public async Task RunTurnAsync_EmitsRuntimeTelemetryStartedAndFinishedEvents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddSingleton<CapturingTelemetrySink>();
        services.AddSingleton<IAgentBlazorTelemetrySink>(sp => sp.GetRequiredService<CapturingTelemetrySink>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var telemetry = provider.GetRequiredService<CapturingTelemetrySink>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var started = Assert.Single(telemetry.Events, static e => e.Kind == AgentBlazorRunEventKind.Started);
        var finished = Assert.Single(telemetry.Events, static e => e.Kind == AgentBlazorRunEventKind.Finished);

        Assert.Equal(AgentBlazorTelemetrySources.Runtime, started.Source);
        Assert.Equal("AgentBlazor UI Agent", started.AgentName);
        Assert.Equal(AgentBlazorRunOutcome.Succeeded, finished.Outcome);
        Assert.Equal(response.PlannedActions.Count, finished.PlannedActionCount);
        Assert.Equal(response.ExecutionResults.Count, finished.ExecutionResultCount);
    }

    [Fact]
    public async Task RunTurnAsync_IncludesRegisteredWrapperSnapshot_InInstructions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapturingInstructionChatClient>();
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<CapturingInstructionChatClient>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new StubRegisteredComponent(
            "supplier-grid",
            "DataGrid",
            new Dictionary<string, object?>
            {
                ["riskScoreThreshold"] = 7,
                ["currentPage"] = 2
            }));

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var chatClient = provider.GetRequiredService<CapturingInstructionChatClient>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("what can you do?"));

        Assert.Contains("supplier-grid", chatClient.LastInstructions ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("riskScoreThreshold", chatClient.LastInstructions ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("currentPage", chatClient.LastInstructions ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_WithStructuredJsonDirectiveText_ExecutesMappedAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonDirectiveThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        Assert.Equal(1, executor.CallCount);
        Assert.Contains(response.PlannedActions, static action =>
            string.Equals(action.ComponentId, AgentComponentV1CapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, AgentComponentV1CapabilityProfile.DialogOpenActionId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.DialogOpenActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    [Fact]
    public async Task RunTurnAsync_WithAllowedActionsPolicy_DoesNotExecuteDisallowedAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentchatwidget_open_widget"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices()
            .AddAgent("policy-agent", agent =>
            {
                agent.WithAllowedComponents("AgentChatWidget");
                agent.WithAllowedActions("AgentChatWidget.close_widget");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open the widget",
            AgentName: "policy-agent"));

        Assert.Equal(0, executor.CallCount);
        Assert.Empty(response.PlannedActions);
        Assert.Empty(response.ExecutionResults);
        Assert.Contains("Policy filtered disallowed actions", response.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTurnAsync_DefaultAndCustomAgentRouting_AppliesDeterministicMudPolicies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices()
            .AddAgent("risk-only-agent", agent =>
            {
                agent.WithAllowedComponents(AgentComponentV1CapabilityProfile.AgentDataGridComponentId);
                agent.WithAllowedActions(
                    $"{AgentComponentV1CapabilityProfile.AgentDataGridComponentId}.{AgentComponentV1CapabilityProfile.DataGridFilterActionId}",
                    $"{AgentComponentV1CapabilityProfile.AgentDataGridComponentId}.{AgentComponentV1CapabilityProfile.DataGridSortActionId}");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var defaultRouteResponse = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));
        var riskRouteResponse = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            AgentName: "risk-only-agent"));

        Assert.Equal(1, executor.CallCount);
        Assert.Contains(defaultRouteResponse.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.DialogOpenActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);

        Assert.Empty(riskRouteResponse.PlannedActions);
        Assert.Empty(riskRouteResponse.ExecutionResults);
        Assert.Contains("Policy filtered disallowed actions", riskRouteResponse.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTurnAsync_WhenPolicyRemovesAllMudActions_ReturnsPolicyMessage_AndSkipsProviderExecution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CountingChatClient>();
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<CountingChatClient>());
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices()
            .AddAgent("policy-empty-agent", agent =>
            {
                agent.WithAllowedComponents(AgentComponentV1CapabilityProfile.AgentDialogComponentId);
                agent.WithAllowedActions($"{AgentComponentV1CapabilityProfile.AgentDialogComponentId}.not_an_action");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();
        var chatClient = provider.GetRequiredService<CountingChatClient>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            AgentName: "policy-empty-agent"));

        Assert.Equal(0, executor.CallCount);
        Assert.Equal(0, chatClient.CallCount);
        Assert.Contains("No allowed component actions are available for this agent policy", response.ResponseText, StringComparison.Ordinal);
        Assert.Contains("Filtered actions:", response.ResponseText, StringComparison.Ordinal);
        Assert.Empty(response.PlannedActions);
        Assert.Empty(response.ExecutionResults);
    }

    [Fact]
    public async Task RunTurnAsync_ApprovalRequiredTool_SkipsExecutor_WhenApprovalMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_confirm"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("confirm the dialog"));

        Assert.Equal(0, executor.CallCount);
        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.DialogConfirmActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Message.Contains("Approval required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTurnAsync_MudApprovalRequiredTool_SkipsExecutor_WhenApprovalMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("submit the form"));

        Assert.Equal(0, executor.CallCount);
        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Message.Contains("Approval required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTurnAsync_ApprovalRequiredTool_Executes_WhenApprovalContextProvided()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_confirm"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "confirm the dialog",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.Equal(1, executor.CallCount);
        Assert.DoesNotContain(response.ExecutionResults, static result =>
            result.Message.Contains("Approval required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTurnAsync_MudApprovalRequiredTool_Executes_WhenApprovalContextProvided()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "submit the form",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.Equal(1, executor.CallCount);
        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    [Fact]
    public async Task RunTurnAsync_MudPremiumAction_IsBlocked_WhenTierIsPaid()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);
        services
            .AddAgentBlazorServices()
            .AddAgent("premium-submit-agent", agent =>
            {
                agent.WithAllowedComponents(AgentComponentV1CapabilityProfile.AgentFormComponentId);
                agent.WithAllowedActions($"{AgentComponentV1CapabilityProfile.AgentFormComponentId}.{AgentComponentV1CapabilityProfile.FormSubmitActionId}");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "submit the form",
            AgentName: "premium-submit-agent",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.Equal(0, executor.CallCount);
        Assert.Empty(response.PlannedActions);
        Assert.Empty(response.ExecutionResults);
        Assert.Contains("Current tier: Paid", response.ResponseText, StringComparison.Ordinal);
        Assert.Contains("Filtered actions:", response.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTurnAsync_WithToolsFromAssembly_InvokesRegisteredAssemblyTool()
    {
        RuntimeCustomTools.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_external_runtimecustomtools_getsupplierstatus",
            new Dictionary<string, object?>
            {
                ["supplierId"] = "acme"
            }));
        services
            .AddAgentBlazorServices()
            .AddAgent("custom-tools-agent", agent =>
            {
                agent.WithToolsFromAssembly(typeof(RuntimeCustomTools).Assembly);
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest(
            "get supplier status",
            AgentName: "custom-tools-agent"));

        Assert.Equal(1, RuntimeCustomTools.CallCount);
        Assert.Equal("acme", RuntimeCustomTools.LastSupplierId);
    }

    [Fact]
    public async Task RunTurnAsync_WithMudAndAssemblyTools_ExecutesBothInSingleRun()
    {
        RuntimeCustomTools.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MultiToolThenTextChatClient(
        [
            new ToolInvocation(
                "agentblazor_external_runtimecustomtools_getsupplierstatus",
                new Dictionary<string, object?> { ["supplierId"] = "acme" }),
            new ToolInvocation("agentblazor_agentdialog_open")
        ]));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices()
            .AddAgent("mixed-tools-agent", agent =>
            {
                agent.WithToolsFromAssembly(typeof(RuntimeCustomTools).Assembly);
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "check supplier and open dialog",
            AgentName: "mixed-tools-agent"));

        Assert.Equal(1, RuntimeCustomTools.CallCount);
        Assert.Equal("acme", RuntimeCustomTools.LastSupplierId);
        Assert.Equal(1, executor.CallCount);
        Assert.Contains(response.PlannedActions, static action =>
            string.Equals(action.ComponentId, AgentComponentV1CapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, AgentComponentV1CapabilityProfile.DialogOpenActionId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.ExecutionResults, static result =>
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, AgentComponentV1CapabilityProfile.DialogOpenActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    public static class RuntimeCustomTools
    {
        public static int CallCount { get; private set; }

        public static string? LastSupplierId { get; private set; }

        public static void Reset()
        {
            CallCount = 0;
            LastSupplierId = null;
        }

        [Description("Gets supplier status information by supplier id.")]
        public static string GetSupplierStatus(string supplierId)
        {
            CallCount++;
            LastSupplierId = supplierId;
            return $"Supplier {supplierId} is low risk.";
        }
    }

    private sealed class ToolThenTextChatClient(string functionName, IDictionary<string, object?>? arguments = null) : IChatClient
    {
        private readonly IDictionary<string, object?> _arguments = arguments ?? new Dictionary<string, object?>();
        private int _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber % 2 == 1)
            {
                var functionCall = new FunctionCallContent("call_test123", functionName, _arguments);
                var callMessage = new ChatMessage(ChatRole.Assistant, [functionCall]);
                return Task.FromResult(new ChatResponse([callMessage]) { FinishReason = ChatFinishReason.ToolCalls });
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Completed.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber % 2 == 1)
            {
                yield return new ChatResponseUpdate
                {
                    Contents = [new FunctionCallContent("call_test123", functionName, _arguments)],
                    Role = ChatRole.Assistant,
                    FinishReason = ChatFinishReason.ToolCalls
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Completed.");
            }

            await Task.CompletedTask;
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

    private sealed class JsonDirectiveThenTextChatClient(string functionName) : IChatClient
    {
        private int _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber % 2 == 1)
            {
                var payload = $$"""
                                {
                                  "name": "{{functionName}}",
                                  "arguments": {}
                                }
                                """;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, payload)));
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Completed.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber % 2 == 1)
            {
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    $$"""
                      {
                        "name": "{{functionName}}",
                        "arguments": {}
                      }
                      """);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Completed.");
            }

            await Task.CompletedTask;
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

    private sealed class MultiToolThenTextChatClient(IReadOnlyList<ToolInvocation> toolInvocations) : IChatClient
    {
        private int _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber <= toolInvocations.Count)
            {
                var invocation = toolInvocations[callNumber - 1];
                var functionCall = new FunctionCallContent($"call_{callNumber}", invocation.Name, invocation.Arguments);
                var callMessage = new ChatMessage(ChatRole.Assistant, [functionCall]);
                return Task.FromResult(new ChatResponse([callMessage]) { FinishReason = ChatFinishReason.ToolCalls });
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Completed.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            var callNumber = Interlocked.Increment(ref _callCount);
            if (callNumber <= toolInvocations.Count)
            {
                var invocation = toolInvocations[callNumber - 1];
                yield return new ChatResponseUpdate
                {
                    Contents = [new FunctionCallContent($"call_{callNumber}", invocation.Name, invocation.Arguments)],
                    Role = ChatRole.Assistant,
                    FinishReason = ChatFinishReason.ToolCalls
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Completed.");
            }

            await Task.CompletedTask;
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

    private sealed record ToolInvocation(string Name, IDictionary<string, object?>? Arguments = null);

    private sealed class CapturingInstructionChatClient : IChatClient
    {
        public string? LastInstructions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = cancellationToken;
            LastInstructions = options?.Instructions;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ready.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = cancellationToken;
            LastInstructions = options?.Instructions;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Ready.");
            await Task.CompletedTask;
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

    private sealed class StubRegisteredComponent(
        string agentId,
        string componentType,
        IReadOnlyDictionary<string, object?> state) : IAgentControllable
    {
        public string AgentId { get; } = agentId;

        public string ComponentType { get; } = componentType;

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Registered wrapper component.");
            capability.UpsertAction(new ComponentActionCapability("sort", "Sort data."));
            capability.UpsertAction(new ComponentActionCapability("filter", "Filter data."));
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in state)
            {
                values[pair.Key] = pair.Value;
            }

            return new ComponentState(values);
        }

        public Task<ActionResult> ExecuteActionAsync(
            AgentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = action;
            _ = cancellationToken;
            return Task.FromResult(ActionResult.Success("ok"));
        }
    }

    private sealed class CountingChatClient : IChatClient
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Should not be called.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Should not be called.");
            await Task.CompletedTask;
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

    private sealed class CountingExecutor : IComponentActionExecutor
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Executed {action.ComponentId}.{action.ActionId}"));
        }
    }

    private sealed class CapturingPlannedActionExecutor : IComponentActionExecutor
    {
        public PlannedComponentAction? LastAction { get; private set; }

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastAction = action;
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Executed {action.ComponentId}.{action.ActionId}"));
        }
    }

    private sealed class MissingParameterExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: false,
                Message: "Action 'sort' requires 'column' parameter."));
        }
    }

    private sealed class SortNeedsColumnExecutor : IComponentActionExecutor
    {
        private int _callCount;

        public int CallCount => _callCount;

        public string? LastColumn { get; private set; }

        public string? LastDirection { get; private set; }

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);

            if (!string.Equals(action.ComponentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(action.ActionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: true,
                    Message: "Executed non-sort action."));
            }

            if (action.Arguments is null ||
                !action.Arguments.TryGetValue("column", out var columnRaw) ||
                string.IsNullOrWhiteSpace(columnRaw?.ToString()))
            {
                return Task.FromResult(new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: false,
                    Message: "Action 'sort' requires 'column' parameter."));
            }

            LastColumn = columnRaw!.ToString();
            LastDirection = action.Arguments.TryGetValue("direction", out var directionRaw)
                ? directionRaw?.ToString()
                : null;

            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Sorted by {LastColumn} ({LastDirection ?? "asc"})."));
        }
    }

    private sealed class CapturingTelemetrySink : IAgentBlazorTelemetrySink
    {
        private readonly List<AgentBlazorRunTelemetryEvent> _events = [];

        public IReadOnlyList<AgentBlazorRunTelemetryEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public ValueTask TrackRunEventAsync(
            AgentBlazorRunTelemetryEvent telemetryEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            lock (_events)
            {
                _events.Add(telemetryEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}
