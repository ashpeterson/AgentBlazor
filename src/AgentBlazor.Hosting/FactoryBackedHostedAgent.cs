using System.Text.Json;
using AgentBlazor.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace AgentBlazor.Hosting;

internal sealed class FactoryBackedHostedAgent(
    AgentBlazorHostedAgentFactory factory,
    IAgentBlazorTelemetrySink telemetrySink,
    ILogger<FactoryBackedHostedAgent>? logger = null) : AIAgent
{
    private readonly AgentBlazorHostedAgentFactory _factory = factory;
    private readonly IAgentBlazorTelemetrySink _telemetrySink = telemetrySink;
    private readonly ILogger<FactoryBackedHostedAgent>? _logger = logger;

    public override string? Name => _factory.CreateDefaultAgent().Name;

    public override string? Description => _factory.CreateDefaultAgent().Description;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        _factory.CreateDefaultAgent().CreateSessionAsync(cancellationToken);

    protected override JsonElement SerializeSessionCore(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null) =>
        _factory.CreateDefaultAgent().SerializeSession(session, jsonSerializerOptions);

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        _factory.CreateDefaultAgent().DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken);

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agent = _factory.CreateDefaultAgent();
        var agentName = agent.Name ?? "AgentBlazor UI Agent";
        var hasContext = HasContext(options);
        var hasRegisteredComponents = HasRegisteredComponents(options);

        await TrackRunEventAsync(CreateRunEvent(
            agentName,
            hasContext,
            hasRegisteredComponents,
            AgentBlazorRunEventKind.Started));

        try
        {
            var response = await agent.RunAsync(messages, session, options, cancellationToken);

            await TrackRunEventAsync(CreateRunEvent(
                agentName,
                hasContext,
                hasRegisteredComponents,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Succeeded));

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TrackRunEventAsync(CreateRunEvent(
                agentName,
                hasContext,
                hasRegisteredComponents,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Canceled,
                "Run canceled."));
            throw;
        }
        catch (Exception ex)
        {
            await TrackRunEventAsync(CreateRunEvent(
                agentName,
                hasContext,
                hasRegisteredComponents,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Failed,
                ex.Message));
            throw;
        }
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunCoreStreamingWithTelemetry(messages, session, options, cancellationToken);

    private async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingWithTelemetry(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = _factory.CreateDefaultAgent();
        var agentName = agent.Name ?? "AgentBlazor UI Agent";
        var hasContext = HasContext(options);
        var hasRegisteredComponents = HasRegisteredComponents(options);

        await TrackRunEventAsync(CreateRunEvent(
            agentName,
            hasContext,
            hasRegisteredComponents,
            AgentBlazorRunEventKind.Started));

        var stream = agent.RunStreamingAsync(messages, session, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                AgentResponseUpdate update;
                try
                {
                    if (!await stream.MoveNextAsync())
                    {
                        break;
                    }

                    update = stream.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await TrackRunEventAsync(CreateRunEvent(
                        agentName,
                        hasContext,
                        hasRegisteredComponents,
                        AgentBlazorRunEventKind.Finished,
                        AgentBlazorRunOutcome.Canceled,
                        "Run canceled."));
                    throw;
                }
                catch (Exception ex)
                {
                    await TrackRunEventAsync(CreateRunEvent(
                        agentName,
                        hasContext,
                        hasRegisteredComponents,
                        AgentBlazorRunEventKind.Finished,
                        AgentBlazorRunOutcome.Failed,
                        ex.Message));
                    throw;
                }

                yield return update;
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }

        await TrackRunEventAsync(CreateRunEvent(
            agentName,
            hasContext,
            hasRegisteredComponents,
            AgentBlazorRunEventKind.Finished,
            AgentBlazorRunOutcome.Succeeded));
    }

    private async ValueTask TrackRunEventAsync(AgentBlazorRunTelemetryEvent telemetryEvent)
    {
        try
        {
            await _telemetrySink.TrackRunEventAsync(telemetryEvent);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Telemetry sink failed while recording hosted event {TelemetryKind} for {AgentName}.",
                telemetryEvent.Kind,
                telemetryEvent.AgentName);
        }
    }

    private static bool HasContext(AgentRunOptions? options)
    {
        var properties = options?.AdditionalProperties;
        if (properties is null)
        {
            return false;
        }

        return properties.TryGetValue("agentblazor_context", out var runtimeContextObj) &&
               runtimeContextObj is IDictionary<string, string> runtimeContext &&
               runtimeContext.Count > 0 ||
               properties.TryGetValue("ag_ui_context", out var agUiContextObj) &&
               agUiContextObj is IEnumerable<KeyValuePair<string, string>> agUiContext &&
               agUiContext.Any();
    }

    private static bool HasRegisteredComponents(AgentRunOptions? options)
    {
        var properties = options?.AdditionalProperties;
        if (properties is null)
        {
            return false;
        }

        if (!properties.TryGetValue("agentblazor_registered_components", out var componentObj))
        {
            return false;
        }

        return componentObj is IEnumerable<object> components && components.Any();
    }

    private AgentBlazorRunTelemetryEvent CreateRunEvent(
        string agentName,
        bool hasContext,
        bool hasRegisteredComponents,
        AgentBlazorRunEventKind kind,
        AgentBlazorRunOutcome? outcome = null,
        string? detail = null)
        => new()
        {
            Kind = kind,
            Source = AgentBlazorTelemetrySources.AgUiHosted,
            AgentName = agentName,
            Outcome = outcome,
            ProviderConfigured = _factory.IsProviderConfigured,
            Tier = _factory.CurrentTier,
            HasContext = hasContext,
            HasRegisteredComponents = hasRegisteredComponents,
            Detail = detail
        };
}
