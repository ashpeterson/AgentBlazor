using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Hosting;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using AgentBlazor.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class AgUiHostingIntegrationTests
{
    [Fact]
    public async Task AgUiRun_ApprovalRequiredAction_SkipsExecution_WhenApprovalMissing()
    {
        var app = await CreateAppAsync(
            BuildPlanJson("AgentForm", "submit"),
            tier: AgentBlazorTier.Premium);
        try
        {
            using var client = CreateClient(app);
            var executor = app.Services.GetRequiredService<CountingExecutor>();

            var response = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload());
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("TEXT_MESSAGE_CONTENT", body, StringComparison.Ordinal);
            Assert.Contains("STATE_SNAPSHOT", body, StringComparison.Ordinal);
            Assert.Contains("approval_required", body, StringComparison.Ordinal);
            Assert.Contains("Approval required for AgentForm.submit.", body, StringComparison.Ordinal);
            Assert.Equal(0, executor.CallCount);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_ApprovalRequiredAction_Executes_WhenApprovalProvided()
    {
        var app = await CreateAppAsync(
            BuildPlanJson("AgentForm", "submit"),
            tier: AgentBlazorTier.Premium);
        try
        {
            using var client = CreateClient(app);
            var executor = app.Services.GetRequiredService<CountingExecutor>();

            var response = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload([
                    new KeyValuePair<string, string>("agentblazor.approvals", "all")
                ]));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("TEXT_MESSAGE_CONTENT", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_START", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_ARGS", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_RESULT", body, StringComparison.Ordinal);
            Assert.Contains("AgentForm.submit", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Approval required for AgentForm.submit.", body, StringComparison.Ordinal);
            Assert.Equal(1, executor.CallCount);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_EmitsLifecycleAndToolEvents_ForDeterministicExecution()
    {
        var app = await CreateAppAsync(BuildPlanJson("AgentForm", "validate"));
        try
        {
            using var client = CreateClient(app);
            var executor = app.Services.GetRequiredService<CountingExecutor>();

            var response = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload());
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("TEXT_MESSAGE_CONTENT", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_START", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_ARGS", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_RESULT", body, StringComparison.Ordinal);
            Assert.Contains("AgentForm.validate", body, StringComparison.Ordinal);
            Assert.Equal(1, executor.CallCount);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_PaidTier_ExecutesFormSubmitAction()
    {
        var app = await CreateAppAsync(
            BuildPlanJson("AgentForm", "submit"),
            tier: AgentBlazorTier.Paid,
            configureOptions: options =>
            {
                options.DefaultAgent.AllowedComponents.Add("AgentForm");
                options.DefaultAgent.AllowedActions.Add("AgentForm.submit");
            });
        try
        {
            using var client = CreateClient(app);
            var executor = app.Services.GetRequiredService<CountingExecutor>();

            var response = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload([
                    new KeyValuePair<string, string>("agentblazor.approvals", "all")
                ]));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_START", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_END", body, StringComparison.Ordinal);
            Assert.True(executor.CallCount >= 1, "Executor should be called at least once");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_EmitsHostedTelemetryStartedAndFinishedEvents()
    {
        var telemetrySink = new CapturingTelemetrySink();
        var app = await CreateAppAsync(BuildPlanJson("AgentForm", "validate"), telemetrySink: telemetrySink);
        try
        {
            using var client = CreateClient(app);

            var response = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload());
            response.EnsureSuccessStatusCode();

            _ = await response.Content.ReadAsStringAsync();

            var started = Assert.Single(
                telemetrySink.Events,
                static e => e.Kind == AgentBlazorRunEventKind.Started && e.Source == AgentBlazorTelemetrySources.AgUiHosted);
            var finished = Assert.Single(
                telemetrySink.Events,
                static e => e.Kind == AgentBlazorRunEventKind.Finished && e.Source == AgentBlazorTelemetrySources.AgUiHosted);

            Assert.Equal(AgentBlazorTelemetrySources.AgUiHosted, started.Source);
            Assert.Equal(AgentBlazorRunOutcome.Succeeded, finished.Outcome);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_EmitsStateSnapshots_ForStepAndReasoningEvents()
    {
        var app = await CreateAppWithStreamingRuntimeAsync();
        try
        {
            using var client = CreateClient(app);

            var response = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload());
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("TEXT_MESSAGE_CONTENT", body, StringComparison.Ordinal);
            Assert.Contains("TOOL_CALL_RESULT", body, StringComparison.Ordinal);
            Assert.Contains("STATE_SNAPSHOT", body, StringComparison.Ordinal);
            Assert.Contains("reasoning_start", body, StringComparison.Ordinal);
            Assert.Contains("reasoning_content", body, StringComparison.Ordinal);
            Assert.Contains("reasoning_end", body, StringComparison.Ordinal);
            Assert.Contains("step_started", body, StringComparison.Ordinal);
            Assert.Contains("tool_call_end", body, StringComparison.Ordinal);
            Assert.Contains("step_finished", body, StringComparison.Ordinal);
            Assert.Contains("shared_state_snapshot", body, StringComparison.Ordinal);
            Assert.Contains("shared_state_delta", body, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_ConnectOperation_UsesReconnectStream()
    {
        const string runId = "reconnect-run-1";
        var runtime = new ControlStreamingRuntime();
        var app = await CreateAppWithControlRuntimeAsync(runtime);
        try
        {
            using var client = CreateClient(app);

            var initialResponse = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload(runId: runId));
            initialResponse.EnsureSuccessStatusCode();
            _ = await initialResponse.Content.ReadAsStringAsync();

            var reconnectResponse = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload(runId: runId, forwardedProps: new { ag_ui_operation = "connect" }));
            reconnectResponse.EnsureSuccessStatusCode();

            var reconnectBody = await reconnectResponse.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", reconnectResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", reconnectBody, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", reconnectBody, StringComparison.Ordinal);
            Assert.Contains("TEXT_MESSAGE_CONTENT", reconnectBody, StringComparison.Ordinal);
            Assert.Contains("Reconnected stream for reconnect-run-1", reconnectBody, StringComparison.Ordinal);
            Assert.Equal(1, runtime.RunStreamCallCount);
            Assert.Equal(1, runtime.ConnectStreamCallCount);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_StopOperation_StopsActiveRun()
    {
        const string runId = "stop-run-1";
        var runtime = new ControlStreamingRuntime();
        var app = await CreateAppWithControlRuntimeAsync(runtime);
        try
        {
            using var client = CreateClient(app);

            var initialResponse = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload(runId: runId));
            initialResponse.EnsureSuccessStatusCode();
            _ = await initialResponse.Content.ReadAsStringAsync();

            var stopResponse = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload(runId: runId, forwardedProps: new { ag_ui_operation = "stop" }));
            stopResponse.EnsureSuccessStatusCode();

            var stopBody = await stopResponse.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", stopResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_STARTED", stopBody, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", stopBody, StringComparison.Ordinal);
            Assert.Contains("STATE_SNAPSHOT", stopBody, StringComparison.Ordinal);
            Assert.Contains("run_stop", stopBody, StringComparison.Ordinal);
            Assert.Contains("Run stop requested.", stopBody, StringComparison.Ordinal);
            Assert.Equal(1, runtime.StopCallCount);
            Assert.Equal(runId, runtime.LastStoppedRunId);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_ConnectOperation_ReturnsHelpfulMessage_WhenReconnectUnsupported()
    {
        var runtimeAdapter = new StreamingOnlyRuntimeAdapter();
        var app = await CreateAppWithRuntimeAdapterAsync(runtimeAdapter);
        try
        {
            using var client = CreateClient(app);

            var response = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload(runId: "connect-unsupported", forwardedProps: new { ag_ui_operation = "connect" }));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("Run reconnection is not supported by the configured runtime adapter.", body, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_StopOperation_ReturnsHelpfulMessage_WhenCancellationUnsupported()
    {
        var runtimeAdapter = new StreamingOnlyRuntimeAdapter();
        var app = await CreateAppWithRuntimeAdapterAsync(runtimeAdapter);
        try
        {
            using var client = CreateClient(app);

            var response = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload(runId: "stop-unsupported", forwardedProps: new { ag_ui_operation = "stop" }));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("RUN_STARTED", body, StringComparison.Ordinal);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);
            Assert.Contains("Active run cancellation is not supported by the configured runtime adapter.", body, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<WebApplication> CreateAppAsync(
        string planJson,
        AgentBlazorTier? tier = null,
        Action<AgentBlazorOptions>? configureOptions = null,
        IChatClient? chatClient = null,
        IAgentBlazorTelemetrySink? telemetrySink = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(chatClient ?? new FixedPlanChatClient(planJson));
        builder.Services.AddSingleton<CountingExecutor>();
        builder.Services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        if (telemetrySink is not null)
        {
            builder.Services.AddSingleton<IAgentBlazorTelemetrySink>(telemetrySink);
        }

        if (tier is not null)
        {
            builder.Services.AddAgentBlazorLicensing(tier.Value);
        }

        builder.Services.AddAgentBlazorServices(options =>
        {
            ConfigureDefaultAgent(options);
            configureOptions?.Invoke(options);
        })
            .UseLegacyRuntimeAdapter();

        builder.Services.AddAgentBlazorHosting();

        var app = builder.Build();
        app.MapAgentBlazorAgUiRun();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        return app;
    }

    private static async Task<WebApplication> CreateAppWithControlRuntimeAsync(ControlStreamingRuntime runtime)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAgentBlazorServices(ConfigureDefaultAgent);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton<IAgentRuntime>(static sp => sp.GetRequiredService<ControlStreamingRuntime>());
        builder.Services.AddAgentBlazorHosting();

        var app = builder.Build();
        app.MapAgentBlazorAgUiRun();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        return app;
    }

    private static async Task<WebApplication> CreateAppWithRuntimeAdapterAsync(IAgentRuntimeAdapter runtimeAdapter)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAgentBlazorServices(ConfigureDefaultAgent)
            .UseRuntimeAdapter(_ => runtimeAdapter);
        builder.Services.AddAgentBlazorHosting();

        var app = builder.Build();
        app.MapAgentBlazorAgUiRun();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        return app;
    }

    private static async Task<WebApplication> CreateAppWithStreamingRuntimeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAgentBlazorServices(ConfigureDefaultAgent);
        builder.Services.AddSingleton<StubStreamingRuntime>();
        builder.Services.AddSingleton<IAgentRuntime>(static sp => sp.GetRequiredService<StubStreamingRuntime>());
        builder.Services.AddAgentBlazorHosting();

        var app = builder.Build();
        app.MapAgentBlazorAgUiRun();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        return app;
    }

    private static void ConfigureDefaultAgent(AgentBlazorOptions options)
    {
        options.DefaultAgent.AllowedComponents.Clear();
        options.DefaultAgent.AllowedActions.Clear();
        options.DefaultAgent.AllowedComponents.Add("AgentForm");
        options.DefaultAgent.AllowedActions.Add("AgentForm.submit");
        options.DefaultAgent.AllowedActions.Add("AgentForm.validate");
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses
            ?? throw new InvalidOperationException("No server addresses are available.");
        var address = addresses.First();
        return new HttpClient
        {
            BaseAddress = new Uri(address)
        };
    }

    private static object CreateRunPayload(
        IEnumerable<KeyValuePair<string, string>>? context = null,
        string? runId = null,
        object? forwardedProps = null)
        => new
        {
            threadId = "thread-1",
            runId = runId ?? Guid.NewGuid().ToString("N"),
            messages = new[]
            {
                new
                {
                    id = "msg-1",
                    role = "user",
                    content = "submit the form"
                }
            },
            context = (context ?? []).Select(static kvp => new
            {
                description = kvp.Key,
                value = kvp.Value
            }).ToArray(),
            forwardedProps
        };

    private static string BuildPlanJson(
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
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

    private sealed class FixedPlanChatClient(string planJson) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, planJson)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, planJson);
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

    private sealed class StubStreamingRuntime : IAgentRuntime, IAgentRuntimeStreaming
    {
        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new AgentTurnResponse(
                AgentName: "AgentBlazor UI Agent",
                ResponseText: "Completed.",
                PlannedActions: [],
                ExecutionResults: []));
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;

            var runId = request.Context is not null &&
                        request.Context.TryGetValue("ag_ui_run_id", out var contextRunId) &&
                        !string.IsNullOrWhiteSpace(contextRunId)
                ? contextRunId
                : Guid.NewGuid().ToString("N");

            var plannedAction = new PlannedComponentAction(
                "AgentForm",
                "validate",
                "Validate form before response",
                new Dictionary<string, object?>
                {
                    ["field"] = "supplierName"
                });
            var executionResult = new ComponentActionExecutionResult(
                plannedAction.ComponentId,
                plannedAction.ActionId,
                Succeeded: true,
                Message: "Executed AgentForm.validate");

            var sequence = 0L;
            yield return CreateEvent(AgentTurnStreamEventKind.RunStarted, runId, ++sequence);
            yield return CreateEvent(
                AgentTurnStreamEventKind.StateSnapshot,
                runId,
                ++sequence,
                sharedStateSnapshot: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["component.recipe.state.title"] = "Classic Scrambled Eggs",
                    ["route.current"] = "/demo/dojo"
                });
            yield return CreateEvent(
                AgentTurnStreamEventKind.StateDelta,
                runId,
                ++sequence,
                sharedStateDelta: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["component.recipe.state.title"] = "Test"
                });
            yield return CreateEvent(AgentTurnStreamEventKind.ReasoningStart, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.ReasoningContent, runId, ++sequence, reasoningDelta: "Model is planning next action.");
            yield return CreateEvent(AgentTurnStreamEventKind.ReasoningEnd, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.StepStarted, runId, ++sequence, stepIndex: 0, plannedAction: plannedAction);
            yield return CreateEvent(AgentTurnStreamEventKind.ToolCallStart, runId, ++sequence, stepIndex: 0, plannedAction: plannedAction, toolArgs: plannedAction.Arguments);
            yield return CreateEvent(AgentTurnStreamEventKind.ToolCallArgs, runId, ++sequence, stepIndex: 0, plannedAction: plannedAction, toolArgs: plannedAction.Arguments);
            yield return CreateEvent(AgentTurnStreamEventKind.ToolCallEnd, runId, ++sequence, stepIndex: 0, plannedAction: plannedAction);
            yield return CreateEvent(AgentTurnStreamEventKind.ToolCallResult, runId, ++sequence, stepIndex: 0, executionResult: executionResult);
            yield return CreateEvent(AgentTurnStreamEventKind.StepFinished, runId, ++sequence, stepIndex: 0, stepSucceeded: true, executionResult: executionResult);
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageStart, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageContent, runId, ++sequence, textDelta: "Action completed.");
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageEnd, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.RunFinished, runId, ++sequence);

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            return Task.FromResult(true);
        }

        private static AgentTurnStreamEvent CreateEvent(
            AgentTurnStreamEventKind kind,
            string runId,
            long sequence,
            int? stepIndex = null,
            PlannedComponentAction? plannedAction = null,
            ComponentActionExecutionResult? executionResult = null,
            IReadOnlyDictionary<string, object?>? toolArgs = null,
            bool? stepSucceeded = null,
            string? textDelta = null,
            string? reasoningDelta = null,
            IReadOnlyDictionary<string, string>? sharedStateSnapshot = null,
            IReadOnlyDictionary<string, string?>? sharedStateDelta = null)
        {
            return new AgentTurnStreamEvent
            {
                Kind = kind,
                RunId = runId,
                Sequence = sequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent",
                StepIndex = stepIndex,
                PlannedAction = plannedAction,
                ExecutionResult = executionResult,
                ToolArguments = toolArgs,
                StepSucceeded = stepSucceeded,
                TextDelta = textDelta,
                ReasoningDelta = reasoningDelta,
                SharedStateSnapshot = sharedStateSnapshot,
                SharedStateDelta = sharedStateDelta
            };
        }
    }

    private sealed class ControlStreamingRuntime : IAgentRuntime, IAgentRuntimeStreaming
    {
        private readonly HashSet<string> _activeRuns = new(StringComparer.Ordinal);

        public int RunStreamCallCount { get; private set; }

        public int ConnectStreamCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public string? LastStoppedRunId { get; private set; }

        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new AgentTurnResponse(
                AgentName: "AgentBlazor UI Agent",
                ResponseText: "Completed.",
                PlannedActions: [],
                ExecutionResults: []));
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            RunStreamCallCount++;
            var runId = ResolveRunId(request);
            _activeRuns.Add(runId);

            var sequence = 0L;
            yield return CreateEvent(AgentTurnStreamEventKind.RunStarted, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageStart, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageContent, runId, ++sequence, textDelta: $"Live stream for {runId}");
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageEnd, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.RunFinished, runId, ++sequence);

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ConnectStreamCallCount++;

            var sequence = 0L;
            yield return CreateEvent(AgentTurnStreamEventKind.RunStarted, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageStart, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageContent, runId, ++sequence, textDelta: $"Reconnected stream for {runId}");
            yield return CreateEvent(AgentTurnStreamEventKind.TextMessageEnd, runId, ++sequence);
            yield return CreateEvent(AgentTurnStreamEventKind.RunFinished, runId, ++sequence);

            await Task.CompletedTask;
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            StopCallCount++;
            LastStoppedRunId = runId;
            return Task.FromResult(_activeRuns.Remove(runId));
        }

        private static string ResolveRunId(AgentTurnRequest request)
        {
            if (request.Context is not null &&
                request.Context.TryGetValue("ag_ui_run_id", out var runId) &&
                !string.IsNullOrWhiteSpace(runId))
            {
                return runId;
            }

            return Guid.NewGuid().ToString("N");
        }

        private static AgentTurnStreamEvent CreateEvent(
            AgentTurnStreamEventKind kind,
            string runId,
            long sequence,
            string? textDelta = null)
        {
            return new AgentTurnStreamEvent
            {
                Kind = kind,
                RunId = runId,
                Sequence = sequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent",
                TextDelta = textDelta
            };
        }
    }

    private sealed class StreamingOnlyRuntimeAdapter : IAgentRuntimeAdapter
    {
        public bool SupportsStreaming => true;

        public bool SupportsReconnect => false;

        public bool SupportsCancellation => false;

        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new AgentTurnResponse("AgentBlazor UI Agent", "Completed.", [], []));
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var runId = request.Context is not null &&
                        request.Context.TryGetValue("ag_ui_run_id", out var contextRunId) &&
                        !string.IsNullOrWhiteSpace(contextRunId)
                ? contextRunId
                : Guid.NewGuid().ToString("N");

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunStarted,
                RunId = runId,
                Sequence = 1,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent"
            };
            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageStart,
                RunId = runId,
                Sequence = 2,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent"
            };
            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageContent,
                RunId = runId,
                Sequence = 3,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent",
                TextDelta = "Streaming response."
            };
            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageEnd,
                RunId = runId,
                Sequence = 4,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent"
            };
            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunFinished,
                RunId = runId,
                Sequence = 5,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = "AgentBlazor UI Agent"
            };

            await Task.CompletedTask;
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }
}
