using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentBlazor.Agents;
using AgentBlazor.Execution;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Hosting;

internal sealed class DeterministicAgUiHostedAgent(
    IAgentRuntimeAdapter runtimeAdapter,
    IAgentExecutionScopeAccessor executionScopeAccessor,
    IServiceProvider serviceProvider,
    IAgentRegistry agentRegistry,
    IOptions<AgentBlazorOptions> options,
    IAgentBlazorTelemetrySink telemetrySink,
    IAgentSharedStateStore sharedStateStore,
    AgentComponentRegistryHub? registryHub = null,
    IAgentBlazorEntitlementService? entitlementService = null) : AIAgent
{
    private const string SessionIdContextKey = AgentRuntimeContextKeys.SessionId;
    private const string RunIdContextKey = AgentRuntimeContextKeys.RunId;
    private const string UserIdContextKey = AgentRuntimeContextKeys.UserId;
    private const string AgentNameContextKey = AgentRuntimeContextKeys.AgentName;

    private readonly IAgentRuntimeAdapter _runtimeAdapter = runtimeAdapter;
    private readonly IAgentExecutionScopeAccessor _executionScopeAccessor = executionScopeAccessor;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IAgentRegistry _agentRegistry = agentRegistry;
    private readonly IOptions<AgentBlazorOptions> _options = options;
    private readonly IAgentBlazorTelemetrySink _telemetrySink = telemetrySink;
    private readonly IAgentSharedStateStore _sharedStateStore = sharedStateStore;
    private readonly AgentComponentRegistryHub? _registryHub = registryHub;
    private readonly IAgentBlazorEntitlementService? _entitlementService = entitlementService;
    public override string? Name => ResolveAgentRegistration(null).Name;

    public override string? Description => ResolveAgentRegistration(null).Description;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return ValueTask.FromResult<AgentSession>(new HostedAgentSession());
    }

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        _ = session;
        _ = cancellationToken;
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new Dictionary<string, object?>(), jsonSerializerOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        _ = serializedState;
        _ = jsonSerializerOptions;
        _ = cancellationToken;
        return ValueTask.FromResult<AgentSession>(new HostedAgentSession());
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = session;

        var invocation = BuildInvocation(messages, options);
        await TrackRunEventAsync(CreateRunEvent(
            invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
            invocation.HasContext,
            AgentBlazorRunEventKind.Started));

        if (invocation.Operation is HostedRunOperation.Stop)
        {
            if (!_runtimeAdapter.SupportsCancellation)
            {
                var unsupportedStopResponse = new AgentTurnResponse(
                    invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                    "Active run cancellation is not supported by the configured runtime adapter.",
                    PlannedActions: [],
                    ExecutionResults: []);

                await TrackRunEventAsync(CreateRunEvent(
                    unsupportedStopResponse.AgentName,
                    invocation.HasContext,
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.Succeeded));
                return ToAgentResponse(unsupportedStopResponse, invocation);
            }

            var stopped = await _runtimeAdapter.StopRunAsync(invocation.RunId, cancellationToken);
            var stopResponse = new AgentTurnResponse(
                invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                stopped
                    ? "Run stop requested."
                    : "No active run was found for stop request.",
                PlannedActions: [],
                ExecutionResults: []);

            await TrackRunEventAsync(CreateRunEvent(
                stopResponse.AgentName,
                invocation.HasContext,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Succeeded));
            return ToAgentResponse(stopResponse, invocation);
        }

        AgentTurnResponse response;
        try
        {
            using var serviceScope = _serviceProvider.CreateScope();
            using var runtimeExecutionScope = _executionScopeAccessor.Push(serviceScope.ServiceProvider);
            response = await _runtimeAdapter.RunTurnAsync(invocation.Request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TrackRunEventAsync(CreateRunEvent(
                invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                invocation.HasContext,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Canceled,
                "Run canceled."));
            throw;
        }
        catch (Exception ex)
        {
            await TrackRunEventAsync(CreateRunEvent(
                invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                invocation.HasContext,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Failed,
                ex.Message));
            throw;
        }

        await TrackRunEventAsync(CreateRunEvent(
            response.AgentName,
            invocation.HasContext,
            AgentBlazorRunEventKind.Finished,
            AgentBlazorRunOutcome.Succeeded));

        return ToAgentResponse(response, invocation);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        _ = session;

        var invocation = BuildInvocation(messages, options);
        await TrackRunEventAsync(CreateRunEvent(
            invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
            invocation.HasContext,
            AgentBlazorRunEventKind.Started));

        if (invocation.Operation is HostedRunOperation.Stop)
        {
            if (!_runtimeAdapter.SupportsCancellation)
            {
                var unsupportedTimestamp = DateTimeOffset.UtcNow;
                yield return CreateTextUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:assistant:stop:unsupported",
                    "Active run cancellation is not supported by the configured runtime adapter.",
                    unsupportedTimestamp,
                    invocation.Request.AgentName);

                await TrackRunEventAsync(CreateRunEvent(
                    invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                    invocation.HasContext,
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.Succeeded));
                yield break;
            }

            var stopped = await _runtimeAdapter.StopRunAsync(invocation.RunId, cancellationToken);
            var timestamp = DateTimeOffset.UtcNow;
            var stopPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["kind"] = "run_stop",
                ["stopped"] = stopped
            };

            yield return CreateStateSnapshotUpdate(
                invocation.RunId,
                $"{invocation.RunId}:state:stop",
                stopPayload,
                timestamp);
            yield return CreateTextUpdate(
                invocation.RunId,
                $"{invocation.RunId}:assistant:stop",
                stopped
                    ? "Run stop requested."
                    : "No active run was found for stop request.",
                timestamp,
                invocation.Request.AgentName);

            await TrackRunEventAsync(CreateRunEvent(
                invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                invocation.HasContext,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Succeeded));
            yield break;
        }

        if (invocation.Operation is HostedRunOperation.Connect && !_runtimeAdapter.SupportsReconnect)
        {
            yield return CreateTextUpdate(
                invocation.RunId,
                $"{invocation.RunId}:assistant:connect:unsupported",
                "Run reconnection is not supported by the configured runtime adapter.",
                DateTimeOffset.UtcNow,
                invocation.Request.AgentName);

            await TrackRunEventAsync(CreateRunEvent(
                invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                invocation.HasContext,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Succeeded));
            yield break;
        }

        if (!_runtimeAdapter.SupportsStreaming)
        {
            AgentTurnResponse nonStreamingResponse;
            try
            {
                using var fallbackScope = _serviceProvider.CreateScope();
                using var runtimeExecutionScope = _executionScopeAccessor.Push(fallbackScope.ServiceProvider);
                nonStreamingResponse = await _runtimeAdapter.RunTurnAsync(invocation.Request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TrackRunEventAsync(CreateRunEvent(
                    invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                    invocation.HasContext,
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.Canceled,
                    "Run canceled."));
                throw;
            }
            catch (Exception ex)
            {
                await TrackRunEventAsync(CreateRunEvent(
                    invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                    invocation.HasContext,
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.Failed,
                    ex.Message));
                throw;
            }

            foreach (var update in CreateNonStreamingUpdates(invocation.RunId, nonStreamingResponse))
            {
                yield return update;
            }

            await TrackRunEventAsync(CreateRunEvent(
                nonStreamingResponse.AgentName,
                invocation.HasContext,
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Succeeded));
            yield break;
        }

        var mappingState = new MappingState(invocation.RunId);
        using var serviceScope = _serviceProvider.CreateScope();
        using var streamExecutionScope = _executionScopeAccessor.Push(serviceScope.ServiceProvider);
        var stream = (invocation.Operation is HostedRunOperation.Connect
                ? _runtimeAdapter.ConnectRunStreamAsync(invocation.RunId, cancellationToken)
                : _runtimeAdapter.RunTurnStreamingAsync(invocation.Request, cancellationToken))
            .GetAsyncEnumerator(cancellationToken);
        var switchedToReconnectStream = invocation.Operation is HostedRunOperation.Connect;
        try
        {
            while (true)
            {
                AgentTurnStreamEvent streamEvent;
                try
                {
                    if (!await stream.MoveNextAsync())
                    {
                        break;
                    }

                    streamEvent = stream.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await TrackRunEventAsync(CreateRunEvent(
                        invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                        invocation.HasContext,
                        AgentBlazorRunEventKind.Finished,
                        AgentBlazorRunOutcome.Canceled,
                        "Run canceled."));
                    throw;
                }
                catch (Exception ex)
                {
                    if (!switchedToReconnectStream &&
                        _runtimeAdapter.SupportsReconnect &&
                        invocation.Operation is HostedRunOperation.Run &&
                        IsRunAlreadyActiveException(ex))
                    {
                        await stream.DisposeAsync();
                        stream = _runtimeAdapter
                            .ConnectRunStreamAsync(invocation.RunId, cancellationToken)
                            .GetAsyncEnumerator(cancellationToken);
                        switchedToReconnectStream = true;
                        continue;
                    }

                    await TrackRunEventAsync(CreateRunEvent(
                        invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
                        invocation.HasContext,
                        AgentBlazorRunEventKind.Finished,
                        AgentBlazorRunOutcome.Failed,
                        ex.Message));
                    throw;
                }

                foreach (var update in MapStreamEvent(invocation, mappingState, streamEvent))
                {
                    TrackMessageRunAssociation(invocation, mappingState, update);
                    yield return update;
                }
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }

        await TrackRunEventAsync(CreateRunEvent(
            mappingState.LastAgentName ?? invocation.Request.AgentName ?? ResolveAgentRegistration(null).Name,
            invocation.HasContext,
            AgentBlazorRunEventKind.Finished,
            AgentBlazorRunOutcome.Succeeded));
    }

    private void TrackMessageRunAssociation(
        TurnInvocation invocation,
        MappingState mappingState,
        AgentResponseUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.MessageId))
        {
            return;
        }

        var agentName = mappingState.LastAgentName
            ?? invocation.Request.AgentName
            ?? ResolveAgentRegistration(null).Name;
        var sessionId = invocation.Request.GetEffectiveSessionId();

        _sharedStateStore.AssociateMessageWithRun(
            agentName,
            sessionId,
            update.MessageId,
            invocation.RunId);
    }

    private AgentRegistration ResolveAgentRegistration(string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName) &&
            _agentRegistry.TryGet(requestedName, out var requested))
        {
            return requested;
        }

        return _agentRegistry.GetAll()
                   .OrderBy(static agent => agent.Name, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault()
               ?? new AgentRegistration
               {
                   Name = "AgentBlazor hosted agent",
                   Description = "Deterministic AgentBlazor AG-UI hosted agent."
               };
    }

    private TurnInvocation BuildInvocation(IEnumerable<ChatMessage> messages, AgentRunOptions? runOptions)
    {
        var properties = ResolveAdditionalProperties(runOptions);
        var threadId = ResolveStringProperty(properties, "ag_ui_thread_id")
            ?? Guid.NewGuid().ToString("N");
        var runId = ResolveStringProperty(properties, "ag_ui_run_id")
            ?? Guid.NewGuid().ToString("N");
        var context = BuildContext(properties, threadId, runId);
        var userMessage = ResolveUserMessage(messages);
        var requestedAgentName = ResolveRequestedAgentName(context, properties);
        var operation = ResolveRunOperation(properties);
        var userId = context.TryGetValue(UserIdContextKey, out var userIdValue) &&
                     !string.IsNullOrWhiteSpace(userIdValue)
            ? userIdValue
            : null;

        var request = new AgentTurnRequest(
            UserMessage: userMessage,
            AgentName: requestedAgentName,
            SessionId: threadId,
            UserId: userId,
            Context: context.Count == 0 ? null : context);

        return new TurnInvocation(request, runId, context.Count > 0, operation);
    }

    private static AdditionalPropertiesDictionary? ResolveAdditionalProperties(AgentRunOptions? options)
    {
        AdditionalPropertiesDictionary? combined = null;

        if (options?.AdditionalProperties is { Count: > 0 } optionProperties)
        {
            combined = new AdditionalPropertiesDictionary();
            foreach (var kvp in optionProperties)
            {
                combined[kvp.Key] = kvp.Value;
            }
        }

        if (options is ChatClientAgentRunOptions chatOptions &&
            chatOptions.ChatOptions?.AdditionalProperties is { Count: > 0 } chatProperties)
        {
            combined ??= new AdditionalPropertiesDictionary();
            foreach (var kvp in chatProperties)
            {
                combined[kvp.Key] = kvp.Value;
            }
        }

        return combined;
    }

    private static Dictionary<string, string> BuildContext(
        AdditionalPropertiesDictionary? properties,
        string threadId,
        string runId)
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (properties is not null &&
            properties.TryGetValue("ag_ui_context", out var agUiContext))
        {
            MergeAgUiContext(context, agUiContext);
        }

        context[SessionIdContextKey] = threadId;
        context[RunIdContextKey] = runId;
        context["ag_ui_thread_id"] = threadId;
        context["ag_ui_run_id"] = runId;
        return context;
    }

    private static void MergeAgUiContext(
        IDictionary<string, string> context,
        object? agUiContext)
    {
        switch (agUiContext)
        {
            case IEnumerable<KeyValuePair<string, string>> typedPairs:
                foreach (var pair in typedPairs)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        context[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
                return;
            case JsonElement { ValueKind: JsonValueKind.Array } arrayElement:
                foreach (var item in arrayElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("description", out var keyProperty) ||
                        keyProperty.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var key = keyProperty.GetString();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var value = item.TryGetProperty("value", out var valueProperty)
                        ? valueProperty.ValueKind == JsonValueKind.String
                            ? valueProperty.GetString() ?? string.Empty
                            : valueProperty.GetRawText()
                        : string.Empty;

                    context[key] = value;
                }
                return;
            default:
                return;
        }
    }

    private static string ResolveUserMessage(IEnumerable<ChatMessage> messages)
    {
        ChatMessage? candidate = null;
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                candidate = message;
            }
        }

        candidate ??= messages.LastOrDefault();
        var text = candidate is null ? null : ExtractText(candidate);
        return string.IsNullOrWhiteSpace(text) ? "continue" : text;
    }

    private static string ExtractText(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }

        var builder = new StringBuilder();
        foreach (var content in message.Contents.OfType<TextContent>())
        {
            if (!string.IsNullOrWhiteSpace(content.Text))
            {
                builder.Append(content.Text);
            }
        }

        return builder.ToString();
    }

    private static string? ResolveRequestedAgentName(
        IReadOnlyDictionary<string, string> context,
        AdditionalPropertiesDictionary? properties)
    {
        if (context.TryGetValue(AgentNameContextKey, out var fromContext) &&
            !string.IsNullOrWhiteSpace(fromContext))
        {
            return fromContext;
        }

        if (TryResolveForwardedProperty(properties, out var forwardedJson) &&
            (TryGetString(forwardedJson, "agentName", out var agentName) ||
            TryGetString(forwardedJson, "agent", out agentName) ||
            TryGetString(forwardedJson, "agent_name", out agentName)))
        {
            return agentName;
        }

        return null;
    }

    private static HostedRunOperation ResolveRunOperation(AdditionalPropertiesDictionary? properties)
    {
        var operation = ResolveStringProperty(properties, "ag_ui_operation");

        if (string.IsNullOrWhiteSpace(operation) &&
            TryResolveForwardedProperty(properties, out var forwardedJson))
        {
            _ = TryGetString(forwardedJson, "ag_ui_operation", out operation) ||
                TryGetString(forwardedJson, "agUiOperation", out operation) ||
                TryGetString(forwardedJson, "operation", out operation) ||
                TryGetString(forwardedJson, "mode", out operation);
        }

        return operation?.Trim().ToLowerInvariant() switch
        {
            "connect" or "reconnect" or "resume" => HostedRunOperation.Connect,
            "stop" or "cancel" or "abort" => HostedRunOperation.Stop,
            _ => HostedRunOperation.Run
        };
    }

    private static bool TryResolveForwardedProperty(
        AdditionalPropertiesDictionary? properties,
        out JsonElement forwardedJson)
    {
        forwardedJson = default;
        if (properties is null ||
            !properties.TryGetValue("ag_ui_forwarded_properties", out var forwardedProps) ||
            forwardedProps is not JsonElement { ValueKind: JsonValueKind.Object } resolved)
        {
            return false;
        }

        forwardedJson = resolved;
        return true;
    }

    private static bool TryGetString(JsonElement jsonElement, string propertyName, out string? value)
    {
        value = null;
        if (!jsonElement.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? ResolveStringProperty(AdditionalPropertiesDictionary? properties, string key)
    {
        if (properties is null ||
            !properties.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null
        };
    }

    private static bool IsRunAlreadyActiveException(Exception exception)
    {
        return exception is InvalidOperationException invalidOperation &&
               invalidOperation.Message.Contains("already active", StringComparison.OrdinalIgnoreCase);
    }

    private static AgentResponseUpdate CreateTextUpdate(
        string runId,
        string messageId,
        string text,
        DateTimeOffset timestamp,
        string? agentName)
    {
        return new AgentResponseUpdate(ChatRole.Assistant, text)
        {
            ResponseId = runId,
            MessageId = messageId,
            CreatedAt = timestamp,
            AgentId = agentName
        };
    }

    private static AgentResponseUpdate CreateToolCallUpdate(
        string runId,
        PendingToolCall pending,
        DateTimeOffset timestamp,
        string? agentName)
    {
        var invocation = new FunctionCallContent(
            pending.CallId,
            pending.Name,
            pending.Arguments.Count == 0 ? null : pending.Arguments);

        return new AgentResponseUpdate(ChatRole.Assistant, [invocation])
        {
            ResponseId = runId,
            MessageId = pending.CallId,
            CreatedAt = timestamp,
            AgentId = agentName
        };
    }

    private static AgentResponseUpdate CreateToolResultUpdate(
        string runId,
        string callId,
        ComponentActionExecutionResult executionResult,
        DateTimeOffset timestamp)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["componentId"] = executionResult.ComponentId,
            ["actionId"] = executionResult.ActionId,
            ["outcome"] = executionResult.Outcome.ToString(),
            ["succeeded"] = executionResult.Succeeded,
            ["message"] = executionResult.Message
        };

        return new AgentResponseUpdate(ChatRole.Tool, [new FunctionResultContent(callId, payload)])
        {
            ResponseId = runId,
            MessageId = $"{callId}:result",
            CreatedAt = timestamp
        };
    }

    private static AgentResponseUpdate CreateStateSnapshotUpdate(
        string runId,
        string messageId,
        Dictionary<string, object?> payload,
        DateTimeOffset timestamp)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return new AgentResponseUpdate(ChatRole.Assistant, [new DataContent(jsonBytes, "application/json")])
        {
            ResponseId = runId,
            MessageId = messageId,
            CreatedAt = timestamp
        };
    }

    private static Dictionary<string, object?> CreatePlannedActionPayload(PlannedComponentAction plannedAction)
    {
        var arguments = plannedAction.Arguments;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["componentId"] = plannedAction.ComponentId,
            ["actionId"] = plannedAction.ActionId,
            ["arguments"] = arguments is null || arguments.Count == 0
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IEnumerable<AgentResponseUpdate> CreateNonStreamingUpdates(
        string runId,
        AgentTurnResponse response)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var sequence = 0;
        var mappingState = new MappingState(runId);
        var emittedStepStates = false;

        if (response.ExecutionPlan?.Steps.Count > 0)
        {
            foreach (var step in response.ExecutionPlan.Steps.OrderBy(static step => step.Order))
            {
                emittedStepStates = true;

                var stepPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "step_started",
                    ["stepIndex"] = step.Order
                };
                var plannedAction = ToPlannedAction(step);
                if (plannedAction is not null)
                {
                    stepPayload["plannedAction"] = CreatePlannedActionPayload(plannedAction);
                }

                yield return CreateStateSnapshotUpdate(
                    runId,
                    $"{runId}:state:{++sequence}",
                    stepPayload,
                    timestamp);

                if (plannedAction is not null)
                {
                    var pending = mappingState.StartToolCall(step.Order, plannedAction, step.Arguments);
                    if (pending is not null)
                    {
                        yield return CreateToolCallUpdate(runId, pending, timestamp, response.AgentName);
                    }

                    yield return CreateStateSnapshotUpdate(
                        runId,
                        $"{runId}:state:{++sequence}",
                        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["kind"] = "tool_call_end",
                            ["stepIndex"] = step.Order,
                            ["plannedAction"] = CreatePlannedActionPayload(plannedAction)
                        },
                        timestamp);

                    var executionResult = ToExecutionResult(step);
                    if (pending is not null && executionResult is not null)
                    {
                        yield return CreateToolResultUpdate(runId, pending.CallId, executionResult, timestamp);
                    }
                }

                yield return CreateStateSnapshotUpdate(
                    runId,
                    $"{runId}:state:{++sequence}",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "step_finished",
                        ["stepIndex"] = step.Order,
                        ["succeeded"] = step.Status is AgentExecutionStepStatus.Completed,
                        ["executionResult"] = ToExecutionResult(step) is { } result
                            ? CreateExecutionResultPayload(result)
                            : null
                    },
                    timestamp);
            }
        }
        else if (response.LegacyPlannedActions.Count > 0)
        {
            foreach (var pair in EnumerateLegacyStepPairs(response))
            {
                emittedStepStates = true;
                var plannedAction = pair.PlannedAction;

                yield return CreateStateSnapshotUpdate(
                    runId,
                    $"{runId}:state:{++sequence}",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "step_started",
                        ["stepIndex"] = pair.StepIndex,
                        ["plannedAction"] = CreatePlannedActionPayload(plannedAction)
                    },
                    timestamp);

                var pending = mappingState.StartToolCall(pair.StepIndex, plannedAction, plannedAction.Arguments);
                if (pending is not null)
                {
                    yield return CreateToolCallUpdate(runId, pending, timestamp, response.AgentName);
                }

                yield return CreateStateSnapshotUpdate(
                    runId,
                    $"{runId}:state:{++sequence}",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "tool_call_end",
                        ["stepIndex"] = pair.StepIndex,
                        ["plannedAction"] = CreatePlannedActionPayload(plannedAction)
                    },
                    timestamp);

                if (pending is not null && pair.ExecutionResult is not null)
                {
                    yield return CreateToolResultUpdate(runId, pending.CallId, pair.ExecutionResult, timestamp);
                }

                yield return CreateStateSnapshotUpdate(
                    runId,
                    $"{runId}:state:{++sequence}",
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["kind"] = "step_finished",
                        ["stepIndex"] = pair.StepIndex,
                        ["succeeded"] = pair.ExecutionResult?.Succeeded ?? false,
                        ["executionResult"] = pair.ExecutionResult is not null
                            ? CreateExecutionResultPayload(pair.ExecutionResult)
                            : null
                    },
                    timestamp);
            }
        }

        if (response.PendingApprovals.Count > 0)
        {
            emittedStepStates = true;
            yield return CreateStateSnapshotUpdate(
                runId,
                $"{runId}:state:{++sequence}",
                CreateApprovalRequiredPayload(response.PendingApprovals),
                timestamp);
        }
        else if (response.RequiresApproval)
        {
            emittedStepStates = true;
            yield return CreateStateSnapshotUpdate(
                runId,
                $"{runId}:state:{++sequence}",
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "approval_required",
                    ["pendingApprovals"] = Array.Empty<object>()
                },
                timestamp);
        }

        if (!emittedStepStates && response.ExecutionPlan?.Context is { } context)
        {
            yield return CreateStateSnapshotUpdate(
                runId,
                $"{runId}:state:{++sequence}",
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "shared_state_snapshot",
                    ["state"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["runId"] = context.RunId,
                        ["sessionId"] = context.SessionId,
                        ["route"] = context.Route,
                        ["freshness"] = context.Freshness.ToString()
                    }
                },
                timestamp);
        }

        yield return CreateTextUpdate(
            runId,
            $"{runId}:assistant:1",
            response.ResponseText,
            timestamp,
            response.AgentName);
    }

    private static Dictionary<string, object?> CreateApprovalRequiredPayload(
        IReadOnlyList<PendingApproval> pendingApprovals)
    {
        var approvals = pendingApprovals
            .Select(static pending => (object?)CreatePendingApprovalPayload(pending))
            .ToArray();

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "approval_required",
            ["pendingApprovals"] = approvals
        };
    }

    private static Dictionary<string, object?> CreatePendingApprovalPayload(PendingApproval pending)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["componentId"] = pending.ComponentId,
            ["actionId"] = pending.ActionId,
            ["description"] = pending.Description,
            ["parameters"] = pending.Parameters,
            ["policyDecision"] = pending.PolicyDecision is null
                ? null
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["allowed"] = pending.PolicyDecision.Allowed,
                    ["riskClass"] = pending.PolicyDecision.RiskClass.ToString(),
                    ["approvalMode"] = pending.PolicyDecision.ApprovalMode.ToString(),
                    ["reason"] = pending.PolicyDecision.Reason
                }
        };

    private static PlannedComponentAction? ToPlannedAction(AgentExecutionStep step)
    {
        if (string.IsNullOrWhiteSpace(step.TargetId) || string.IsNullOrWhiteSpace(step.ActionId))
        {
            return null;
        }

        return new PlannedComponentAction(
            step.TargetId,
            step.ActionId,
            $"{step.TargetId}.{step.ActionId}",
            step.Arguments);
    }

    private static ComponentActionExecutionResult? ToExecutionResult(AgentExecutionStep step)
    {
        if (string.IsNullOrWhiteSpace(step.TargetId) || string.IsNullOrWhiteSpace(step.ActionId))
        {
            return null;
        }

        var outcome = step.Status switch
        {
            AgentExecutionStepStatus.Completed => ActionOutcome.Applied,
            AgentExecutionStepStatus.ApprovalRequired => ActionOutcome.Failed,
            AgentExecutionStepStatus.NeedsClarification => ActionOutcome.Failed,
            AgentExecutionStepStatus.Blocked => ActionOutcome.Failed,
            AgentExecutionStepStatus.Failed => ActionOutcome.Failed,
            _ => ActionOutcome.Failed
        };

        return new ComponentActionExecutionResult(
            step.TargetId,
            step.ActionId,
            outcome,
            step.Message ?? $"{step.TargetId}.{step.ActionId} {step.Status}.");
    }

    private static IEnumerable<LegacyStepPair> EnumerateLegacyStepPairs(AgentTurnResponse response)
    {
        for (var index = 0; index < response.LegacyPlannedActions.Count; index++)
        {
            var plannedAction = response.LegacyPlannedActions[index];
            var executionResult = response.LegacyExecutionResults.FirstOrDefault(result =>
                string.Equals(result.ComponentId, plannedAction.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(result.ActionId, plannedAction.ActionId, StringComparison.OrdinalIgnoreCase));

            yield return new LegacyStepPair(index, plannedAction, executionResult);
        }
    }

    private static Dictionary<string, object?> CreateExecutionResultPayload(ComponentActionExecutionResult executionResult)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["componentId"] = executionResult.ComponentId,
            ["actionId"] = executionResult.ActionId,
            ["outcome"] = executionResult.Outcome.ToString(),
            ["succeeded"] = executionResult.Succeeded,
            ["message"] = executionResult.Message
        };
    }

    private static Dictionary<string, object?> ToObjectPayload(
        IReadOnlyDictionary<string, string> values)
    {
        return values.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> ToNullableObjectPayload(
        IReadOnlyDictionary<string, string?> values)
    {
        return values.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<AgentResponseUpdate> MapStreamEvent(
        TurnInvocation invocation,
        MappingState mappingState,
        AgentTurnStreamEvent streamEvent)
    {
        if (!string.IsNullOrWhiteSpace(streamEvent.AgentName))
        {
            mappingState.LastAgentName = streamEvent.AgentName;
        }

        switch (streamEvent.Kind)
        {
            case AgentTurnStreamEventKind.StepStarted:
            {
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "step_started",
                    ["stepIndex"] = streamEvent.StepIndex
                };
                if (streamEvent.PlannedAction is not null)
                {
                    payload["plannedAction"] = CreatePlannedActionPayload(streamEvent.PlannedAction);
                }

                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.StateSnapshot:
            {
                if (streamEvent.SharedStateSnapshot is null)
                {
                    yield break;
                }

                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "shared_state_snapshot",
                    ["state"] = ToObjectPayload(streamEvent.SharedStateSnapshot)
                };

                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.StateDelta:
            {
                if (streamEvent.SharedStateDelta is null || streamEvent.SharedStateDelta.Count == 0)
                {
                    yield break;
                }

                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "shared_state_delta",
                    ["delta"] = ToNullableObjectPayload(streamEvent.SharedStateDelta)
                };

                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.TextMessageStart:
                mappingState.ActiveTextMessageId = $"{invocation.RunId}:assistant:{++mappingState.TextMessageCount}";
                yield break;

            case AgentTurnStreamEventKind.TextMessageContent:
            {
                var messageId = mappingState.ActiveTextMessageId
                    ?? $"{invocation.RunId}:assistant:{++mappingState.TextMessageCount}";
                mappingState.ActiveTextMessageId = messageId;
                if (!string.IsNullOrWhiteSpace(streamEvent.TextDelta))
                {
                    yield return CreateTextUpdate(
                        invocation.RunId,
                        messageId,
                        streamEvent.TextDelta,
                        streamEvent.Timestamp,
                        streamEvent.AgentName);
                }

                yield break;
            }

            case AgentTurnStreamEventKind.TextMessageEnd:
                mappingState.ActiveTextMessageId = null;
                yield break;

            case AgentTurnStreamEventKind.ToolCallStart:
            {
                var pending = mappingState.StartToolCall(
                    streamEvent.StepIndex,
                    streamEvent.PlannedAction,
                    streamEvent.ToolArguments);
                if (pending is not null)
                {
                    yield return CreateToolCallUpdate(
                        invocation.RunId,
                        pending,
                        streamEvent.Timestamp,
                        streamEvent.AgentName);
                }

                yield break;
            }

            case AgentTurnStreamEventKind.ToolCallArgs:
                mappingState.UpdateToolCallArguments(streamEvent.StepIndex, streamEvent.ToolArguments);
                yield break;

            case AgentTurnStreamEventKind.ToolCallEnd:
            {
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "tool_call_end",
                    ["stepIndex"] = streamEvent.StepIndex
                };
                if (streamEvent.PlannedAction is not null)
                {
                    payload["plannedAction"] = CreatePlannedActionPayload(streamEvent.PlannedAction);
                }

                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.ToolCallResult:
            {
                var pending = mappingState.ResolveToolCall(streamEvent.StepIndex, streamEvent.ExecutionResult);
                if (pending is not null && streamEvent.ExecutionResult is not null)
                {
                    yield return CreateToolResultUpdate(
                        invocation.RunId,
                        pending.CallId,
                        streamEvent.ExecutionResult,
                        streamEvent.Timestamp);
                }

                yield break;
            }

            case AgentTurnStreamEventKind.StepFinished:
            {
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "step_finished",
                    ["stepIndex"] = streamEvent.StepIndex,
                    ["succeeded"] = streamEvent.StepSucceeded
                };
                if (streamEvent.ExecutionResult is not null)
                {
                    payload["executionResult"] = CreateExecutionResultPayload(streamEvent.ExecutionResult);
                }

                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.ClarificationRequired:
            {
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "clarification_required",
                    ["question"] = streamEvent.ClarificationQuestion ?? string.Empty
                };
                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.ApprovalRequired:
            {
                var pendingApprovals = streamEvent.PendingApprovals?
                    .Select(static pending => (object?)CreatePendingApprovalPayload(pending))
                    .ToArray() ?? [];
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "approval_required",
                    ["pendingApprovals"] = pendingApprovals
                };
                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.ReasoningStart:
            {
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "reasoning_start"
                };
                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.ReasoningContent:
            {
                if (string.IsNullOrWhiteSpace(streamEvent.ReasoningDelta))
                {
                    yield break;
                }

                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "reasoning_content",
                    ["content"] = streamEvent.ReasoningDelta
                };
                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.ReasoningEnd:
            {
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "reasoning_end"
                };
                yield return CreateStateSnapshotUpdate(
                    invocation.RunId,
                    $"{invocation.RunId}:state:{streamEvent.Sequence}",
                    payload,
                    streamEvent.Timestamp);
                yield break;
            }

            case AgentTurnStreamEventKind.RunError:
                throw new InvalidOperationException(streamEvent.ErrorMessage ?? "Agent run failed.");

            default:
                yield break;
        }
    }

    private static AgentResponse ToAgentResponse(AgentTurnResponse response, TurnInvocation invocation)
    {
        var message = new ChatMessage(ChatRole.Assistant, response.ResponseText)
        {
            MessageId = $"{invocation.RunId}:assistant:1"
        };

        return new AgentResponse(message)
        {
            AgentId = response.AgentName,
            ResponseId = invocation.RunId,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["agentblazor_requires_clarification"] = response.RequiresClarification,
                ["agentblazor_requires_approval"] = response.RequiresApproval,
                ["agentblazor_execution_step_count"] = response.ExecutionPlan?.Steps.Count ?? response.LegacyPlannedActions.Count,
                ["agentblazor_context_freshness"] = response.ExecutionPlan?.Context.Freshness.ToString()
            }
        };
    }

    private async ValueTask TrackRunEventAsync(AgentBlazorRunTelemetryEvent telemetryEvent)
    {
        try
        {
            await _telemetrySink.TrackRunEventAsync(telemetryEvent);
        }
        catch
        {
            // Ignore telemetry sink failures to avoid affecting agent execution.
        }
    }

    private AgentBlazorRunTelemetryEvent CreateRunEvent(
        string agentName,
        bool hasContext,
        AgentBlazorRunEventKind kind,
        AgentBlazorRunOutcome? outcome = null,
        string? detail = null)
    {
        return new AgentBlazorRunTelemetryEvent
        {
            Kind = kind,
            Source = AgentBlazorTelemetrySources.AgUiHosted,
            AgentName = agentName,
            Outcome = outcome,
            ProviderConfigured = true,
            Tier = _entitlementService?.CurrentTier.ToString(),
            HasContext = hasContext,
            HasRegisteredComponents = _registryHub?.GetAll().Any(static r => r.GetAll().Count > 0) ?? false,
            Detail = detail
        };
    }

    private sealed record TurnInvocation(
        AgentTurnRequest Request,
        string RunId,
        bool HasContext,
        HostedRunOperation Operation);

    private enum HostedRunOperation
    {
        Run,
        Connect,
        Stop
    }

    private sealed class MappingState(string runId)
    {
        private readonly Dictionary<int, PendingToolCall> _pendingByStep = new();
        private readonly Queue<PendingToolCall> _pendingWithoutStep = new();

        public int TextMessageCount { get; set; }
        public int ToolCallCount { get; set; }
        public string? ActiveTextMessageId { get; set; }
        public string? LastAgentName { get; set; }

        public PendingToolCall? StartToolCall(
            int? stepIndex,
            PlannedComponentAction? plannedAction,
            IReadOnlyDictionary<string, object?>? toolArguments)
        {
            if (plannedAction is null)
            {
                return null;
            }

            var callId = $"{runId}:tool:{++ToolCallCount}";
            var name = $"{plannedAction.ComponentId}.{plannedAction.ActionId}";
            var args = SanitizeArguments(plannedAction.Arguments);
            MergeArguments(args, toolArguments);
            var pending = new PendingToolCall(callId, name, args);

            if (stepIndex is int resolvedStep)
            {
                _pendingByStep[resolvedStep] = pending;
            }
            else
            {
                _pendingWithoutStep.Enqueue(pending);
            }

            return pending;
        }

        public void UpdateToolCallArguments(int? stepIndex, IReadOnlyDictionary<string, object?>? toolArguments)
        {
            if (toolArguments is null || toolArguments.Count == 0)
            {
                return;
            }

            if (stepIndex is int resolvedStep &&
                _pendingByStep.TryGetValue(resolvedStep, out var pendingForStep))
            {
                MergeArguments(pendingForStep.Arguments, toolArguments);
                return;
            }

            if (_pendingWithoutStep.TryPeek(out var pendingWithoutStep))
            {
                MergeArguments(pendingWithoutStep.Arguments, toolArguments);
            }
        }

        public PendingToolCall? ResolveToolCall(
            int? stepIndex,
            ComponentActionExecutionResult? executionResult)
        {
            if (stepIndex is int resolvedStep &&
                _pendingByStep.Remove(resolvedStep, out var pendingForStep))
            {
                return pendingForStep;
            }

            if (_pendingWithoutStep.Count > 0)
            {
                return _pendingWithoutStep.Dequeue();
            }

            if (executionResult is null)
            {
                return null;
            }

            return new PendingToolCall(
                $"{runId}:tool:{++ToolCallCount}",
                $"{executionResult.ComponentId}.{executionResult.ActionId}",
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
        }

        private static Dictionary<string, object?> SanitizeArguments(IReadOnlyDictionary<string, object?>? source)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (source is null)
            {
                return result;
            }

            foreach (var kvp in source)
            {
                result[kvp.Key] = SanitizeValue(kvp.Value);
            }

            return result;
        }

        private static void MergeArguments(
            IDictionary<string, object?> target,
            IReadOnlyDictionary<string, object?>? source)
        {
            if (source is null)
            {
                return;
            }

            foreach (var kvp in source)
            {
                target[kvp.Key] = SanitizeValue(kvp.Value);
            }
        }

        private static object? SanitizeValue(object? value)
        {
            return value switch
            {
                null => null,
                string => value,
                bool => value,
                byte => value,
                sbyte => value,
                short => value,
                ushort => value,
                int => value,
                uint => value,
                long => value,
                ulong => value,
                float => value,
                double => value,
                decimal => value,
                JsonElement json => SanitizeJsonElement(json),
                _ => value.ToString()
            };
        }

        private static object? SanitizeJsonElement(JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.Number => json.TryGetInt64(out var intValue)
                    ? intValue
                    : json.TryGetDecimal(out var decimalValue)
                        ? decimalValue
                        : json.GetDouble(),
                JsonValueKind.String => json.GetString(),
                _ => json.GetRawText()
            };
        }
    }

    private sealed class PendingToolCall(
        string callId,
        string name,
        Dictionary<string, object?> arguments)
    {
        public string CallId { get; } = callId;

        public string Name { get; } = name;

        public Dictionary<string, object?> Arguments { get; } = arguments;
    }

    private sealed record LegacyStepPair(
        int StepIndex,
        PlannedComponentAction PlannedAction,
        ComponentActionExecutionResult? ExecutionResult);

    private sealed class HostedAgentSession : AgentSession
    {
    }
}
