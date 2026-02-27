using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentBlazor.Components;
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
        var app = await CreateAppAsync(BuildPlanJson("AgentForm", "submit"));
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
        var app = await CreateAppAsync(BuildPlanJson("AgentForm", "submit"));
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
    public async Task AgUiRun_PaidTier_StillExecutesDeterministicSubmitAction()
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
            Assert.Equal(1, executor.CallCount);
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
        });

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

    private static object CreateRunPayload(IEnumerable<KeyValuePair<string, string>>? context = null)
        => new
        {
            threadId = "thread-1",
            runId = Guid.NewGuid().ToString("N"),
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
            }).ToArray()
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
}
