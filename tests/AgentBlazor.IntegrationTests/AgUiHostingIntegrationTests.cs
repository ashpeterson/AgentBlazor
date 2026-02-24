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
    public async Task AgUiRun_MudApprovalRequiredTool_SkipsExecution_WhenApprovalMissing()
    {
        var app = await CreateAppAsync("agentblazor_agentform_submit");
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
            Assert.Equal(0, executor.CallCount);
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
        var app = await CreateAppAsync("agentblazor_agentform_validate", telemetrySink: telemetrySink);
        try
        {
            using var client = CreateClient(app);

            var response = await client.PostAsJsonAsync("/agentblazor/agui/run", CreateRunPayload());
            response.EnsureSuccessStatusCode();

            _ = await response.Content.ReadAsStringAsync();

            var started = Assert.Single(telemetrySink.Events, static e => e.Kind == AgentBlazorRunEventKind.Started);
            var finished = Assert.Single(telemetrySink.Events, static e => e.Kind == AgentBlazorRunEventKind.Finished);

            Assert.Equal(AgentBlazorTelemetrySources.AgUiHosted, started.Source);
            Assert.Equal(AgentBlazorRunOutcome.Succeeded, finished.Outcome);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact(Skip = "Deterministic AG-UI hosted flow does not yet forward approval context for submit actions.")]
    public async Task AgUiRun_MudApprovalRequiredTool_Executes_WhenApprovalProvided()
    {
        var app = await CreateAppAsync("agentblazor_agentform_submit");
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
    public async Task AgUiRun_MudPremiumAction_IsBlocked_WhenTierIsPaid()
    {
        var app = await CreateAppAsync(
            "agentblazor_agentform_submit",
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
            Assert.Equal(0, executor.CallCount);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AgUiRun_IncludesRegisteredWrapperSnapshot_InHostedInstructions()
    {
        var capturingClient = new CapturingToolThenTextChatClient("agentblazor_agentform_validate");
        var app = await CreateAppAsync("agentblazor_agentform_validate", chatClient: capturingClient);
        try
        {
            var registry = app.Services.GetRequiredService<IAgentComponentRegistry>();
            registry.Register(new StubRegisteredComponent(
                "supplier-grid",
                "DataGrid",
                new Dictionary<string, object?>
                {
                    ["riskScoreThreshold"] = 7,
                    ["currentPage"] = 2
                }));

            using var client = CreateClient(app);

            var response = await client.PostAsJsonAsync(
                "/agentblazor/agui/run",
                CreateRunPayload());
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("RUN_FINISHED", body, StringComparison.Ordinal);

            var instructions = capturingClient.LastInstructions ?? string.Empty;
            Assert.Contains("supplier-grid", instructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("riskScoreThreshold", instructions, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("currentPage", instructions, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<WebApplication> CreateAppAsync(
        string functionName,
        AgentBlazorTier? tier = null,
        Action<AgentBlazorOptions>? configureOptions = null,
        IChatClient? chatClient = null,
        IAgentBlazorTelemetrySink? telemetrySink = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(chatClient ?? new ToolThenTextChatClient(functionName));
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

        builder.Services.AddAgentBlazorServices(configureOptions);
        builder.Services.AddAgentBlazorHosting();

        var app = builder.Build();
        app.MapAgentBlazorAgUiRun();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        return app;
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

    private static string BuildPlanJson(string toolName)
    {
        if (!TryResolveToolName(toolName, out var componentId, out var actionId))
        {
            return """{"steps":[]}""";
        }

        var payload = new
        {
            steps = new[]
            {
                new
                {
                    componentId,
                    actionId,
                    arguments = new Dictionary<string, object?>()
                }
            }
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
            "agentblazor_agentform_submit" => Resolve("AgentForm", "submit", out componentId, out actionId),
            "agentblazor_agentform_validate" => Resolve("AgentForm", "validate", out componentId, out actionId),
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

    private sealed class ToolThenTextChatClient(string functionName) : IChatClient
    {
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
                BuildPlanJson(functionName))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, BuildPlanJson(functionName));
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

    private sealed class CapturingToolThenTextChatClient(string functionName) : IChatClient
    {
        public string? LastInstructions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastInstructions = options?.Instructions;
            if (string.IsNullOrWhiteSpace(LastInstructions))
            {
                LastInstructions = ExtractSystemPrompt(messages);
            }
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                BuildPlanJson(functionName))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastInstructions = options?.Instructions;
            if (string.IsNullOrWhiteSpace(LastInstructions))
            {
                LastInstructions = ExtractSystemPrompt(messages);
            }
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, BuildPlanJson(functionName));
            await Task.CompletedTask;
        }

        private static string ExtractSystemPrompt(IEnumerable<ChatMessage> messages)
        {
            var systemMessage = messages.FirstOrDefault(static message => message.Role == ChatRole.System);
            if (systemMessage is null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(systemMessage.Text))
            {
                return systemMessage.Text;
            }

            return string.Concat(systemMessage.Contents
                .OfType<TextContent>()
                .Select(static content => content.Text));
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
}
