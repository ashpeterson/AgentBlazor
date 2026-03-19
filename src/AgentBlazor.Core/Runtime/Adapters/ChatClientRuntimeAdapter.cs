using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Planning;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Execution;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Core.Paid;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Adapters;

/// <summary>
/// Runtime adapter backed by the external chat-agent runtime abstractions.
/// This provides a minimal, adapter-first path without depending on the legacy planner.
/// </summary>
public sealed class ChatClientRuntimeAdapter(
    IChatClient chatClient,
    IAgentRegistry agentRegistry,
    IAgentCapabilityRegistry capabilityRegistry,
    IComponentCapabilityCatalog componentCatalog,
    IComponentActionExecutor componentActionExecutor,
    IAgentUiToolCatalog uiToolCatalog,
    IOptions<AgentBlazorOptions> options,
    IConversationStore? conversationStore = null,
    IAgentInspectorStore? inspectorStore = null,
    IActionHistoryStore? actionHistoryStore = null,
    IAgentServiceToolRegistry? serviceToolRegistry = null,
    IEnumerable<IMcpToolProvider>? mcpToolProviders = null,
    IAgentBlazorEntitlementService? entitlementService = null,
    IOptions<PromptTracingOptions>? promptTracingOptions = null,
    IAgentExecutionScopeAccessor? executionScopeAccessor = null,
    ILoggerFactory? loggerFactory = null,
    IServiceProvider? serviceProvider = null) : IAgentRuntimeAdapter
{
    private static readonly AsyncLocal<TurnExecutionState?> CurrentTurnState = new();
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """).RootElement;
    private readonly IChatClient _chatClient = chatClient;
    private readonly IAgentRegistry _agentRegistry = agentRegistry;
    private readonly IAgentCapabilityRegistry _capabilityRegistry = capabilityRegistry;
    private readonly IComponentCapabilityCatalog _componentCatalog = componentCatalog;
    private readonly IComponentActionExecutor _componentActionExecutor = componentActionExecutor;
    private readonly IAgentUiToolCatalog _uiToolCatalog = uiToolCatalog;
    private readonly IOptions<AgentBlazorOptions> _options = options;
    private readonly IConversationStore? _conversationStore = conversationStore;
    private readonly IAgentInspectorStore? _inspectorStore = inspectorStore;
    private readonly IActionHistoryStore? _actionHistoryStore = actionHistoryStore;
    private readonly IAgentServiceToolRegistry? _serviceToolRegistry = serviceToolRegistry;
    private readonly IEnumerable<IMcpToolProvider>? _mcpToolProviders = mcpToolProviders;
    private readonly IAgentBlazorEntitlementService? _entitlementService = entitlementService;
    private readonly IOptions<PromptTracingOptions>? _promptTracingOptions = promptTracingOptions;
    private readonly IAgentExecutionScopeAccessor? _executionScopeAccessor = executionScopeAccessor;
    private readonly ILoggerFactory? _loggerFactory = loggerFactory;
    private readonly IServiceProvider? _serviceProvider = serviceProvider;
    private readonly ConcurrentDictionary<string, Task<SessionState>> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public bool SupportsStreaming => true;

    public bool SupportsReconnect => false;

    public bool SupportsCancellation => false;

    public async Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceBuilder = new PromptTraceBuilder(_promptTracingOptions)
            .RecordEntry(request)
            .StartStage();
        var registration = ResolveAgentRegistration(request.AgentName, request.Context);
        if (registration is null)
        {
            return await BuildNoAgentResponseAsync(traceBuilder, request, cancellationToken).ConfigureAwait(false);
        }

        traceBuilder.RecordEntry(request, registration.Name);
        var capabilityPolicy = ResolveAllowedCapabilityPolicy(registration);
        var projectedTools = await ResolveToolsAsync(registration, request, turnState: null, cancellationToken).ConfigureAwait(false);
        if (projectedTools.Count == 0)
        {
            return await BuildNoAvailableActionsResponseAsync(
                traceBuilder,
                registration,
                capabilityPolicy,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        var sessionState = await GetOrCreateSessionStateAsync(registration, request, cancellationToken).ConfigureAwait(false);
        await sessionState.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;
        var turnState = new TurnExecutionState(
            registration.Name,
            request.GetEffectiveSessionId(),
            ResolveOrCreateRunId(request),
            request.UserMessage,
            request.GeneratedUiAction,
            request.Context is null
                ? null
                : new Dictionary<string, string>(request.Context, StringComparer.OrdinalIgnoreCase));
        try
        {
            var agent = await CreateAgentAsync(registration, request, turnState, cancellationToken).ConfigureAwait(false);
            CurrentTurnState.Value = turnState;
            var response = await agent.RunAsync(
                new ChatMessage(ChatRole.User, BuildUserMessage(request)),
                sessionState.Session,
                options: null,
                cancellationToken).ConfigureAwait(false);

            var turnResponse = BuildTurnResponse(registration.Name, response.Text, request, turnState);
            await StoreConversationTurnAsync(registration.Name, request, turnResponse, cancellationToken).ConfigureAwait(false);
            await RecordActionHistoryAsync(request, turnState, cancellationToken).ConfigureAwait(false);
            await StoreTraceAsync(traceBuilder, turnState, turnResponse, cancellationToken).ConfigureAwait(false);
            RecordInspectorRun(registration.Name, request, turnState, turnResponse, startedAt, succeeded: !turnState.HasFailures, errorMessage: turnState.HasFailures ? turnResponse.ResponseText : null);
            return turnResponse;
        }
        catch (Exception ex)
        {
            traceBuilder.RecordFailure(ex.Message);
            await StoreTraceAsync(traceBuilder, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            CurrentTurnState.Value = null;
            sessionState.Gate.Release();
        }
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var traceBuilder = new PromptTraceBuilder(_promptTracingOptions)
            .RecordEntry(request)
            .StartStage();
        var registration = ResolveAgentRegistration(request.AgentName, request.Context);
        if (registration is null)
        {
            var noAgentResponse = await BuildNoAgentResponseAsync(traceBuilder, request, cancellationToken).ConfigureAwait(false);
            var noAgentRunId = ResolveOrCreateRunId(request);
            var noAgentSequence = 0L;

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunStarted,
                RunId = noAgentRunId,
                Sequence = ++noAgentSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noAgentResponse.AgentName
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageStart,
                RunId = noAgentRunId,
                Sequence = ++noAgentSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noAgentResponse.AgentName
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageContent,
                RunId = noAgentRunId,
                Sequence = ++noAgentSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noAgentResponse.AgentName,
                TextDelta = noAgentResponse.ResponseText
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageEnd,
                RunId = noAgentRunId,
                Sequence = ++noAgentSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noAgentResponse.AgentName
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunFinished,
                RunId = noAgentRunId,
                Sequence = ++noAgentSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noAgentResponse.AgentName,
                Response = noAgentResponse
            };
            yield break;
        }

        traceBuilder.RecordEntry(request, registration.Name);
        var capabilityPolicy = ResolveAllowedCapabilityPolicy(registration);
        var projectedTools = await ResolveToolsAsync(registration, request, turnState: null, cancellationToken).ConfigureAwait(false);
        if (projectedTools.Count == 0)
        {
            var noActionsResponse = await BuildNoAvailableActionsResponseAsync(
                traceBuilder,
                registration,
                capabilityPolicy,
                request,
                cancellationToken).ConfigureAwait(false);
            var noActionsRunId = ResolveOrCreateRunId(request);
            var noActionsSequence = 0L;

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunStarted,
                RunId = noActionsRunId,
                Sequence = ++noActionsSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noActionsResponse.AgentName
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageStart,
                RunId = noActionsRunId,
                Sequence = ++noActionsSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noActionsResponse.AgentName
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageContent,
                RunId = noActionsRunId,
                Sequence = ++noActionsSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noActionsResponse.AgentName,
                TextDelta = noActionsResponse.ResponseText
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageEnd,
                RunId = noActionsRunId,
                Sequence = ++noActionsSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noActionsResponse.AgentName
            };

            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunFinished,
                RunId = noActionsRunId,
                Sequence = ++noActionsSequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = noActionsResponse.AgentName,
                Response = noActionsResponse
            };
            yield break;
        }

        var sessionState = await GetOrCreateSessionStateAsync(registration, request, cancellationToken).ConfigureAwait(false);
        await sessionState.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var runId = ResolveOrCreateRunId(request);
        var sequence = 0L;
        var responseText = new StringBuilder();
        var openedTextMessage = false;
        var startedAt = DateTimeOffset.UtcNow;
        var turnState = new TurnExecutionState(
            registration.Name,
            request.GetEffectiveSessionId(),
            runId,
            request.UserMessage,
            request.GeneratedUiAction,
            request.Context is null
                ? null
                : new Dictionary<string, string>(request.Context, StringComparer.OrdinalIgnoreCase));

        try
        {
            var agent = await CreateAgentAsync(registration, request, turnState, cancellationToken).ConfigureAwait(false);
            CurrentTurnState.Value = turnState;
            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunStarted,
                RunId = runId,
                Sequence = ++sequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = registration.Name
            };

            CurrentTurnState.Value = turnState;
            await foreach (var update in agent.RunStreamingAsync(
                               new ChatMessage(ChatRole.User, BuildUserMessage(request)),
                               sessionState.Session,
                               options: null,
                               cancellationToken).ConfigureAwait(false))
            {
                foreach (var queuedEvent in turnState.DrainStreamEvents())
                {
                    yield return queuedEvent with
                    {
                        RunId = runId,
                        Sequence = ++sequence,
                        AgentName = queuedEvent.AgentName ?? registration.Name
                    };
                }

                var text = update.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!openedTextMessage)
                {
                    openedTextMessage = true;
                    yield return new AgentTurnStreamEvent
                    {
                        Kind = AgentTurnStreamEventKind.TextMessageStart,
                        RunId = runId,
                        Sequence = ++sequence,
                        Timestamp = update.CreatedAt ?? DateTimeOffset.UtcNow,
                        AgentName = registration.Name
                    };
                }

                responseText.Append(text);
                yield return new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.TextMessageContent,
                    RunId = runId,
                    Sequence = ++sequence,
                    Timestamp = update.CreatedAt ?? DateTimeOffset.UtcNow,
                    AgentName = registration.Name,
                    TextDelta = text
                };

                foreach (var queuedEvent in turnState.DrainStreamEvents())
                {
                    yield return queuedEvent with
                    {
                        RunId = runId,
                        Sequence = ++sequence,
                        AgentName = queuedEvent.AgentName ?? registration.Name
                    };
                }
            }

            foreach (var queuedEvent in turnState.DrainStreamEvents())
            {
                yield return queuedEvent with
                {
                    RunId = runId,
                    Sequence = ++sequence,
                    AgentName = queuedEvent.AgentName ?? registration.Name
                };
            }

            if (openedTextMessage)
            {
                yield return new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.TextMessageEnd,
                    RunId = runId,
                    Sequence = ++sequence,
                    Timestamp = DateTimeOffset.UtcNow,
                    AgentName = registration.Name
                };
            }

            var turnResponse = BuildTurnResponse(registration.Name, responseText.ToString(), request, turnState);
            await StoreConversationTurnAsync(registration.Name, request, turnResponse, cancellationToken).ConfigureAwait(false);
            await RecordActionHistoryAsync(request, turnState, cancellationToken).ConfigureAwait(false);
            await StoreTraceAsync(traceBuilder, turnState, turnResponse, cancellationToken).ConfigureAwait(false);
            RecordInspectorRun(registration.Name, request, turnState, turnResponse, startedAt, succeeded: !turnState.HasFailures, errorMessage: turnState.HasFailures ? turnResponse.ResponseText : null);
            yield return new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunFinished,
                RunId = runId,
                Sequence = ++sequence,
                Timestamp = DateTimeOffset.UtcNow,
                AgentName = registration.Name,
                Response = turnResponse
            };
        }
        finally
        {
            CurrentTurnState.Value = null;
            sessionState.Gate.Release();
        }
    }

    public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = runId;
        _ = cancellationToken;
        throw new NotSupportedException("This runtime adapter does not support reconnecting to existing runs.");
    }

    public Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = runId;
        _ = cancellationToken;
        throw new NotSupportedException("This runtime adapter does not support canceling active runs.");
    }

    private async Task<SessionState> GetOrCreateSessionStateAsync(
        AgentRegistration registration,
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        var sessionKey = BuildSessionKey(registration.Name, request);
        var factory = new Func<string, Task<SessionState>>(_ => CreateSessionStateAsync(registration, cancellationToken));
        var task = _sessions.GetOrAdd(sessionKey, factory);

        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            _sessions.TryRemove(sessionKey, out _);
            throw;
        }
    }

    private async Task<SessionState> CreateSessionStateAsync(
        AgentRegistration registration,
        CancellationToken cancellationToken)
    {
        var agent = await CreateAgentAsync(registration, request: null, turnState: null, cancellationToken).ConfigureAwait(false);
        var session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        return new SessionState(session);
    }

    private async Task<ChatClientAgent> CreateAgentAsync(
        AgentRegistration registration,
        AgentTurnRequest? request,
        TurnExecutionState? turnState,
        CancellationToken cancellationToken)
    {
        var instructions = ResolveInstructions(registration);
        var chatOptions = new ChatOptions();
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            chatOptions.Instructions = instructions;
        }

        var tools = await ResolveToolsAsync(registration, request, turnState, cancellationToken).ConfigureAwait(false);
        if (tools.Count > 0)
        {
            chatOptions.Tools = tools.ToList();
        }

        var executionServiceProvider = ResolveExecutionServiceProvider();

        return new ChatClientAgent(
            _chatClient,
            new ChatClientAgentOptions
            {
                Id = registration.Name,
                Name = registration.Name,
                Description = registration.Description,
                ChatOptions = chatOptions,
                UseProvidedChatClientAsIs = false
            },
            _loggerFactory,
            executionServiceProvider);
    }

    private async Task<IReadOnlyList<AITool>> ResolveToolsAsync(
        AgentRegistration registration,
        AgentTurnRequest? request,
        TurnExecutionState? turnState,
        CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        var allowedCapabilities = ResolveAllowedCapabilityPolicy(registration).AllowedCapabilities;
        var executionServiceProvider = ResolveExecutionServiceProvider();
        var semanticCapabilities = executionServiceProvider is not null
            ? _capabilityRegistry.GetActions(executionServiceProvider)
            : [];

        foreach (var capability in semanticCapabilities)
        {
            if (!IsNonComponentToolAllowed(registration, capability.ActionId))
            {
                continue;
            }

            tools.Add(CreateCapabilityTool(capability, turnState));
        }

        foreach (var component in allowedCapabilities)
        {
            foreach (var action in component.Actions)
            {
                var functionName = NormalizeToolName($"ui_{component.ComponentId}_{action.ActionId}");
                tools.Add(CreateComponentActionTool(functionName, component, action, turnState));
            }
        }

        if (ShouldProjectGeneratedUiTools(request))
        {
            foreach (var descriptor in _uiToolCatalog.GetTools())
            {
                tools.Add(CreateGeneratedUiTool(descriptor, turnState));
            }
        }

        foreach (var tool in _serviceToolRegistry?.GetTools() ?? [])
        {
            if (!IsNonComponentToolAllowed(registration, tool.Name))
            {
                continue;
            }

            tools.Add(CreateServiceTool(tool, turnState));
        }

        if (_mcpToolProviders is not null)
        {
            foreach (var provider in _mcpToolProviders)
            {
                IReadOnlyList<AgentServiceTool> providerTools;
                try
                {
                    providerTools = await provider.GetToolsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _loggerFactory?
                        .CreateLogger<ChatClientRuntimeAdapter>()
                        .LogWarning(ex, "Failed to resolve MCP tools for runtime adapter.");
                    continue;
                }

                foreach (var tool in providerTools)
                {
                    if (!IsNonComponentToolAllowed(registration, tool.Name))
                    {
                        continue;
                    }

                    tools.Add(CreateServiceTool(tool, turnState));
                }
            }
        }

        return tools;
    }

    private IReadOnlyList<ComponentCapability> ResolveAllowedCapabilities(AgentRegistration registration)
    {
        return ResolveAllowedCapabilityPolicy(registration).AllowedCapabilities;
    }

    private RuntimeCapabilityPolicyResult ResolveAllowedCapabilityPolicy(AgentRegistration registration)
    {
        var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
        return RuntimeCapabilityPolicy.Evaluate(
            _componentCatalog.GetComponents(),
            registration.AllowedComponents,
            registration.AllowedActions,
            effectiveTier);
    }

    private bool IsNonComponentToolAllowed(AgentRegistration registration, string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return registration.AllowedActions.Count == 0 ||
               registration.AllowedActions.Contains(toolName);
    }

    private AITool CreateCapabilityTool(
        AgentCapabilityActionDescriptor capability,
        TurnExecutionState? turnState)
    {
        var functionName = NormalizeToolName($"capability_{capability.ActionId}");
        var declaration = AIFunctionFactory.CreateDeclaration(
            functionName,
            BuildCapabilityActionDescription(capability),
            ParseSchemaOrDefault(capability.InputSchema));
        var invocable = AIFunctionFactory.Create(
            (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
                InvokeCapabilityAsync(capability, arguments, turnState, cancellationToken));

        return WrapFunction(invocable, declaration, capability.RequiresApproval);
    }

    private AITool CreateComponentActionTool(
        string functionName,
        ComponentCapability component,
        ComponentActionCapability action,
        TurnExecutionState? turnState)
    {
        var schema = ParseSchemaOrDefault(action.InputSchema);
        var description = BuildComponentActionDescription(component, action);
        var declaration = AIFunctionFactory.CreateDeclaration(functionName, description, schema);
        var invocable = AIFunctionFactory.Create(
            (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
                InvokeComponentActionAsync(component.ComponentId, action, arguments, turnState, cancellationToken));

        return WrapFunction(invocable, declaration, action.RequiresApproval);
    }

    private AITool CreateServiceTool(AgentServiceTool tool, TurnExecutionState? turnState)
    {
        var functionName = NormalizeToolName(tool.Name);
        var declaration = AIFunctionFactory.CreateDeclaration(
            functionName,
            tool.Description,
            BuildServiceToolSchema(tool.Parameters));
        var invocable = AIFunctionFactory.Create(
            (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
                InvokeServiceToolAsync(tool, arguments, turnState, cancellationToken));

        return WrapFunction(invocable, declaration, requiresApproval: false);
    }

    private AITool CreateGeneratedUiTool(AgentUiToolDescriptor descriptor, TurnExecutionState? turnState)
    {
        var functionName = NormalizeToolName($"generated_ui_{descriptor.ToolId}");
        var declaration = AIFunctionFactory.CreateDeclaration(
            functionName,
            descriptor.Description,
            ParseSchemaOrDefault(descriptor.InputSchema));
        var invocable = AIFunctionFactory.Create(
            (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
                InvokeGeneratedUiToolAsync(descriptor.ToolId, arguments, turnState, cancellationToken));

        return WrapFunction(invocable, declaration, requiresApproval: false);
    }

    private static AITool WrapFunction(
        AIFunction invocable,
        AIFunctionDeclaration declaration,
        bool requiresApproval)
    {
        var wrapped = new SchemaOverrideAIFunction(invocable, declaration);
        _ = requiresApproval;
        return wrapped;
    }

    private async Task<string> InvokeComponentActionAsync(
        string componentId,
        ComponentActionCapability capability,
        AIFunctionArguments arguments,
        TurnExecutionState? turnState,
        CancellationToken cancellationToken)
    {
        var invocationArguments = new Dictionary<string, object?>(
            arguments,
            StringComparer.OrdinalIgnoreCase);

        if (turnState is not null)
        {
            invocationArguments.TryAdd(AgentRuntimeContextKeys.SessionId, turnState.SessionId);
            invocationArguments.TryAdd(AgentRuntimeContextKeys.RunId, turnState.RunId);
        }

        var plannedAction = new PlannedComponentAction(
            componentId,
            capability.ActionId,
            Reason: "Invoked by runtime adapter tool projection.",
            Arguments: invocationArguments);
        var stepIndex = turnState?.AddPlannedAction(
            plannedAction,
            AgentExecutionStepKind.UiAction,
            requiresApproval: capability.RequiresApproval);
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StepStarted,
            PlannedAction = plannedAction,
            StepIndex = stepIndex
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallStart,
            PlannedAction = plannedAction,
            StepIndex = stepIndex
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallArgs,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ToolArguments = invocationArguments
        });

        if (capability.RequiresApproval &&
            !ComponentActionApprovalPolicy.IsApprovalGranted(
                componentId,
                capability.ActionId,
                turnState?.RuntimeContext))
        {
            var policyDecision = RuntimeTrustDecisions.BuildPolicyDecision(
                componentId,
                capability.ActionId,
                requiresApproval: true);
            var approval = new PendingApproval(
                componentId,
                capability.ActionId,
                capability.Description,
                invocationArguments,
                policyDecision);
            turnState?.AddPendingApproval(approval);
            if (stepIndex is int approvalStepIndex)
            {
                turnState.UpdateExecutionStep(
                    approvalStepIndex,
                    AgentExecutionStepStatus.ApprovalRequired,
                    capability.Description,
                    requiresApproval: true,
                    policyDecision);
            }
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ApprovalRequired,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                PendingApprovals = [approval]
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallEnd,
                PlannedAction = plannedAction,
                StepIndex = stepIndex
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.StepFinished,
                PlannedAction = plannedAction,
                StepIndex = stepIndex
            });
            return $"Approval required for {componentId}.{capability.ActionId}.";
        }

        if (ShouldBlockImplicitGeneratedUiSubmit(componentId, capability.ActionId, turnState))
        {
            var blockedResult = new ComponentActionExecutionResult(
                componentId,
                capability.ActionId,
                ActionOutcome.Blocked,
                "Explicit submit intent is required before submitting a generated form action.");
            turnState?.AddExecutionResult(blockedResult);
            if (stepIndex is int blockedStepIndex)
            {
                turnState.UpdateExecutionStep(
                    blockedStepIndex,
                    MapOutcomeToStepStatus(blockedResult.Outcome),
                    blockedResult.Message);
            }
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallResult,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                ExecutionResult = blockedResult
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallEnd,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                ExecutionResult = blockedResult
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.StepFinished,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                StepSucceeded = false,
                ExecutionResult = blockedResult
            });
            return blockedResult.Message;
        }

        var result = await _componentActionExecutor
            .ExecuteAsync(plannedAction, cancellationToken)
            .ConfigureAwait(false);
        turnState?.AddExecutionResult(result);
        if (stepIndex is int completedStepIndex)
        {
            turnState.UpdateExecutionStep(
                completedStepIndex,
                MapOutcomeToStepStatus(result.Outcome),
                result.Message);
        }
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallResult,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ExecutionResult = result
        });
        if (result.Outcome is ActionOutcome.NeedsClarification &&
            !string.IsNullOrWhiteSpace(result.Message))
        {
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ClarificationRequired,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                ClarificationQuestion = result.Message
            });
        }
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallEnd,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ExecutionResult = result
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StepFinished,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            StepSucceeded = result.Succeeded,
            ExecutionResult = result
        });
        return result.Message;
    }

    private async Task<string> InvokeCapabilityAsync(
        AgentCapabilityActionDescriptor capability,
        AIFunctionArguments arguments,
        TurnExecutionState? turnState,
        CancellationToken cancellationToken)
    {
        var invocationArguments = new Dictionary<string, object?>(
            arguments,
            StringComparer.OrdinalIgnoreCase);

        if (turnState is not null)
        {
            invocationArguments.TryAdd(AgentRuntimeContextKeys.SessionId, turnState.SessionId);
            invocationArguments.TryAdd(AgentRuntimeContextKeys.RunId, turnState.RunId);
        }

        var plannedAction = new PlannedComponentAction(
            capability.CapabilityId,
            capability.LocalActionId,
            Reason: "Invoked semantic capability through runtime adapter tool projection.",
            Arguments: invocationArguments);
        var stepIndex = turnState?.AddPlannedAction(
            plannedAction,
            AgentExecutionStepKind.SemanticCapability,
            requiresApproval: capability.RequiresApproval);
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StepStarted,
            PlannedAction = plannedAction,
            StepIndex = stepIndex
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallStart,
            PlannedAction = plannedAction,
            StepIndex = stepIndex
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallArgs,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ToolArguments = invocationArguments
        });

        if (capability.RequiresApproval &&
            !ComponentActionApprovalPolicy.IsApprovalGranted(
                capability.CapabilityId,
                capability.LocalActionId,
                turnState?.RuntimeContext))
        {
            var policyDecision = RuntimeTrustDecisions.BuildPolicyDecision(
                capability.CapabilityId,
                capability.LocalActionId,
                requiresApproval: true);
            var approval = new PendingApproval(
                capability.CapabilityId,
                capability.LocalActionId,
                capability.Description,
                invocationArguments,
                policyDecision);
            turnState?.AddPendingApproval(approval);
            if (stepIndex is int approvalStepIndex)
            {
                turnState.UpdateExecutionStep(
                    approvalStepIndex,
                    AgentExecutionStepStatus.ApprovalRequired,
                    capability.Description,
                    requiresApproval: true,
                    policyDecision);
            }
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ApprovalRequired,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                PendingApprovals = [approval]
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallEnd,
                PlannedAction = plannedAction,
                StepIndex = stepIndex
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.StepFinished,
                PlannedAction = plannedAction,
                StepIndex = stepIndex
            });
            return $"Approval required for semantic capability {capability.ActionId}.";
        }

        var executionServiceProvider = ResolveExecutionServiceProvider();
        if (executionServiceProvider is null)
        {
            var failure = new ComponentActionExecutionResult(
                capability.CapabilityId,
                capability.LocalActionId,
                ActionOutcome.Failed,
                "Capability execution requires a service provider, but none was available.");
            turnState?.AddExecutionResult(failure);
            if (stepIndex is int failedStepIndex)
            {
                turnState.UpdateExecutionStep(
                    failedStepIndex,
                    AgentExecutionStepStatus.Failed,
                    failure.Message);
            }
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallResult,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                ExecutionResult = failure
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallEnd,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                ExecutionResult = failure
            });
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.StepFinished,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                StepSucceeded = false,
                ExecutionResult = failure
            });
            return failure.Message;
        }

        var capabilityResult = await _capabilityRegistry
            .ExecuteAsync(capability.ActionId, invocationArguments, executionServiceProvider, cancellationToken)
            .ConfigureAwait(false);
        var executionResult = MapCapabilityResult(capability, capabilityResult);
        turnState?.AddExecutionResult(executionResult);
        if (stepIndex is int completedStepIndex)
        {
            turnState.UpdateExecutionStep(
                completedStepIndex,
                MapCapabilityStatus(capabilityResult),
                capabilityResult.RequiresClarification
                    ? capabilityResult.ClarificationQuestion ?? capabilityResult.Summary
                    : capabilityResult.Summary);
        }
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallResult,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ExecutionResult = executionResult
        });
        if (capabilityResult.RequiresClarification &&
            !string.IsNullOrWhiteSpace(capabilityResult.ClarificationQuestion))
        {
            turnState?.AddStreamEvent(new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ClarificationRequired,
                PlannedAction = plannedAction,
                StepIndex = stepIndex,
                ClarificationQuestion = capabilityResult.ClarificationQuestion
            });
        }
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallEnd,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ExecutionResult = executionResult
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StepFinished,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            StepSucceeded = executionResult.Succeeded,
            ExecutionResult = executionResult
        });

        return JsonSerializer.Serialize(capabilityResult);
    }

    private async Task<string> InvokeServiceToolAsync(
        AgentServiceTool tool,
        AIFunctionArguments arguments,
        TurnExecutionState? turnState,
        CancellationToken cancellationToken)
    {
        var invocationArguments = new Dictionary<string, object?>(
            arguments,
            StringComparer.OrdinalIgnoreCase);
        var stepIndex = turnState?.ReserveStep(
            AgentExecutionStepKind.ServiceTool,
            "service-tool",
            tool.Name,
            invocationArguments);
        var plannedAction = new PlannedComponentAction(
            "service-tool",
            tool.Name,
            Reason: "Invoked by runtime adapter tool projection.",
            Arguments: invocationArguments);
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StepStarted,
            PlannedAction = plannedAction,
            StepIndex = stepIndex
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallStart,
            PlannedAction = plannedAction,
            StepIndex = stepIndex
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallArgs,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ToolArguments = invocationArguments
        });

        var executionServiceProvider = ResolveExecutionServiceProvider();
        if (executionServiceProvider is null)
        {
            var unavailableResult = new ComponentActionExecutionResult(
                "service-tool",
                tool.Name,
                ActionOutcome.Failed,
                "IServiceProvider not available for tool execution.");
            turnState?.AddExecutionResult(unavailableResult);
            if (stepIndex is int unavailableStepIndex)
            {
                turnState.UpdateExecutionStep(
                    unavailableStepIndex,
                    AgentExecutionStepStatus.Failed,
                    unavailableResult.Message);
            }
            return unavailableResult.Message;
        }

        var result = await tool.Handler(invocationArguments, executionServiceProvider, cancellationToken).ConfigureAwait(false);
        var executionResult = new ComponentActionExecutionResult(
            "service-tool",
            tool.Name,
            ActionOutcome.Applied,
            result);
        turnState?.AddExecutionResult(executionResult);
        if (stepIndex is int completedStepIndex)
        {
            turnState.UpdateExecutionStep(
                completedStepIndex,
                AgentExecutionStepStatus.Completed,
                result);
        }
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallResult,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ExecutionResult = executionResult
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ToolCallEnd,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            ExecutionResult = executionResult
        });
        turnState?.AddStreamEvent(new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StepFinished,
            PlannedAction = plannedAction,
            StepIndex = stepIndex,
            StepSucceeded = true,
            ExecutionResult = executionResult
        });

        return result;
    }

    private Task<string> InvokeGeneratedUiToolAsync(
        string toolId,
        AIFunctionArguments arguments,
        TurnExecutionState? turnState,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var invocationArguments = new Dictionary<string, object?>(
            arguments,
            StringComparer.OrdinalIgnoreCase);
        turnState?.AddGeneratedUiToolCall(new AgentUiToolCall
        {
            ToolId = toolId,
            Arguments = invocationArguments
        });

        return Task.FromResult($"Prepared generated UI block using '{toolId}'.");
    }

    private static string BuildComponentActionDescription(
        ComponentCapability component,
        ComponentActionCapability action)
    {
        var builder = new StringBuilder();
        builder.Append(action.Description);
        if (!string.IsNullOrWhiteSpace(component.Description))
        {
            builder.Append(" Component: ").Append(component.Description).Append('.');
        }

        builder.Append(" Targets ").Append(component.ComponentId).Append('.');
        if (action.RequiresApproval)
        {
            builder.Append(" User approval is required before execution.");
        }

        return builder.ToString();
    }

    private static string BuildCapabilityActionDescription(AgentCapabilityActionDescriptor capability)
    {
        var builder = new StringBuilder(capability.Description);
        builder.Append(" Semantic capability ").Append(capability.ActionId).Append('.');
        if (!string.IsNullOrWhiteSpace(capability.Category))
        {
            builder.Append(" Category ").Append(capability.Category).Append('.');
        }

        if (capability.RequiresApproval)
        {
            builder.Append(" User approval is required before execution.");
        }

        return builder.ToString();
    }

    private static ComponentActionExecutionResult MapCapabilityResult(
        AgentCapabilityActionDescriptor capability,
        CapabilityResult result)
    {
        var message = result.ClarificationQuestion ?? result.Summary;
        var outcome = result.RequiresClarification
            ? ActionOutcome.NeedsClarification
            : result.Succeeded
                ? ActionOutcome.Applied
                : ActionOutcome.Failed;

        return new ComponentActionExecutionResult(
            capability.CapabilityId,
            capability.LocalActionId,
            outcome,
            message);
    }

    private static AgentExecutionStepStatus MapCapabilityStatus(CapabilityResult result)
    {
        if (result.RequiresClarification)
        {
            return AgentExecutionStepStatus.NeedsClarification;
        }

        return result.Succeeded
            ? AgentExecutionStepStatus.Completed
            : AgentExecutionStepStatus.Failed;
    }

    private static AgentExecutionStepStatus MapOutcomeToStepStatus(ActionOutcome outcome) =>
        outcome switch
        {
            ActionOutcome.Applied or ActionOutcome.Queued => AgentExecutionStepStatus.Completed,
            ActionOutcome.NeedsClarification => AgentExecutionStepStatus.NeedsClarification,
            ActionOutcome.Blocked => AgentExecutionStepStatus.Blocked,
            ActionOutcome.Failed => AgentExecutionStepStatus.Failed,
            _ => AgentExecutionStepStatus.Pending
        };

    private static JsonElement BuildServiceToolSchema(IReadOnlyList<AgentToolParameter> parameters)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var required = new List<string>();

        foreach (var parameter in parameters)
        {
            properties[parameter.Name] = new Dictionary<string, object?>
            {
                ["type"] = parameter.Type,
                ["description"] = parameter.Description
            };

            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonElement ParseSchemaOrDefault(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return EmptyObjectSchema;
        }

        try
        {
            return JsonDocument.Parse(schema).RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyObjectSchema;
        }
    }

    private static string NormalizeToolName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        if (builder.Length == 0 || !char.IsLetter(builder[0]))
        {
            builder.Insert(0, "tool_");
        }

        return builder.ToString();
    }

    private AgentRegistration? ResolveAgentRegistration(
        string? requestedName,
        IDictionary<string, string>? context)
    {
        if (!string.IsNullOrWhiteSpace(requestedName) &&
            _agentRegistry.TryGet(requestedName, out var requested))
        {
            return requested;
        }

        if (RuntimeTurnPreflight.TryGetContextAgentName(context, out var contextAgentName) &&
            contextAgentName is not null &&
            _agentRegistry.TryGet(contextAgentName, out var contextAgent))
        {
            return contextAgent;
        }

        if (RuntimeTurnPreflight.IsAgentLockRequested(context))
        {
            return null;
        }

        if (_agentRegistry.TryGet(_options.Value.DefaultAgent.Name, out var configuredDefault))
        {
            return configuredDefault;
        }

        return _agentRegistry.GetAll()
            .OrderBy(static agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private string BuildSessionKey(string agentName, AgentTurnRequest request)
    {
        var sessionId = request.GetEffectiveSessionId();
        return AgentConversationScope.BuildSessionKey(
            sessionId,
            agentName,
            _options.Value.IsolateConversationsByAgent);
    }

    private string? ResolveInstructions(AgentRegistration registration)
    {
        if (!string.IsNullOrWhiteSpace(registration.Instructions))
        {
            return registration.Instructions;
        }

        return string.IsNullOrWhiteSpace(_options.Value.DefaultAgent.Instructions)
            ? null
            : _options.Value.DefaultAgent.Instructions;
    }

    private async Task<AgentTurnResponse> BuildNoAgentResponseAsync(
        PromptTraceBuilder traceBuilder,
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        var response = RuntimeEarlyExitResponses.BuildNoAgentResponse(
            _agentRegistry.GetAll().Count,
            request.AgentName,
            request.Context);
        traceBuilder.RecordFailure(response.ResponseText);
        await StoreTraceAsync(traceBuilder, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private async Task<AgentTurnResponse> BuildNoAvailableActionsResponseAsync(
        PromptTraceBuilder traceBuilder,
        AgentRegistration registration,
        RuntimeCapabilityPolicyResult capabilityPolicy,
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        var response = RuntimeEarlyExitResponses.BuildNoAllowedActionsResponse(
            registration.Name,
            capabilityPolicy.BlockedByAgentPolicy,
            capabilityPolicy.BlockedByTier,
            _entitlementService?.CurrentTier ?? _options.Value.LicensedTier,
            "actions");
        traceBuilder.RecordFailure(response.ResponseText);
        await StoreTraceAsync(traceBuilder, cancellationToken).ConfigureAwait(false);

        await StoreConversationTurnAsync(registration.Name, request, response, cancellationToken).ConfigureAwait(false);
        return response;
    }

    private AgentTurnResponse BuildTurnResponse(
        string agentName,
        string responseText,
        AgentTurnRequest request,
        TurnExecutionState turnState)
    {
        var executionPlan = RuntimeExecutionPlans.Build(
            agentName,
            turnState.SessionId,
            turnState.RunId,
            request.GetEffectiveUserId(),
            request.Context?.TryGetValue(AgentRuntimeContextKeys.CurrentRoute, out var route) == true ? route : null,
            request.Context?.TryGetValue(AgentRuntimeContextKeys.ContextVersion, out var contextVersion) == true ? contextVersion : null,
            turnState.GetExecutionSteps());
        var executionResults = turnState.GetExecutionResults();
        return RuntimeTurnResponses.Build(
            agentName,
            RuntimePlanResponses.BuildExecutionResponseText(responseText, executionResults),
            executionPlan.Steps.Count > 0 ? [] : turnState.GetPlannedActions(),
            executionResults,
            generatedUi: BuildGeneratedUi(turnState),
            pendingApprovals: turnState.GetPendingApprovals(),
            requiresApproval: turnState.RequiresApproval,
            executionPlan: executionPlan);
    }

    private AgentUiDocument? BuildGeneratedUi(TurnExecutionState turnState)
    {
        return RuntimeGeneratedUi.BuildDocument(
            _uiToolCatalog,
            turnState.GetGeneratedUiToolCalls(),
            message => _loggerFactory?
                .CreateLogger<ChatClientRuntimeAdapter>()
                .LogWarning("Generated UI rendering failed: {Errors}", message));
    }

    private static bool ShouldProjectGeneratedUiTools(AgentTurnRequest? request)
        => request?.Context is { } context &&
           context.TryGetValue(AgentGenerativeUiSpec.GenerateUiContextKey, out var raw) &&
           bool.TryParse(raw, out var enabled) &&
           enabled;

    private static bool ShouldBlockImplicitGeneratedUiSubmit(
        string componentId,
        string actionId,
        TurnExecutionState? turnState)
    {
        if (turnState?.GeneratedUiAction is null)
        {
            return false;
        }

        if (!string.Equals(componentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actionId, AgentComponentCapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsExplicitSubmitIntent(turnState.UserMessage) &&
               !IsExplicitSubmitIntent(turnState.GeneratedUiAction.ActionId);
    }

    private static bool IsExplicitSubmitIntent(string? text)
        => !string.IsNullOrWhiteSpace(text) && (
            text.Contains("submit", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("finalize", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("send", StringComparison.OrdinalIgnoreCase));

    private async Task StoreConversationTurnAsync(
        string agentName,
        AgentTurnRequest request,
        AgentTurnResponse response,
        CancellationToken cancellationToken)
    {
        if (_conversationStore is null)
        {
            return;
        }

        var sessionId = AgentConversationScope.BuildSessionKey(
            request.GetEffectiveSessionId(),
            agentName,
            _options.Value.IsolateConversationsByAgent);

        try
        {
            await _conversationStore.AppendTurnAsync(
                sessionId,
                RuntimePersistenceRecords.CreateConversationTurn(request.UserMessage, response),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _loggerFactory?
                .CreateLogger<ChatClientRuntimeAdapter>()
                .LogWarning(ex, "Failed to store conversation turn for session {SessionId}", sessionId);
        }
    }

    private async Task RecordActionHistoryAsync(
        AgentTurnRequest request,
        TurnExecutionState turnState,
        CancellationToken cancellationToken)
    {
        if (_actionHistoryStore is null)
        {
            return;
        }

        var successfulResults = turnState.GetExecutionResults()
            .Where(static result => result.Succeeded)
            .ToArray();
        if (successfulResults.Length == 0)
        {
            return;
        }

        var plannedActions = turnState.GetPlannedActions();
        foreach (var result in successfulResults)
        {
            var plannedAction = plannedActions.FirstOrDefault(action =>
                string.Equals(action.ComponentId, result.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action.ActionId, result.ActionId, StringComparison.OrdinalIgnoreCase));
            IReadOnlyDictionary<string, object?> args = plannedAction?.Arguments
                ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            try
            {
                await _actionHistoryStore.RecordAsync(new ActionHistoryEntry(
                    SessionId: request.GetEffectiveSessionId(),
                    UserId: request.GetEffectiveUserId(),
                    Timestamp: DateTimeOffset.UtcNow,
                    UserMessage: request.UserMessage,
                    ActionId: result.ActionId,
                    AgentId: result.ComponentId,
                    Args: args), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _loggerFactory?
                    .CreateLogger<ChatClientRuntimeAdapter>()
                    .LogWarning(ex, "Failed to record action history for {ActionId}", result.ActionId);
            }
        }
    }

    private async Task StoreTraceAsync(
        PromptTraceBuilder traceBuilder,
        TurnExecutionState turnState,
        AgentTurnResponse response,
        CancellationToken cancellationToken)
    {
        if (!traceBuilder.IsEnabled)
        {
            return;
        }

        var plannedActions = turnState.GetPlannedActions();
        var generatedUiToolCalls = turnState.GetGeneratedUiToolCalls();
        var executionResults = turnState.GetExecutionResults();
        var firstAction = plannedActions.FirstOrDefault();
        var primaryIntent = plannedActions.Count > 0
            ? "tool_execution"
            : generatedUiToolCalls.Count > 0
                ? "generated_ui"
                : "chat";

        traceBuilder
            .RecordClassification(
                method: "runtime_adapter",
                primaryIntent: primaryIntent,
                confidence: 1.0f,
                targetComponentId: firstAction?.ComponentId,
                targetActionId: firstAction?.ActionId)
            .RecordPlanning(plannedActions, plannedActions.Count + generatedUiToolCalls.Count)
            .RecordExecution(executionResults);

        if (turnState.HasFailures)
        {
            traceBuilder.RecordFailure(response.ResponseText);
        }
        else
        {
            traceBuilder.RecordSuccess(response.ResponseText);
        }

        await StoreTraceAsync(traceBuilder, cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreTraceAsync(
        PromptTraceBuilder traceBuilder,
        CancellationToken cancellationToken)
    {
        var traceStore = ResolveTraceStore();
        if (traceStore is null || !traceBuilder.IsEnabled)
        {
            return;
        }

        try
        {
            var trace = traceBuilder.Build();
            if (trace is not null)
            {
                await traceStore.StoreAsync(trace, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _loggerFactory?
                .CreateLogger<ChatClientRuntimeAdapter>()
                .LogWarning(ex, "Failed to store trace");
        }
    }

    private IPromptTraceStore? ResolveTraceStore()
    {
        return ResolveExecutionServiceProvider()?.GetService(typeof(IPromptTraceStore)) as IPromptTraceStore
               ?? _serviceProvider?.GetService(typeof(IPromptTraceStore)) as IPromptTraceStore;
    }

    private IServiceProvider? ResolveExecutionServiceProvider()
    {
        return _executionScopeAccessor?.Current ?? _serviceProvider;
    }

    private void RecordInspectorRun(
        string agentName,
        AgentTurnRequest request,
        TurnExecutionState turnState,
        AgentTurnResponse? response,
        DateTimeOffset startedAt,
        bool succeeded,
        string? errorMessage)
    {
        if (_inspectorStore is null)
        {
            return;
        }

        var sessionId = request.GetEffectiveSessionId();

        try
        {
            _inspectorStore.RecordRun(RuntimePersistenceRecords.CreateInspectorRunRecord(
                turnState.RunId,
                sessionId,
                agentName,
                startedAt,
                ResolveInstructions(ResolveAgentRegistration(request.AgentName, request.Context) ?? new AgentRegistration
                {
                    Name = request.AgentName ?? "none"
                }),
                rawPlanResponse: null,
                events: BuildInspectorEvents(request, response, turnState),
                executionResults: turnState.GetExecutionResults(),
                succeeded: succeeded,
                errorMessage: errorMessage));
        }
        catch (Exception ex)
        {
            _loggerFactory?
                .CreateLogger<ChatClientRuntimeAdapter>()
                .LogWarning(ex, "Failed to record inspector run");
        }
    }

    private static IReadOnlyList<InspectorEvent> BuildInspectorEvents(
        AgentTurnRequest request,
        AgentTurnResponse? response,
        TurnExecutionState turnState)
    {
        var events = new List<InspectorEvent>
        {
            new(DateTimeOffset.UtcNow, "RunStarted", null, null, request.UserMessage)
        };

        if (request.Context is { } context &&
            context.TryGetValue(AgentRuntimeContextKeys.AgentHandoffFrom, out var handoffFrom) &&
            !string.IsNullOrWhiteSpace(handoffFrom) &&
            context.TryGetValue(AgentRuntimeContextKeys.AgentHandoffTo, out var handoffTo) &&
            !string.IsNullOrWhiteSpace(handoffTo))
        {
            events.Add(new InspectorEvent(
                DateTimeOffset.UtcNow,
                "AgentHandoff",
                null,
                null,
                $"{handoffFrom} -> {handoffTo}"));
        }

        foreach (var action in turnState.GetPlannedActions())
        {
            events.Add(new InspectorEvent(
                DateTimeOffset.UtcNow,
                "PlannedAction",
                action.ComponentId,
                action.ActionId,
                SerializeInspectorPayload(action.Arguments)));
        }

        foreach (var toolCall in turnState.GetGeneratedUiToolCalls())
        {
            events.Add(new InspectorEvent(
                DateTimeOffset.UtcNow,
                "GeneratedUiTool",
                "generated-ui",
                toolCall.ToolId,
                SerializeInspectorPayload(toolCall.Arguments)));
        }

        foreach (var approval in turnState.GetPendingApprovals())
        {
            events.Add(new InspectorEvent(
                DateTimeOffset.UtcNow,
                "ApprovalRequired",
                approval.ComponentId,
                approval.ActionId,
                SerializeInspectorPayload(approval.Parameters)));
        }

        if (response is not null)
        {
            events.Add(new InspectorEvent(
                DateTimeOffset.UtcNow,
                "RunFinished",
                null,
                null,
                response.ResponseText));
        }

        return events;
    }

    private static string SerializeInspectorPayload(object? payload)
    {
        if (payload is null)
        {
            return "{}";
        }

        try
        {
            return JsonSerializer.Serialize(payload);
        }
        catch
        {
            return payload.ToString() ?? string.Empty;
        }
    }

    private static string ResolveOrCreateRunId(AgentTurnRequest request)
    {
        if (request.Context is not null &&
            request.Context.TryGetValue(AgentRuntimeContextKeys.RunId, out var runId) &&
            !string.IsNullOrWhiteSpace(runId))
        {
            return runId;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string BuildUserMessage(AgentTurnRequest request)
    {
        if ((request.Context is null || request.Context.Count == 0) &&
            request.GeneratedUiAction is null)
        {
            return request.UserMessage;
        }

        var builder = new StringBuilder(request.UserMessage);

        if (request.GeneratedUiAction is not null)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Generated UI action context:");
            builder.Append("BlockId: ").AppendLine(request.GeneratedUiAction.BlockId);
            builder.Append("ActionId: ").AppendLine(request.GeneratedUiAction.ActionId);
            if (!string.IsNullOrWhiteSpace(request.GeneratedUiAction.Prompt))
            {
                builder.Append("Prompt: ").AppendLine(request.GeneratedUiAction.Prompt);
            }

            if (request.GeneratedUiAction.Payload.Count > 0)
            {
                builder.Append("Payload: ").AppendLine(JsonSerializer.Serialize(request.GeneratedUiAction.Payload));
            }
        }

        if (request.Context is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Runtime context:");
            foreach (var pair in request.Context.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("- ").Append(pair.Key).Append(": ").AppendLine(pair.Value);
            }
        }

        return builder.ToString();
    }

    private sealed class SessionState(AgentSession session)
    {
        public AgentSession Session { get; } = session;

        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed class TurnExecutionState(
        string agentName,
        string sessionId,
        string runId,
        string userMessage,
        GeneratedUiActionInvocation? generatedUiAction,
        IReadOnlyDictionary<string, string>? runtimeContext = null)
    {
        private readonly List<PlannedComponentAction> _plannedActions = [];
        private readonly List<ComponentActionExecutionResult> _executionResults = [];
        private readonly List<AgentExecutionStep> _executionSteps = [];
        private readonly List<PendingApproval> _pendingApprovals = [];
        private readonly List<AgentUiToolCall> _generatedUiToolCalls = [];
        private readonly List<AgentTurnStreamEvent> _streamEvents = [];
        private readonly object _gate = new();
        private int _stepCounter;

        public string AgentName { get; } = agentName;

        public string SessionId { get; } = sessionId;

        public string RunId { get; } = runId;

        public string UserMessage { get; } = userMessage;

        public GeneratedUiActionInvocation? GeneratedUiAction { get; } = generatedUiAction;

        public IReadOnlyDictionary<string, string>? RuntimeContext { get; } = runtimeContext;

        public bool RequiresApproval => _pendingApprovals.Count > 0;

        public bool HasFailures =>
            _pendingApprovals.Count > 0 ||
            _executionResults.Any(static result => result.Outcome is not ActionOutcome.Applied and not ActionOutcome.Queued);

        public int AddPlannedAction(
            PlannedComponentAction action,
            AgentExecutionStepKind kind,
            bool requiresApproval = false)
        {
            lock (_gate)
            {
                var stepIndex = ++_stepCounter;
                _plannedActions.Add(action);
                _executionSteps.Add(CreateExecutionStep(
                    stepIndex,
                    kind,
                    action.ComponentId,
                    action.ActionId,
                    requiresApproval
                        ? AgentExecutionStepStatus.ApprovalRequired
                        : AgentExecutionStepStatus.Pending,
                    requiresApproval,
                    RuntimeTrustDecisions.BuildPolicyDecision(action.ComponentId, action.ActionId, requiresApproval),
                    action.Arguments));
                return stepIndex;
            }
        }

        public int ReserveStep(
            AgentExecutionStepKind kind,
            string targetId,
            string actionId,
            IReadOnlyDictionary<string, object?>? arguments = null,
            bool requiresApproval = false)
        {
            lock (_gate)
            {
                var stepIndex = ++_stepCounter;
                _executionSteps.Add(CreateExecutionStep(
                    stepIndex,
                    kind,
                    targetId,
                    actionId,
                    requiresApproval
                        ? AgentExecutionStepStatus.ApprovalRequired
                        : AgentExecutionStepStatus.Pending,
                    requiresApproval,
                    RuntimeTrustDecisions.BuildPolicyDecision(targetId, actionId, requiresApproval),
                    arguments));
                return stepIndex;
            }
        }

        public void UpdateExecutionStep(
            int stepIndex,
            AgentExecutionStepStatus status,
            string? message = null,
            bool? requiresApproval = null,
            AgentPolicyDecision? policyDecision = null)
        {
            lock (_gate)
            {
                var index = _executionSteps.FindIndex(step => step.Order == stepIndex);
                if (index < 0)
                {
                    return;
                }

                var current = _executionSteps[index];
                _executionSteps[index] = current with
                {
                    Status = status,
                    RequiresApproval = requiresApproval ?? current.RequiresApproval,
                    PolicyDecision = policyDecision ?? current.PolicyDecision,
                    Message = message ?? current.Message
                };
            }
        }

        public void AddExecutionResult(ComponentActionExecutionResult result)
        {
            lock (_gate)
            {
                _executionResults.Add(result);
            }
        }

        public void AddPendingApproval(PendingApproval approval)
        {
            lock (_gate)
            {
                _pendingApprovals.Add(approval);
            }
        }

        public void AddGeneratedUiToolCall(AgentUiToolCall toolCall)
        {
            lock (_gate)
            {
                _generatedUiToolCalls.Add(toolCall);
            }
        }

        public void AddStreamEvent(AgentTurnStreamEvent streamEvent)
        {
            lock (_gate)
            {
                _streamEvents.Add(streamEvent);
            }
        }

        public IReadOnlyList<AgentTurnStreamEvent> DrainStreamEvents()
        {
            lock (_gate)
            {
                var drained = _streamEvents.ToArray();
                _streamEvents.Clear();
                return drained;
            }
        }

        public IReadOnlyList<PlannedComponentAction> GetPlannedActions()
        {
            lock (_gate)
            {
                return [.. _plannedActions];
            }
        }

        public IReadOnlyList<ComponentActionExecutionResult> GetExecutionResults()
        {
            lock (_gate)
            {
                return [.. _executionResults];
            }
        }

        public IReadOnlyList<AgentExecutionStep> GetExecutionSteps()
        {
            lock (_gate)
            {
                return [.. _executionSteps];
            }
        }

        public IReadOnlyList<PendingApproval> GetPendingApprovals()
        {
            lock (_gate)
            {
                return [.. _pendingApprovals];
            }
        }

        public IReadOnlyList<AgentUiToolCall> GetGeneratedUiToolCalls()
        {
            lock (_gate)
            {
                return [.. _generatedUiToolCalls];
            }
        }

        private static AgentExecutionStep CreateExecutionStep(
            int stepIndex,
            AgentExecutionStepKind kind,
            string targetId,
            string actionId,
            AgentExecutionStepStatus status,
            bool requiresApproval,
            AgentPolicyDecision policyDecision,
            IReadOnlyDictionary<string, object?>? arguments)
        {
            return new AgentExecutionStep(
                StepId: $"{stepIndex}:{targetId}.{actionId}",
                Order: stepIndex,
                Kind: kind,
                TargetId: targetId,
                ActionId: actionId,
                Status: status,
                RequiresApproval: requiresApproval,
                PolicyDecision: policyDecision,
                Arguments: arguments,
                Message: null);
        }
    }

    private sealed class SchemaOverrideAIFunction(
        AIFunction innerFunction,
        AIFunctionDeclaration declaration) : DelegatingAIFunction(innerFunction)
    {
        public override string Name => declaration.Name;

        public override string Description => declaration.Description;

        public override JsonElement JsonSchema => declaration.JsonSchema;

        public override JsonElement? ReturnJsonSchema => declaration.ReturnJsonSchema;
    }
}
