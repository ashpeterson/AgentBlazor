using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Licensing;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class PromptTracingTests
{
    [Fact]
    public async Task RunTurnAsync_WithTracingEnabled_StoresTraceInStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open the dialog"));

        Assert.Equal(1, traceStore.Count);
    }

    [Fact]
    public async Task RunTurnAsync_WithTracingDisabled_DoesNotStoreTrace()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open the dialog"));

        Assert.Equal(0, traceStore.Count);
    }

    [Fact]
    public async Task RunTurnAsync_WithTracingEnabled_TraceCapturesUserMessage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        const string userMessage = "please open the dialog for me";
        _ = await runtime.RunTurnAsync(new AgentTurnRequest(userMessage));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);
        Assert.Equal(userMessage, trace.Entry.UserMessage);
    }

    [Fact]
    public async Task RunTurnAsync_WithTracingEnabled_TraceCapturesSessionId()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        const string sessionId = "test-session-123";
        _ = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.session_id"] = sessionId
            }));

        var traces = await traceStore.GetBySessionAsync(sessionId, 1);
        var trace = Assert.Single(traces);
        Assert.Equal(sessionId, trace.Entry.SessionId);
    }

    [Fact]
    public async Task RunTurnAsync_WithTracingEnabled_TraceCapturesPlannedActions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient(
            "agentblazor_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskScore",
                ["operator"] = "gte",
                ["value"] = 70
            }));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("filter by high risk suppliers"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Planning);
        Assert.NotEmpty(trace.Planning.WorkflowSteps);
        Assert.Contains(trace.Planning.WorkflowSteps, action =>
            string.Equals(action.ComponentId, "AgentDataGrid", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, "filter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunTurnAsync_WithTracingEnabled_TraceCapturesExecutionResults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Execution);
        Assert.NotEmpty(trace.Execution.ExecutionSteps);
        Assert.Contains(trace.Execution.ExecutionSteps, result =>
            string.Equals(result.ComponentId, "AgentDialog", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, "open", StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    [Fact]
    public async Task RunTurnAsync_WithTracingEnabled_TraceCapturesResponse()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Response);
        Assert.Equal(PromptTraceOutcome.Succeeded, trace.Response.Outcome);
        Assert.NotEmpty(trace.Response.ResponseText);
        Assert.True(trace.Response.TotalDuration > TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(ComponentTestCases))]
    public async Task RunTurnAsync_TracesComponentAction(
        string toolName,
        string expectedComponentId,
        string expectedActionId,
        IDictionary<string, object?>? arguments)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient(toolName, arguments));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();
        services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("perform the action"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Planning);
        Assert.Contains(trace.Planning.WorkflowSteps, action =>
            string.Equals(action.ComponentId, expectedComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, expectedActionId, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(trace.Execution);
        Assert.Contains(trace.Execution.ExecutionSteps, result =>
            string.Equals(result.ComponentId, expectedComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, expectedActionId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
    }

    public static IEnumerable<object?[]> ComponentTestCases()
    {
        // NavMenu navigation
        yield return new object?[]
        {
            "agentblazor_agentnavmenu_navigate_to",
            "AgentNavMenu",
            "navigate_to",
            new Dictionary<string, object?> { ["uri"] = "/suppliers" }
        };

        // DataGrid filter
        yield return new object?[]
        {
            "agentblazor_agentdatagrid_filter",
            "AgentDataGrid",
            "filter",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskLevel",
                ["operator"] = "eq",
                ["value"] = "High"
            }
        };

        // DataGrid sort
        yield return new object?[]
        {
            "agentblazor_agentdatagrid_sort",
            "AgentDataGrid",
            "sort",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskScore",
                ["direction"] = "desc"
            }
        };

        // DataGrid clear filters
        yield return new object?[]
        {
            "agentblazor_agentdatagrid_clear_filters",
            "AgentDataGrid",
            "clear_filters",
            null
        };

        // DataGrid go to page
        yield return new object?[]
        {
            "agentblazor_agentdatagrid_go_to_page",
            "AgentDataGrid",
            "go_to_page",
            new Dictionary<string, object?> { ["page"] = 2 }
        };

        // DataGrid select row
        yield return new object?[]
        {
            "agentblazor_agentdatagrid_select_row",
            "AgentDataGrid",
            "select_row",
            new Dictionary<string, object?> { ["row_index"] = 0 }
        };

        // Form set field
        yield return new object?[]
        {
            "agentblazor_agentform_set_field",
            "AgentForm",
            "set_field",
            new Dictionary<string, object?>
            {
                ["field"] = "supplierName",
                ["value"] = "Northwind"
            }
        };

        // Dialog open
        yield return new object?[]
        {
            "agentblazor_agentdialog_open",
            "AgentDialog",
            "open",
            null
        };

        // Dialog close
        yield return new object?[]
        {
            "agentblazor_agentdialog_close",
            "AgentDialog",
            "close",
            null
        };

        // Tabs switch_tab
        yield return new object?[]
        {
            "agentblazor_agenttabs_switch_tab",
            "AgentTabs",
            "switch_tab",
            new Dictionary<string, object?> { ["index"] = 1 }
        };
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsCorrectCounts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceService = provider.GetRequiredService<IPromptTraceService>();

        // Run multiple prompts with different tools
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        var dialogRuntime = CreateRuntimeWithTool(provider, "agentblazor_agentdialog_open");
        _ = await dialogRuntime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var navRuntime = CreateRuntimeWithTool(provider, "agentblazor_agentnavmenu_navigate_to",
            new Dictionary<string, object?> { ["uri"] = "/suppliers" });
        _ = await navRuntime.RunTurnAsync(new AgentTurnRequest("go to suppliers"));

        var stats = await traceService.GetStatisticsAsync();

        Assert.True(stats.TotalTraces >= 1);
        Assert.True(stats.SucceededCount >= 0);
    }

    [Fact]
    public async Task GenerateReportAsync_ReturnsMarkdownReport()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceService = provider.GetRequiredService<IPromptTraceService>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var report = await traceService.GenerateReportAsync(10);

        Assert.Contains("# Prompt Trace Report", report);
        Assert.Contains("## Summary Statistics", report);
        Assert.Contains("## Trace Details", report);
        Assert.Contains("#### Workflow Planning", report);
        Assert.Contains("#### Workflow Execution", report);
        Assert.Contains("Planned Workflow Steps", report);
        Assert.Contains("open dialog", report);
    }

    [Fact]
    public async Task ClearTracesAsync_RemovesAllTraces()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();
        var traceService = provider.GetRequiredService<IPromptTraceService>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));
        Assert.Equal(1, traceStore.Count);

        await traceService.ClearTracesAsync();
        Assert.Equal(0, traceStore.Count);
    }

    [Fact]
    public async Task RunTurnAsync_WithFailedAction_TraceCapturesFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdatagrid_sort"));
        services.AddSingleton<TracingFailingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingFailingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("sort the data"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Execution);
        Assert.Contains(trace.Execution.Results, result =>
            !result.Succeeded &&
            result.Message.Contains("requires", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, trace.Execution.FailureCount);
    }

    [Fact]
    public async Task GetByOutcomeAsync_FiltersCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var succeededTraces = await traceStore.GetByOutcomeAsync(PromptTraceOutcome.Succeeded);
        Assert.NotEmpty(succeededTraces);
        Assert.All(succeededTraces, t =>
            Assert.Equal(PromptTraceOutcome.Succeeded, t.Response?.Outcome));
    }

    [Fact]
    public async Task RunTurnAsync_TraceCapturesToolCount()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Planning);
        Assert.True(trace.Planning.ToolCount > 0);
    }

    [Fact]
    public async Task RunTurnAsync_TraceCapturesDurations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);

        Assert.NotNull(trace.Response);
        Assert.True(trace.Response.TotalDuration > TimeSpan.Zero);

        Assert.NotNull(trace.Execution);
        Assert.True(trace.Execution.TotalDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task TraceService_GetTracesAsync_WithQuery_FiltersCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new TracingToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<TracingCountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        services.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceService = provider.GetRequiredService<IPromptTraceService>();

        const string sessionId = "query-test-session";
        _ = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.session_id"] = sessionId
            }));

        var query = new PromptTraceQuery { SessionId = sessionId };
        var traces = await traceService.GetTracesAsync(query);

        Assert.Single(traces);
        Assert.Equal(sessionId, traces[0].Entry.SessionId);
    }

    private static IAgentRuntime CreateRuntimeWithTool(
        ServiceProvider provider,
        string toolName,
        IDictionary<string, object?>? arguments = null)
    {
        var newServices = new ServiceCollection();
        newServices.AddSingleton<IChatClient>(new TracingToolThenTextChatClient(toolName, arguments));
        newServices.AddSingleton(provider.GetRequiredService<TracingCountingExecutor>());
        newServices.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<TracingCountingExecutor>());
        newServices.AddSingleton(provider.GetRequiredService<IPromptTraceStore>());
        newServices.AddSingleton(provider.GetRequiredService<IPromptTraceService>());
        newServices.AddAgentBlazorServices()
            .UseLegacyDefaultAgentFallback()
            .EnablePromptTracing();

        var newProvider = newServices.BuildServiceProvider();
        return newProvider.GetRequiredService<IAgentRuntime>();
    }

    #region Test Doubles

    private static string BuildPlanJson(
        string toolName,
        IDictionary<string, object?>? arguments = null)
    {
        if (!TryResolveToolName(toolName, out var componentId, out var actionId))
        {
            return """{"message":"","actions":[],"needsClarification":false,"clarificationQuestion":null}""";
        }

        var payload = new
        {
            message = $"Executing {componentId}.{actionId}",
            actions = new[]
            {
                new
                {
                    agentId = componentId,
                    action = actionId,
                    args = arguments ?? new Dictionary<string, object?>()
                }
            },
            needsClarification = false,
            clarificationQuestion = (string?)null
        };

        return JsonSerializer.Serialize(payload);
    }

    private static bool TryResolveToolName(
        string toolName,
        out string componentId,
        out string actionId)
    {
        componentId = string.Empty;
        actionId = string.Empty;

        return toolName.ToLowerInvariant() switch
        {
            "agentblazor_agentnavmenu_navigate_to" => Resolve("AgentNavMenu", "navigate_to", out componentId, out actionId),
            "agentblazor_agentdatagrid_filter" => Resolve("AgentDataGrid", "filter", out componentId, out actionId),
            "agentblazor_agentdatagrid_sort" => Resolve("AgentDataGrid", "sort", out componentId, out actionId),
            "agentblazor_agentdatagrid_clear_filters" => Resolve("AgentDataGrid", "clear_filters", out componentId, out actionId),
            "agentblazor_agentdatagrid_go_to_page" => Resolve("AgentDataGrid", "go_to_page", out componentId, out actionId),
            "agentblazor_agentdatagrid_select_row" => Resolve("AgentDataGrid", "select_row", out componentId, out actionId),
            "agentblazor_agentform_set_field" => Resolve("AgentForm", "set_field", out componentId, out actionId),
            "agentblazor_agentdialog_open" => Resolve("AgentDialog", "open", out componentId, out actionId),
            "agentblazor_agentdialog_close" => Resolve("AgentDialog", "close", out componentId, out actionId),
            "agentblazor_agenttabs_switch_tab" => Resolve("AgentTabs", "switch_tab", out componentId, out actionId),
            _ => false
        };
    }

    private static bool Resolve(
        string resolvedComponentId,
        string resolvedActionId,
        out string componentId,
        out string actionId)
    {
        componentId = resolvedComponentId;
        actionId = resolvedActionId;
        return true;
    }

    private sealed class TracingToolThenTextChatClient(
        string functionName,
        IDictionary<string, object?>? arguments = null) : IChatClient
    {
        private readonly IDictionary<string, object?> _arguments = arguments ?? new Dictionary<string, object?>();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                BuildPlanJson(functionName, _arguments))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, BuildPlanJson(functionName, _arguments));
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class TracingCountingExecutor : IComponentActionExecutor
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Executed {action.ComponentId}.{action.ActionId}"));
        }
    }

    private sealed class TracingFailingExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: false,
                Message: "Action 'sort' requires 'column' parameter."));
        }
    }

    #endregion
}
