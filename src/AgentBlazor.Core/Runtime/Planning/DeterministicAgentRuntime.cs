using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Options;
using AgentBlazor.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Deterministic agent runtime with clean separation:
/// 1. Plan (LLM returns structured JSON)
/// 2. Validate (check all params, components)
/// 3. Execute (deterministic step execution)
///
/// No fallbacks. No heuristics. No regex inference.
/// </summary>
internal sealed class DeterministicAgentRuntime : IAgentRuntime, IAgentRuntimeStreaming
{
    private const string RunIdContextKey = AgentRuntimeContextKeys.RunId;
    private const int MaxRetainedRuns = 200;

    private readonly ConcurrentDictionary<string, StreamingRunState> _activeStreamingRuns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StreamingRunHistory> _completedStreamingRuns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _completedRunOrder = new();

    private readonly IStructuredActionPlanner _planner;
    private readonly IPlanValidator _validator;
    private readonly IPlanExecutor _executor;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IComponentCapabilityCatalog _componentCatalog;
    private readonly IAgentUiToolCatalog _uiToolCatalog;
    private readonly IConversationManager? _conversationManager;
    private readonly IAgentComponentRegistry? _componentRegistry;
    private readonly IComponentRouteRegistry _componentRouteRegistry;
    private readonly IRouteRegistry _routeRegistry;
    private readonly IOptions<AgentBlazorOptions> _options;
    private readonly IAgentBlazorTelemetrySink _telemetrySink;
    private readonly IOptions<PromptTracingOptions>? _tracingOptions;
    private readonly IPromptTraceStore? _traceStore;
    private readonly ILogger<DeterministicAgentRuntime>? _logger;

    private sealed class StreamingRunState
    {
        public required string RunId { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public required List<AgentTurnStreamEvent> EventLog { get; init; }
        public required List<Channel<AgentTurnStreamEvent>> Subscribers { get; init; }
        public required object Gate { get; init; }
        public bool Completed { get; set; }
        public long Sequence { get; set; }
    }

    private sealed record StreamingRunHistory(
        string RunId,
        IReadOnlyList<AgentTurnStreamEvent> Events,
        DateTimeOffset CompletedAt);

    public DeterministicAgentRuntime(
        IStructuredActionPlanner planner,
        IPlanValidator validator,
        IPlanExecutor executor,
        IAgentRegistry agentRegistry,
        IComponentCapabilityCatalog componentCatalog,
        IAgentUiToolCatalog uiToolCatalog,
        IConversationManager? conversationManager,
        IAgentComponentRegistry? componentRegistry,
        IComponentRouteRegistry componentRouteRegistry,
        IRouteRegistry routeRegistry,
        IOptions<AgentBlazorOptions> options,
        IAgentBlazorTelemetrySink telemetrySink,
        IOptions<PromptTracingOptions>? tracingOptions = null,
        IPromptTraceStore? traceStore = null,
        ILogger<DeterministicAgentRuntime>? logger = null)
    {
        _planner = planner;
        _validator = validator;
        _executor = executor;
        _agentRegistry = agentRegistry;
        _componentCatalog = componentCatalog;
        _uiToolCatalog = uiToolCatalog;
        _conversationManager = conversationManager;
        _componentRegistry = componentRegistry;
        _componentRouteRegistry = componentRouteRegistry;
        _routeRegistry = routeRegistry;
        _options = options;
        _telemetrySink = telemetrySink;
        _tracingOptions = tracingOptions;
        _traceStore = traceStore;
        _logger = logger;
    }

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default) =>
        RunTurnCoreAsync(request, emitEvent: null, cancellationToken);

    public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = ResolveOrCreateRunId(request);
        var runState = new StreamingRunState
        {
            RunId = runId,
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
            EventLog = [],
            Subscribers = [],
            Gate = new object(),
            Completed = false,
            Sequence = 0
        };

        if (!_activeStreamingRuns.TryAdd(runId, runState))
        {
            throw new InvalidOperationException($"Run '{runId}' is already active.");
        }

        var channel = Subscribe(runState);
        _ = Task.Run(() => ExecuteStreamingRunAsync(runState, request), CancellationToken.None);

        await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return streamEvent;
        }
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (_activeStreamingRuns.TryGetValue(runId, out var active))
        {
            var channel = Subscribe(active);
            List<AgentTurnStreamEvent> replay;
            long lastReplaySequence = 0;
            var completed = false;
            lock (active.Gate)
            {
                replay = active.EventLog.Select(static e => e with { IsReplay = true }).ToList();
                if (replay.Count > 0)
                {
                    lastReplaySequence = replay[^1].Sequence;
                }
                completed = active.Completed;
            }

            foreach (var streamEvent in replay)
            {
                yield return streamEvent;
            }

            if (completed)
            {
                yield break;
            }

            await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (streamEvent.Sequence <= lastReplaySequence)
                {
                    continue;
                }

                yield return streamEvent with { IsReplay = true };
            }

            yield break;
        }

        if (_completedStreamingRuns.TryGetValue(runId, out var completedRun))
        {
            foreach (var streamEvent in completedRun.Events)
            {
                yield return streamEvent with { IsReplay = true };
            }
        }
    }

    public Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (!_activeStreamingRuns.TryGetValue(runId, out var runState))
        {
            return Task.FromResult(false);
        }

        try
        {
            runState.Cancellation.Cancel();
            return Task.FromResult(true);
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(false);
        }
    }

    private async Task<AgentTurnResponse> RunTurnCoreAsync(
        AgentTurnRequest request,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            throw new ArgumentException("User message is required.", nameof(request));
        }

        var stopwatch = Stopwatch.StartNew();
        var traceBuilder = new PromptTraceBuilder(_tracingOptions);
        var sessionId = request.GetEffectiveSessionId();
        var conversationHistory = await BuildConversationHistoryAsync(sessionId, cancellationToken);

        // Resolve agent
        var registration = ResolveAgent(request.AgentName);
        traceBuilder.RecordEntry(request, registration?.Name);

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.RunStarted,
            AgentName = registration?.Name ?? "none"
        });

        if (registration is null)
        {
            var noAgentResponse = await HandleNoAgentAsync(traceBuilder, cancellationToken);
            await StoreConversationTurnAsync(sessionId, request.UserMessage, noAgentResponse, cancellationToken);
            await EmitTextDeltasAsync(noAgentResponse.AgentName, noAgentResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(noAgentResponse, emitEvent);
            return noAgentResponse;
        }

        // Build context
        var allowedComponents = GetAllowedComponents(registration);
        var mountedComponents = GetMountedComponents();
        var providerConfigured = _planner.IsProviderConfigured;

        await TrackStartedAsync(registration.Name, request, providerConfigured);

        if (!providerConfigured)
        {
            var providerMissingResponse = await BuildProviderMissingResponseAsync(
                registration.Name,
                traceBuilder,
                cancellationToken);

            await TrackFinishedAsync(
                registration.Name,
                request,
                AgentBlazorRunOutcome.ProviderMissing,
                plannedCount: 0,
                executedCount: 0,
                providerConfigured: false);

            await StoreConversationTurnAsync(sessionId, request.UserMessage, providerMissingResponse, cancellationToken);
            await EmitTextDeltasAsync(providerMissingResponse.AgentName, providerMissingResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(providerMissingResponse, emitEvent);
            return providerMissingResponse;
        }

        try
        {
            // PHASE 1: PLAN
            _logger?.LogInformation("Phase 1: Planning for request: {Request}", request.UserMessage);

            var availableRoutes = _routeRegistry.GetAll()
                .Select(r => new AvailableRoute { Path = r.Path, Description = r.Description, Aliases = r.Aliases })
                .ToList();

            var planRequest = new ActionPlanRequest
            {
                UserMessage = request.UserMessage,
                SessionId = sessionId,
                UserId = request.GetEffectiveUserId(),
                GenerateUi = IsGeneratedUiRequested(request.Context),
                GeneratedUiAction = request.GeneratedUiAction,
                AvailableComponents = allowedComponents,
                MountedComponents = mountedComponents,
                ConversationHistory = conversationHistory,
                AvailableRoutes = availableRoutes
            };

            var plan = await _planner.PlanAsync(planRequest, cancellationToken);

            // Handle clarification requests
            if (plan.RequiresClarification)
            {
                _logger?.LogInformation("Plan requires clarification: {Question}", plan.ClarificationNeeded);
                var clarificationResponse = AttachPlannedGeneratedUi(
                    await BuildClarificationResponseAsync(
                        registration.Name,
                        plan.ClarificationNeeded!,
                        traceBuilder,
                        cancellationToken),
                    plan);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, clarificationResponse, cancellationToken);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ClarificationRequired,
                    AgentName = clarificationResponse.AgentName,
                    ClarificationQuestion = clarificationResponse.ResponseText
                });
                await EmitTextDeltasAsync(clarificationResponse.AgentName, clarificationResponse.ResponseText, emitEvent);
                await EmitRunFinishedAsync(clarificationResponse, emitEvent);
                return clarificationResponse;
            }

            if (plan.IsEmpty)
            {
                _logger?.LogInformation("Plan is empty - no actions identified");
                var emptyPlanResponse = AttachPlannedGeneratedUi(
                    await BuildEmptyPlanResponseAsync(
                        registration.Name,
                        traceBuilder,
                        cancellationToken),
                    plan);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, emptyPlanResponse, cancellationToken);
                await EmitTextDeltasAsync(emptyPlanResponse.AgentName, emptyPlanResponse.ResponseText, emitEvent);
                await EmitRunFinishedAsync(emptyPlanResponse, emitEvent);
                return emptyPlanResponse;
            }

            _logger?.LogInformation("Plan created with {StepCount} steps", plan.Steps.Count);

            plan = NormalizePlanComponentTargets(plan, mountedComponents, allowedComponents);
            plan = EnsureNavigationWhenTargetUnmounted(plan, mountedComponents, request.UserMessage);
            plan = EnsureDialogOpenBeforeUnmountedFormAction(plan, mountedComponents, allowedComponents);
            plan = NormalizePlanArguments(plan);
            plan = EnforceGeneratedUiActionPolicies(plan, request.GeneratedUiAction, request.UserMessage);
            var plannedActions = CreatePlannedActions(plan);
            await EmitPlannedActionsAsync(plannedActions, emitEvent);
            var runtimeContext = request.Context is null
                ? null
                : new Dictionary<string, string>(request.Context, StringComparer.OrdinalIgnoreCase);
            var approvedActions = GetApprovedActions(plan, allowedComponents, runtimeContext);
            var pendingApprovals = GetPendingApprovals(plan, allowedComponents, approvedActions);

            if (pendingApprovals.Count > 0)
            {
                var blockedResults = pendingApprovals
                    .Select(static pending => new ComponentActionExecutionResult(
                        pending.ComponentId,
                        pending.ActionId,
                        Outcome: ActionOutcome.Blocked,
                        Message: $"Approval required for {pending.ComponentId}.{pending.ActionId}."))
                    .ToArray();
                var approvalResponseText = BuildApprovalRequiredResponseText(pendingApprovals);

                if (traceBuilder.IsEnabled)
                {
                    traceBuilder
                        .RecordPlanning(plannedActions, allowedComponents.Count)
                        .RecordExecution(blockedResults)
                        .RecordSuccess(approvalResponseText);
                    await StoreTraceAsync(traceBuilder, cancellationToken);
                }

                var approvalResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: approvalResponseText,
                    PlannedActions: plannedActions,
                    ExecutionResults: blockedResults)
                {
                    RequiresApproval = true,
                    PendingApprovals = pendingApprovals
                }, plan);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, approvalResponse, cancellationToken);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ApprovalRequired,
                    AgentName = registration.Name,
                    PendingApprovals = pendingApprovals
                });
                await EmitExecutionResultsAsync(blockedResults, emitEvent);
                await EmitTextDeltasAsync(registration.Name, approvalResponseText, emitEvent);
                await EmitRunFinishedAsync(approvalResponse, emitEvent);
                return approvalResponse;
            }

            // PHASE 2: VALIDATE
            _logger?.LogInformation("Phase 2: Validating plan");

            var validationContext = new PlanValidationContext
            {
                AllowedComponents = allowedComponents,
                MountedComponents = mountedComponents,
                ApprovedActions = approvedActions
            };

            var validationResult = _validator.Validate(plan, validationContext);

            if (!validationResult.IsValid)
            {
                var clarification = validationResult.BuildClarificationQuestion();
                var validationFailures = BuildValidationFailureExecutionResults(validationResult);
                _logger?.LogInformation("Plan validation failed: {Question}", clarification);
                var clarificationText = clarification ?? "The plan could not be validated.";

                if (traceBuilder.IsEnabled)
                {
                    traceBuilder
                        .RecordPlanning(plannedActions, allowedComponents.Count)
                        .RecordExecution(validationFailures)
                        .RecordSuccess(clarificationText);
                    await StoreTraceAsync(traceBuilder, cancellationToken);
                }

                var validationResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: clarificationText,
                    PlannedActions: [],
                    ExecutionResults: validationFailures), plan);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, validationResponse, cancellationToken);
                await EmitExecutionResultsAsync(validationFailures, emitEvent);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ClarificationRequired,
                    AgentName = registration.Name,
                    ClarificationQuestion = clarificationText
                });
                await EmitTextDeltasAsync(registration.Name, clarificationText, emitEvent);
                await EmitRunFinishedAsync(validationResponse, emitEvent);
                return validationResponse;
            }

            _logger?.LogInformation("Plan validated successfully");

            // PHASE 3: EXECUTE
            _logger?.LogInformation("Phase 3: Executing plan");

            var executionOptions = new PlanExecutionOptions
            {
                ContinueOnFailure = false,
                SessionId = sessionId,
                RunId = request.Context is not null &&
                        request.Context.TryGetValue(RunIdContextKey, out var contextRunId) &&
                        !string.IsNullOrWhiteSpace(contextRunId)
                    ? contextRunId
                    : null
            };

            var executionResult = await _executor.ExecuteAsync(plan, executionOptions, cancellationToken);

            // Build response
            var executionResults = executionResult.StepResults
                .Select(r => new ComponentActionExecutionResult(
                    r.Step.ComponentId,
                    r.Step.ActionId,
                    r.Outcome,
                    r.Message))
                .ToArray();
            await EmitExecutionResultsAsync(executionResults, emitEvent);

            var responseText = BuildResponseText(executionResult);

            stopwatch.Stop();

            // Record trace
            if (traceBuilder.IsEnabled)
            {
                traceBuilder
                    .RecordPlanning(plannedActions, allowedComponents.Count)
                    .RecordExecution(executionResults)
                    .RecordSuccess(responseText);
                await StoreTraceAsync(traceBuilder, cancellationToken);
            }

            await TrackFinishedAsync(
                registration.Name,
                request,
                executionResult.Succeeded ? AgentBlazorRunOutcome.Succeeded : AgentBlazorRunOutcome.Failed,
                plannedActions.Length,
                executionResults.Length,
                providerConfigured);

            _logger?.LogInformation(
                "Turn completed in {Duration}ms with {SuccessCount}/{TotalCount} successful steps",
                stopwatch.ElapsedMilliseconds,
                executionResult.SuccessCount,
                executionResult.StepResults.Count);

            var successResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                AgentName: registration.Name,
                ResponseText: responseText,
                PlannedActions: plannedActions,
                ExecutionResults: executionResults), plan);
            await StoreConversationTurnAsync(sessionId, request.UserMessage, successResponse, cancellationToken);
            await EmitTextDeltasAsync(successResponse.AgentName, successResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(successResponse, emitEvent);
            return successResponse;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            traceBuilder.RecordCanceled();
            await StoreTraceAsync(traceBuilder, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Turn failed with exception");
            traceBuilder.RecordFailure(ex.Message);
            await StoreTraceAsync(traceBuilder, CancellationToken.None);
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunError,
                AgentName = registration.Name,
                ErrorMessage = ex.Message
            });
            throw;
        }
    }

    private AgentRegistration? ResolveAgent(string? requestedAgentName)
    {
        if (!string.IsNullOrWhiteSpace(requestedAgentName) &&
            _agentRegistry.TryGet(requestedAgentName, out var requested))
        {
            return requested;
        }

        if (_agentRegistry.TryGet(_options.Value.DefaultAgent.Name, out var configuredDefault))
        {
            return configuredDefault;
        }

        return _agentRegistry.GetAll()
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private IReadOnlyList<AvailableComponent> GetAllowedComponents(AgentRegistration registration)
    {
        var components = _componentCatalog.GetComponents();
        var evaluation = ComponentActionPolicy.EvaluateAllowedCapabilities(
            components,
            registration.AllowedComponents,
            registration.AllowedActions);

        return evaluation.AllowedComponents
            .Select(c => new AvailableComponent
            {
                ComponentId = c.ComponentId,
                Description = c.Description ?? $"{c.ComponentId} component",
                Actions = c.Actions.Select(a => new AvailableAction
                {
                    ActionId = a.ActionId,
                    Description = a.Description ?? $"{a.ActionId} action",
                    RequiresApproval = a.RequiresApproval,
                    Parameters = GetActionParameters(c.ComponentId, a.ActionId)
                }).ToList()
            })
            .ToList();
    }

    private static IReadOnlyList<ActionParameter> GetActionParameters(string componentId, string actionId)
    {
        // Define required parameters for each action
        return (componentId, actionId) switch
        {
            ("AgentNavMenu", "navigate_to") =>
            [
                new ActionParameter { Name = "uri", Type = "string", Required = true, Description = "The URI to navigate to" }
            ],
            ("AgentDataGrid", "filter") =>
            [
                new ActionParameter { Name = "column", Type = "string", Required = true, Description = "Column to filter" },
                new ActionParameter { Name = "operator", Type = "string", Required = true, Description = "Filter operator", AllowedValues = ["eq", "neq", "gt", "gte", "lt", "lte", "contains", "startsWith", "endsWith", "in", "notin", "isnull", "notnull"] },
                new ActionParameter { Name = "value", Type = "any", Required = true, Description = "Value to filter by" }
            ],
            ("AgentDataGrid", "sort") =>
            [
                new ActionParameter { Name = "column", Type = "string", Required = true, Description = "Column to sort by" },
                new ActionParameter { Name = "direction", Type = "string", Required = false, Description = "Sort direction", AllowedValues = ["asc", "desc"] }
            ],
            ("AgentDataGrid", "go_to_page") =>
            [
                new ActionParameter { Name = "pageIndex", Type = "int", Required = false, Description = "Page index (0-based)" },
                new ActionParameter { Name = "page", Type = "int", Required = false, Description = "Page index alias (0-based)" },
                new ActionParameter { Name = "pageSize", Type = "int", Required = false, Description = "Optional page size" }
            ],
            ("AgentDataGrid", "set_page") =>
            [
                new ActionParameter { Name = "pageIndex", Type = "int", Required = false, Description = "Page index (0-based)" },
                new ActionParameter { Name = "page", Type = "int", Required = false, Description = "Page index alias (0-based)" },
                new ActionParameter { Name = "pageSize", Type = "int", Required = false, Description = "Optional page size" }
            ],
            ("AgentDataGrid", "navigate_to_row") =>
            [
                new ActionParameter { Name = "rowKey", Type = "string", Required = false, Description = "Optional row key. If omitted, runtime may infer the top row from current sort/filter state." }
            ],
            ("AgentDataGrid", "select_row") =>
            [
                new ActionParameter { Name = "rowKey", Type = "string", Required = false, Description = "Optional row key. If omitted, runtime may infer the top row from current sort/filter state." }
            ],
            ("AgentForm", "set_field") =>
            [
                new ActionParameter { Name = "field", Type = "string", Required = true, Description = "Field name" },
                new ActionParameter { Name = "value", Type = "any", Required = true, Description = "Field value" }
            ],
            ("AgentTabs", "switch_tab") =>
            [
                new ActionParameter { Name = "index", Type = "int", Required = true, Description = "Tab index (0-based)" }
            ],
            _ => []
        };
    }

    private IReadOnlyList<MountedComponentState> GetMountedComponents()
    {
        if (_componentRegistry is null) return [];

        return _componentRegistry.GetAll()
            .Select(c => new MountedComponentState
            {
                AgentId = c.AgentId,
                ComponentType = c.ComponentType,
                State = c.GetCurrentState()
                    .ToDictionary(kv => kv.Key, kv => FormatMountedStateValue(kv.Value))
            })
            .ToList();
    }

    private static string FormatMountedStateValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string s)
        {
            return s;
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };
    }

    /// <summary>
    /// When the plan's first step targets a component that is not mounted, prepend a navigate_to step.
    /// Uses (1) component route registry if the user has visited that page, else (2) intent→route
    /// by resolving the user message against registered [Route] pages so navigation works without visiting first.
    /// </summary>
    private ActionPlan EnsureNavigationWhenTargetUnmounted(ActionPlan plan, IReadOnlyList<MountedComponentState> mountedComponents, string userMessage)
    {
        if (plan.Steps.Count == 0) return plan;

        var first = plan.Steps[0];
        if (string.Equals(first.ComponentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.ActionId, AgentComponentCapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase))
        {
            return plan;
        }

        if (IsComponentTypeMounted(first.ComponentId, mountedComponents))
        {
            _logger?.LogDebug(
                "Skipping navigation prepend: first step targets component {ComponentId} which is already mounted.",
                first.ComponentId);
            return plan;
        }

        var route = ResolveRouteForUnmountedComponent(first.ComponentId, userMessage);
        if (string.IsNullOrWhiteSpace(route))
        {
            _logger?.LogDebug(
                "Skipping navigation prepend: no route for component {ComponentId} (tried registry and intent→route). Mounted count={MountedCount}",
                first.ComponentId, mountedComponents.Count);
            return plan;
        }

        var navigateStep = new PlannedStep
        {
            ComponentId = AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            ActionId = AgentComponentCapabilityProfile.NavigationNavigateToActionId,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["uri"] = route }
        };

        var newSteps = new List<PlannedStep> { navigateStep };
        foreach (var step in plan.Steps)
        {
            newSteps.Add(step);
        }

        _logger?.LogInformation(
            "Prepending navigation to {Route} because first step targets unmounted component {ComponentId}",
            route, first.ComponentId);

        return plan with { Steps = newSteps };
    }

    /// <summary>
    /// Resolves the route for an unmounted component: first from mount-time registry, then by resolving user message against [Route] pages (intent→route).
    /// </summary>
    private string? ResolveRouteForUnmountedComponent(string componentId, string userMessage)
    {
        if (_componentRouteRegistry.TryGetRoute(componentId, out var fromRegistry) && !string.IsNullOrWhiteSpace(fromRegistry))
        {
            return fromRegistry;
        }

        // Dialog/Form workflows should not guess routes from free-text intent.
        // Use paired route hints if one side is known.
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (_componentRouteRegistry.TryGetRoute(AgentComponentCapabilityProfile.AgentDialogComponentId, out var formPairRoute) &&
                !string.IsNullOrWhiteSpace(formPairRoute))
            {
                return formPairRoute;
            }

            return null;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (_componentRouteRegistry.TryGetRoute(AgentComponentCapabilityProfile.AgentFormComponentId, out var dialogPairRoute) &&
                !string.IsNullOrWhiteSpace(dialogPairRoute))
            {
                return dialogPairRoute;
            }

            return null;
        }

        var matches = _routeRegistry.ResolveAll(userMessage, maxResults: 3);
        if (matches.Count == 0)
        {
            return null;
        }

        var best = matches[0];
        if (best.Confidence < 0.2f)
        {
            _logger?.LogDebug(
                "Intent→route best match for \"{UserMessage}\" is {Path} with confidence {Confidence}; requiring >= 0.2.",
                userMessage, best.Path, best.Confidence);
            return null;
        }

        _logger?.LogDebug(
            "Intent→route: \"{UserMessage}\" → {Path} (confidence {Confidence}, method {Method})",
            userMessage, best.Path, best.Confidence, best.MatchMethod);
        return best.Path;
    }

    private static bool IsComponentTypeMounted(string componentId, IReadOnlyList<MountedComponentState> mountedComponents)
    {
        var expectedType = componentId.StartsWith("Agent", StringComparison.OrdinalIgnoreCase)
            ? componentId[5..]
            : componentId;
        return mountedComponents.Any(m =>
            string.Equals(m.ComponentType, expectedType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// If a form action is planned while no Form component is mounted, prepend/open a dialog step before the first form step.
    /// This enables one-turn flows where form elements are hosted inside dialogs.
    /// </summary>
    private static ActionPlan EnsureDialogOpenBeforeUnmountedFormAction(
        ActionPlan plan,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlyList<AvailableComponent> allowedComponents)
    {
        if (plan.Steps.Count == 0 ||
            IsComponentTypeMounted(AgentComponentCapabilityProfile.AgentFormComponentId, mountedComponents))
        {
            return plan;
        }

        var firstFormIndex = -1;
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            if (string.Equals(step.ComponentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
            {
                firstFormIndex = i;
                break;
            }
        }

        if (firstFormIndex < 0)
        {
            return plan;
        }

        var hasDialogOpenBeforeForm = plan.Steps
            .Take(firstFormIndex)
            .Any(step =>
                string.Equals(step.ComponentId, AgentComponentCapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(step.ActionId, AgentComponentCapabilityProfile.DialogOpenActionId, StringComparison.OrdinalIgnoreCase));
        if (hasDialogOpenBeforeForm)
        {
            return plan;
        }

        if (!TryGetAvailableAction(
                AgentComponentCapabilityProfile.AgentDialogComponentId,
                AgentComponentCapabilityProfile.DialogOpenActionId,
                allowedComponents,
                out _))
        {
            return plan;
        }

        var openDialogStep = new PlannedStep
        {
            ComponentId = AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId = AgentComponentCapabilityProfile.DialogOpenActionId,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };

        var rewritten = new List<PlannedStep>(plan.Steps.Count + 1);
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            if (i == firstFormIndex)
            {
                rewritten.Add(openDialogStep);
            }

            rewritten.Add(plan.Steps[i]);
        }

        return plan with { Steps = rewritten };
    }

    private static ActionPlan NormalizePlanArguments(ActionPlan plan)
    {
        if (plan.Steps.Count == 0)
        {
            return plan;
        }

        var normalizedSteps = plan.Steps
            .Select(static step => step with
            {
                Arguments = ComponentActionArgumentNormalizer.Normalize(
                    step.ComponentId,
                    step.ActionId,
                    step.Arguments)
            })
            .ToArray();

        return plan with { Steps = normalizedSteps };
    }

    private ActionPlan NormalizePlanComponentTargets(
        ActionPlan plan,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlyList<AvailableComponent> allowedComponents)
    {
        if (plan.Steps.Count == 0)
        {
            return plan;
        }

        var mountedByAgentId = mountedComponents.ToDictionary(
            static mounted => mounted.AgentId,
            StringComparer.OrdinalIgnoreCase);
        var normalizedSteps = new List<PlannedStep>(plan.Steps.Count);
        var changed = false;

        foreach (var step in plan.Steps)
        {
            var stepChanged = false;
            var componentId = step.ComponentId?.Trim() ?? string.Empty;
            var targetAgentId = string.IsNullOrWhiteSpace(step.TargetAgentId)
                ? null
                : step.TargetAgentId.Trim();

            if (!string.IsNullOrWhiteSpace(targetAgentId) &&
                mountedByAgentId.TryGetValue(targetAgentId, out var targetedComponent))
            {
                var canonicalFromTarget = ResolveAllowedComponentIdForType(
                    targetedComponent.ComponentType,
                    allowedComponents);
                if (!string.IsNullOrWhiteSpace(canonicalFromTarget) &&
                    !string.Equals(componentId, canonicalFromTarget, StringComparison.OrdinalIgnoreCase))
                {
                    componentId = canonicalFromTarget;
                    stepChanged = true;
                }
            }

            if (!IsAllowedComponent(componentId, allowedComponents))
            {
                if (mountedByAgentId.TryGetValue(componentId, out var referencedInstance))
                {
                    var canonicalFromInstance = ResolveAllowedComponentIdForType(
                        referencedInstance.ComponentType,
                        allowedComponents);
                    if (!string.IsNullOrWhiteSpace(canonicalFromInstance))
                    {
                        componentId = canonicalFromInstance;
                        targetAgentId ??= referencedInstance.AgentId;
                        stepChanged = true;
                    }
                }
                else
                {
                    var canonicalByType = ResolveAllowedComponentIdForType(componentId, allowedComponents);
                    if (!string.IsNullOrWhiteSpace(canonicalByType))
                    {
                        componentId = canonicalByType;
                        stepChanged = true;
                    }
                }
            }

            if (stepChanged)
            {
                changed = true;
                normalizedSteps.Add(step with
                {
                    ComponentId = componentId,
                    TargetAgentId = targetAgentId
                });
                continue;
            }

            normalizedSteps.Add(step);
        }

        if (!changed)
        {
            return plan;
        }

        _logger?.LogDebug("Normalized plan component targets using mounted component instances.");
        return plan with { Steps = normalizedSteps };
    }

    private static bool IsAllowedComponent(
        string componentId,
        IReadOnlyList<AvailableComponent> allowedComponents)
    {
        return allowedComponents.Any(component =>
            string.Equals(component.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveAllowedComponentIdForType(
        string? componentTypeOrId,
        IReadOnlyList<AvailableComponent> allowedComponents)
    {
        if (string.IsNullOrWhiteSpace(componentTypeOrId))
        {
            return null;
        }

        var raw = componentTypeOrId.Trim();
        var exact = allowedComponents.FirstOrDefault(component =>
            string.Equals(component.ComponentId, raw, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.ComponentId;
        }

        var typeName = raw.StartsWith("Agent", StringComparison.OrdinalIgnoreCase)
            ? raw[5..]
            : raw;
        var prefixed = $"Agent{typeName}";
        var prefixedMatch = allowedComponents.FirstOrDefault(component =>
            string.Equals(component.ComponentId, prefixed, StringComparison.OrdinalIgnoreCase));
        if (prefixedMatch is not null)
        {
            return prefixedMatch.ComponentId;
        }

        var bySuffix = allowedComponents.FirstOrDefault(component =>
            component.ComponentId.StartsWith("Agent", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(component.ComponentId[5..], typeName, StringComparison.OrdinalIgnoreCase));
        return bySuffix?.ComponentId;
    }

    private ActionPlan EnforceGeneratedUiActionPolicies(
        ActionPlan plan,
        GeneratedUiActionInvocation? generatedUiAction,
        string userMessage)
    {
        if (generatedUiAction is null || plan.Steps.Count == 0)
        {
            return plan;
        }

        if (IsExplicitSubmitIntent(userMessage) || IsExplicitSubmitIntent(generatedUiAction.ActionId))
        {
            return plan;
        }

        var filteredSteps = plan.Steps
            .Where(static step =>
                !(string.Equals(step.ComponentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(step.ActionId, AgentComponentCapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (filteredSteps.Length == plan.Steps.Count)
        {
            return plan;
        }

        _logger?.LogInformation(
            "Removed {RemovedCount} AgentForm.submit step(s) for generated UI action '{ActionId}' because submit was not explicitly requested.",
            plan.Steps.Count - filteredSteps.Length,
            generatedUiAction.ActionId);

        return plan with { Steps = filteredSteps };
    }

    private static bool IsExplicitSubmitIntent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("submit", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("save", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("finalize", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("send", StringComparison.OrdinalIgnoreCase);
    }

    private static ComponentActionExecutionResult[] BuildValidationFailureExecutionResults(PlanValidationResult validationResult) =>
        validationResult.StepResults
            .Where(static step => !step.IsValid)
            .Select(static step =>
            {
                var message = step.MissingParameters.Count > 0
                    ? $"Action '{step.Step.ActionId}' requires '{step.MissingParameters[0]}' parameter."
                    : step.Errors.FirstOrDefault() ?? "Plan validation failed.";

                return new ComponentActionExecutionResult(
                    step.Step.ComponentId,
                    step.Step.ActionId,
                    Outcome: ActionOutcome.NeedsClarification,
                    Message: message);
            })
            .ToArray();

    private static PlannedComponentAction[] CreatePlannedActions(ActionPlan plan) =>
        plan.Steps
            .Select(static s => new PlannedComponentAction(
                s.ComponentId,
                s.ActionId,
                "Planned by structured planner",
                s.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

    private static IReadOnlySet<string> GetApprovedActions(
        ActionPlan plan,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyDictionary<string, string>? context)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null || context.Count == 0)
        {
            return approved;
        }

        foreach (var step in plan.Steps)
        {
            if (!TryGetAvailableAction(step.ComponentId, step.ActionId, allowedComponents, out var action) ||
                !action.RequiresApproval)
            {
                continue;
            }

            if (ComponentActionApprovalPolicy.IsApprovalGranted(step.ComponentId, step.ActionId, context))
            {
                approved.Add($"{step.ComponentId}.{step.ActionId}");
            }
        }

        return approved;
    }

    private static IReadOnlyList<PendingApproval> GetPendingApprovals(
        ActionPlan plan,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlySet<string> approvedActions)
    {
        var pending = new List<PendingApproval>();

        foreach (var step in plan.Steps)
        {
            if (!TryGetAvailableAction(step.ComponentId, step.ActionId, allowedComponents, out var action) ||
                !action.RequiresApproval)
            {
                continue;
            }

            var actionKey = $"{step.ComponentId}.{step.ActionId}";
            if (approvedActions.Contains(actionKey))
            {
                continue;
            }

            pending.Add(new PendingApproval(
                step.ComponentId,
                step.ActionId,
                action.Description,
                step.Arguments.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase)));
        }

        return pending;
    }

    private static bool TryGetAvailableAction(
        string componentId,
        string actionId,
        IReadOnlyList<AvailableComponent> allowedComponents,
        out AvailableAction action)
    {
        action = default!;

        var component = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));
        if (component is null)
        {
            return false;
        }

        var resolvedAction = component.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (resolvedAction is null)
        {
            return false;
        }

        action = resolvedAction;
        return true;
    }

    private static string BuildApprovalRequiredResponseText(IReadOnlyList<PendingApproval> pendingApprovals)
    {
        if (pendingApprovals.Count == 1)
        {
            var only = pendingApprovals[0];
            return $"Approval required for {only.ComponentId}.{only.ActionId}.";
        }

        return $"Approval required for {pendingApprovals.Count} actions.";
    }

    private static string BuildResponseText(PlanExecutionResult result)
    {
        if (result.StepResults.Count == 0)
        {
            return "I understood your request but no actions were required.";
        }

        var appliedCount = result.AppliedCount;
        var queuedCount = result.QueuedCount;
        var blocked = result.StepResults.Where(r => r.Outcome is ActionOutcome.Blocked).ToList();
        var clarification = result.StepResults.Where(r => r.Outcome is ActionOutcome.NeedsClarification).ToList();
        var failures = result.StepResults.Where(r => r.Outcome is ActionOutcome.Failed).ToList();

        if (failures.Count > 0)
        {
            return failures[0].Message;
        }

        if (clarification.Count > 0)
        {
            return clarification[0].Message;
        }

        if (blocked.Count > 0)
        {
            return blocked.Count == 1
                ? blocked[0].Message
                : $"Blocked {blocked.Count} actions pending approval.";
        }

        if (queuedCount > 0)
        {
            if (appliedCount > 0)
            {
                return $"Applied {appliedCount} actions. Queued {queuedCount} actions.";
            }

            return queuedCount == 1
                ? "Queued 1 action for when the target component is available."
                : $"Queued {queuedCount} actions for when target components are available.";
        }

        return appliedCount == 1
            ? "Done."
            : $"Completed {appliedCount} actions.";
    }

    private AgentTurnResponse AttachPlannedGeneratedUi(
        AgentTurnResponse response,
        ActionPlan plan)
    {
        if (plan.UiToolCalls.Count == 0)
        {
            return response;
        }

        var generatedUi = _uiToolCatalog.BuildDocument(plan.UiToolCalls, out var renderErrors);
        if (generatedUi is null)
        {
            if (renderErrors.Count > 0)
            {
                _logger?.LogWarning(
                    "Generated UI rendering failed for {ToolCallCount} tool calls. Errors: {Errors}",
                    plan.UiToolCalls.Count,
                    string.Join("; ", renderErrors));
            }

            return response;
        }

        return response with { GeneratedUi = generatedUi };
    }

    private static bool IsGeneratedUiRequested(IDictionary<string, string>? context)
    {
        if (context is null ||
            !context.TryGetValue(AgentGenerativeUiSpec.GenerateUiContextKey, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (bool.TryParse(raw, out var asBool))
        {
            return asBool;
        }

        return false;
    }

    private async Task<AgentTurnResponse> BuildProviderMissingResponseAsync(
        string agentName,
        PromptTraceBuilder traceBuilder,
        CancellationToken cancellationToken)
    {
        const string message = "No provider is configured. Register an AgentBlazor provider chat client.";

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure(message);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse(
            AgentName: agentName,
            ResponseText: message,
            PlannedActions: [],
            ExecutionResults: []);
    }

    private async Task<AgentTurnResponse> HandleNoAgentAsync(
        PromptTraceBuilder traceBuilder,
        CancellationToken cancellationToken)
    {
        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure("No agents registered");
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse(
            AgentName: "none",
            ResponseText: "No agents are registered.",
            PlannedActions: [],
            ExecutionResults: []);
    }

    private async Task<AgentTurnResponse> BuildClarificationResponseAsync(
        string agentName,
        string question,
        PromptTraceBuilder traceBuilder,
        CancellationToken cancellationToken)
    {
        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordSuccess(question);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse(
            AgentName: agentName,
            ResponseText: question,
            PlannedActions: [],
            ExecutionResults: []);
    }

    private async Task<AgentTurnResponse> BuildEmptyPlanResponseAsync(
        string agentName,
        PromptTraceBuilder traceBuilder,
        CancellationToken cancellationToken)
    {
        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordSuccess("No actions needed.");
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse(
            AgentName: agentName,
            ResponseText: "I understood your request but no actions are needed.",
            PlannedActions: [],
            ExecutionResults: []);
    }

    private string ResolveOrCreateRunId(AgentTurnRequest request)
    {
        if (request.Context is not null &&
            request.Context.TryGetValue(RunIdContextKey, out var existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static AgentTurnRequest EnsureRunIdOnRequest(AgentTurnRequest request, string runId)
    {
        var context = request.Context is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.Context, StringComparer.OrdinalIgnoreCase);
        context[RunIdContextKey] = runId;
        return request with { Context = context };
    }

    private static Channel<AgentTurnStreamEvent> Subscribe(StreamingRunState runState)
    {
        var channel = Channel.CreateUnbounded<AgentTurnStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (runState.Gate)
        {
            if (runState.Completed)
            {
                channel.Writer.TryComplete();
                return channel;
            }

            runState.Subscribers.Add(channel);
        }

        return channel;
    }

    private async Task ExecuteStreamingRunAsync(
        StreamingRunState runState,
        AgentTurnRequest request)
    {
        var requestWithRunId = EnsureRunIdOnRequest(request, runState.RunId);

        try
        {
            await RunTurnCoreAsync(
                requestWithRunId,
                streamEvent => PublishStreamingEventAsync(runState, streamEvent),
                runState.Cancellation.Token);
        }
        catch (OperationCanceledException) when (runState.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await PublishStreamingEventAsync(runState, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunError,
                AgentName = request.AgentName,
                ErrorCode = "UNHANDLED_EXCEPTION",
                ErrorMessage = ex.Message
            });
        }
        finally
        {
            await FinalizeStreamingRunAsync(runState, runState.Cancellation.IsCancellationRequested);
        }
    }

    private async ValueTask PublishStreamingEventAsync(
        StreamingRunState runState,
        AgentTurnStreamEvent streamEvent)
    {
        List<Channel<AgentTurnStreamEvent>> subscribers;
        AgentTurnStreamEvent normalized;

        lock (runState.Gate)
        {
            if (runState.Completed)
            {
                return;
            }

            normalized = streamEvent with
            {
                RunId = runState.RunId,
                Sequence = ++runState.Sequence,
                Timestamp = streamEvent.Timestamp == default ? DateTimeOffset.UtcNow : streamEvent.Timestamp
            };

            runState.EventLog.Add(normalized);
            subscribers = runState.Subscribers.ToList();
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                await subscriber.Writer.WriteAsync(normalized, CancellationToken.None);
            }
            catch
            {
                lock (runState.Gate)
                {
                    runState.Subscribers.Remove(subscriber);
                }
            }
        }
    }

    private async Task FinalizeStreamingRunAsync(StreamingRunState runState, bool wasCanceled)
    {
        var needsTextEnd = false;
        var hasTerminal = false;

        lock (runState.Gate)
        {
            needsTextEnd =
                runState.EventLog.Any(static e => e.Kind == AgentTurnStreamEventKind.TextMessageStart) &&
                !runState.EventLog.Any(static e => e.Kind == AgentTurnStreamEventKind.TextMessageEnd);
            hasTerminal = runState.EventLog.Any(static e =>
                e.Kind is AgentTurnStreamEventKind.RunFinished or AgentTurnStreamEventKind.RunError);
        }

        if (needsTextEnd)
        {
            await PublishStreamingEventAsync(runState, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageEnd
            });
        }

        if (!hasTerminal)
        {
            await PublishStreamingEventAsync(runState, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunError,
                ErrorCode = wasCanceled ? "STOPPED" : "MISSING_TERMINAL_EVENT",
                ErrorMessage = wasCanceled ? "Run canceled." : "Run ended without terminal event."
            });
        }

        List<Channel<AgentTurnStreamEvent>> subscribers;
        List<AgentTurnStreamEvent> historySnapshot;
        lock (runState.Gate)
        {
            runState.Completed = true;
            subscribers = runState.Subscribers.ToList();
            runState.Subscribers.Clear();
            historySnapshot = runState.EventLog.ToList();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
        }

        _activeStreamingRuns.TryRemove(runState.RunId, out _);
        _completedStreamingRuns[runState.RunId] = new StreamingRunHistory(
            runState.RunId,
            historySnapshot,
            DateTimeOffset.UtcNow);
        _completedRunOrder.Enqueue(runState.RunId);
        TrimCompletedRunHistoryIfNeeded();

        runState.Cancellation.Dispose();
    }

    private void TrimCompletedRunHistoryIfNeeded()
    {
        while (_completedRunOrder.Count > MaxRetainedRuns && _completedRunOrder.TryDequeue(out var runId))
        {
            _completedStreamingRuns.TryRemove(runId, out _);
        }
    }

    private static async ValueTask EmitEventAsync(
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent,
        AgentTurnStreamEvent streamEvent)
    {
        if (emitEvent is null)
        {
            return;
        }

        await emitEvent(streamEvent);
    }

    private static async ValueTask EmitRunFinishedAsync(
        AgentTurnResponse response,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.RunFinished,
            AgentName = response.AgentName,
            Response = response
        });
    }

    private static async ValueTask EmitPlannedActionsAsync(
        IReadOnlyList<PlannedComponentAction> plannedActions,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        for (var index = 0; index < plannedActions.Count; index++)
        {
            var plannedAction = plannedActions[index];
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.StepStarted,
                AgentName = null,
                StepIndex = index,
                PlannedAction = plannedAction
            });

            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallStart,
                StepIndex = index,
                PlannedAction = plannedAction
            });

            if (plannedAction.Arguments is { Count: > 0 })
            {
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ToolCallArgs,
                    StepIndex = index,
                    PlannedAction = plannedAction,
                    ToolArguments = plannedAction.Arguments
                });
            }

            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallEnd,
                StepIndex = index,
                PlannedAction = plannedAction
            });
        }
    }

    private static async ValueTask EmitExecutionResultsAsync(
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        for (var index = 0; index < executionResults.Count; index++)
        {
            var executionResult = executionResults[index];
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ToolCallResult,
                AgentName = null,
                StepIndex = index,
                ExecutionResult = executionResult
            });

            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.StepFinished,
                AgentName = null,
                StepIndex = index,
                StepSucceeded = executionResult.Succeeded,
                ExecutionResult = executionResult
            });
        }
    }

    private static async ValueTask EmitTextDeltasAsync(
        string agentName,
        string responseText,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        if (emitEvent is null || string.IsNullOrWhiteSpace(responseText))
        {
            return;
        }

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.TextMessageStart,
            AgentName = agentName
        });

        foreach (var delta in SplitTextDeltas(responseText))
        {
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextMessageContent,
                AgentName = agentName,
                TextDelta = delta
            });
            await Task.Yield();
        }

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.TextMessageEnd,
            AgentName = agentName
        });
    }

    private static IEnumerable<string> SplitTextDeltas(string text)
    {
        var buffer = new StringBuilder();

        foreach (var ch in text)
        {
            buffer.Append(ch);
            if (char.IsWhiteSpace(ch))
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private async Task StoreTraceAsync(PromptTraceBuilder traceBuilder, CancellationToken cancellationToken)
    {
        if (_traceStore is null || !traceBuilder.IsEnabled) return;

        try
        {
            var trace = traceBuilder.Build();
            if (trace is not null)
            {
                await _traceStore.StoreAsync(trace, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to store trace");
        }
    }

    private async Task<IReadOnlyList<ConversationTurn>> BuildConversationHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (_conversationManager is null)
        {
            return [];
        }

        try
        {
            var history = await _conversationManager.GetHistoryAsync(sessionId, cancellationToken);
            if (history is null || history.Turns.Count == 0)
            {
                return [];
            }

            var plannerTurns = new List<ConversationTurn>(history.Turns.Count * 2);
            foreach (var turn in history.Turns.TakeLast(10))
            {
                if (!string.IsNullOrWhiteSpace(turn.UserMessage))
                {
                    plannerTurns.Add(new ConversationTurn
                    {
                        Role = "user",
                        Content = turn.UserMessage
                    });
                }

                if (!string.IsNullOrWhiteSpace(turn.AgentResponse))
                {
                    plannerTurns.Add(new ConversationTurn
                    {
                        Role = "assistant",
                        Content = turn.AgentResponse
                    });
                }
            }

            return plannerTurns;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load conversation history for session {SessionId}", sessionId);
            return [];
        }
    }

    private async Task StoreConversationTurnAsync(
        string sessionId,
        string userMessage,
        AgentTurnResponse response,
        CancellationToken cancellationToken)
    {
        if (_conversationManager is null)
        {
            return;
        }

        try
        {
            var turn = new AgentBlazor.Core.Runtime.Conversation.ConversationTurn
            {
                Timestamp = DateTime.UtcNow,
                UserMessage = userMessage,
                AgentResponse = response.ResponseText,
                PlannedActions = response.PlannedActions,
                ExecutionResults = response.ExecutionResults
            };

            await _conversationManager.AppendTurnAsync(sessionId, turn, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to store conversation turn for session {SessionId}", sessionId);
        }
    }

    private async Task TrackStartedAsync(
        string agentName,
        AgentTurnRequest request,
        bool providerConfigured)
    {
        await _telemetrySink.TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
        {
            Kind = AgentBlazorRunEventKind.Started,
            Source = AgentBlazorTelemetrySources.Runtime,
            AgentName = agentName,
            RequestedAgentName = request.AgentName,
            HasContext = request.Context?.Count > 0,
            ProviderConfigured = providerConfigured
        });
    }

    private async Task TrackFinishedAsync(
        string agentName,
        AgentTurnRequest request,
        AgentBlazorRunOutcome outcome,
        int plannedCount,
        int executedCount,
        bool providerConfigured)
    {
        await _telemetrySink.TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
        {
            Kind = AgentBlazorRunEventKind.Finished,
            Source = AgentBlazorTelemetrySources.Runtime,
            AgentName = agentName,
            RequestedAgentName = request.AgentName,
            Outcome = outcome,
            PlannedActionCount = plannedCount,
            ExecutionResultCount = executedCount,
            HasContext = request.Context?.Count > 0,
            ProviderConfigured = providerConfigured
        });
    }
}
