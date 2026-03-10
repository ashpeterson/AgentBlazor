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
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Middleware;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Options;
using AgentBlazor.Telemetry;
using AgentBlazor.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Agent runtime: Plan → Validate → Execute.
/// Registry is resolved per-request from AgentComponentRegistryHub using the circuit session ID.
/// </summary>
internal sealed class AgentRuntime : IAgentRuntime, IAgentRuntimeStreaming
{
    private const string RunIdContextKey = AgentRuntimeContextKeys.RunId;
    private const int MaxRetainedRuns = 200;
    private static readonly string[] RouteAgentMetadataKeys = ["agent", "agent_name", "agentName", "agent_lock"];
    private static readonly string[] AgentRouteMetadataKeys = ["route", "routes", "route_prefix", "route_prefixes"];

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
    private readonly IConversationStore? _conversationStore;
    private readonly AgentComponentRegistryHub? _registryHub;
    private readonly IRouteRegistry _routeRegistry;
    private readonly IAgentSharedStateStore _sharedStateStore;
    private readonly IOptions<AgentBlazorOptions> _options;
    private readonly IAgentBlazorTelemetrySink _telemetrySink;
    private readonly IReadOnlyList<IAgentRuntimeEventSubscriber> _runtimeEventSubscribers;
    private readonly IOptions<PromptTracingOptions>? _tracingOptions;
    private readonly IPromptTraceStore? _traceStore;
    private readonly AgentBlazor.Core.Paid.IActionHistoryStore? _actionHistoryStore;
    private readonly IAgentServiceToolRegistry? _serviceToolRegistry;
    private readonly IEnumerable<IMcpToolProvider>? _mcpToolProviders;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IAgentBlazorEntitlementService? _entitlementService;
    private readonly AgentBlazor.Core.Paid.IAgentInspectorStore? _inspectorStore;
    private readonly AgentMiddlewarePipeline? _middlewarePipeline;
    private readonly ILogger<AgentRuntime>? _logger;

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

    private sealed record AllowedComponentPolicyResult(
        IReadOnlyList<AvailableComponent> AllowedComponents,
        IReadOnlyList<string> BlockedByAgentPolicy,
        IReadOnlyList<string> BlockedByTier);

    public AgentRuntime(
        IStructuredActionPlanner planner,
        IPlanValidator validator,
        IPlanExecutor executor,
        IAgentRegistry agentRegistry,
        IComponentCapabilityCatalog componentCatalog,
        IAgentUiToolCatalog uiToolCatalog,
        IConversationStore? conversationStore,
        AgentComponentRegistryHub? registryHub,
        IRouteRegistry routeRegistry,
        IAgentSharedStateStore sharedStateStore,
        IOptions<AgentBlazorOptions> options,
        IAgentBlazorTelemetrySink telemetrySink,
        IEnumerable<IAgentRuntimeEventSubscriber>? runtimeEventSubscribers = null,
        IOptions<PromptTracingOptions>? tracingOptions = null,
        IPromptTraceStore? traceStore = null,
        AgentBlazor.Core.Paid.IActionHistoryStore? actionHistoryStore = null,
        IAgentServiceToolRegistry? serviceToolRegistry = null,
        IEnumerable<IMcpToolProvider>? mcpToolProviders = null,
        IServiceProvider? serviceProvider = null,
        IAgentBlazorEntitlementService? entitlementService = null,
        AgentBlazor.Core.Paid.IAgentInspectorStore? inspectorStore = null,
        AgentMiddlewarePipeline? middlewarePipeline = null,
        ILogger<AgentRuntime>? logger = null)
    {
        _planner = planner;
        _validator = validator;
        _executor = executor;
        _agentRegistry = agentRegistry;
        _componentCatalog = componentCatalog;
        _uiToolCatalog = uiToolCatalog;
        _conversationStore = conversationStore;
        _registryHub = registryHub;
        _routeRegistry = routeRegistry;
        _sharedStateStore = sharedStateStore;
        _options = options;
        _telemetrySink = telemetrySink;
        _runtimeEventSubscribers = runtimeEventSubscribers?.ToArray() ?? [];
        _tracingOptions = tracingOptions;
        _traceStore = traceStore;
        _actionHistoryStore = actionHistoryStore;
        _serviceToolRegistry = serviceToolRegistry;
        _mcpToolProviders = mcpToolProviders;
        _serviceProvider = serviceProvider;
        _entitlementService = entitlementService;
        _inspectorStore = inspectorStore;
        _middlewarePipeline = middlewarePipeline;
        _logger = logger;
    }

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_middlewarePipeline is { HasMiddlewares: true })
        {
            return _middlewarePipeline.RunAsync(
                request,
                (req, ct) => RunTurnCoreAsync(req, emitEvent: null, ct),
                cancellationToken);
        }

        return RunTurnCoreAsync(request, emitEvent: null, cancellationToken);
    }

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
                if (replay.Count > 0) lastReplaySequence = replay[^1].Sequence;
                completed = active.Completed;
            }

            foreach (var streamEvent in replay) yield return streamEvent;

            if (completed) yield break;

            await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (streamEvent.Sequence <= lastReplaySequence) continue;
                yield return streamEvent with { IsReplay = true };
            }

            yield break;
        }

        if (_completedStreamingRuns.TryGetValue(runId, out var completedRun))
        {
            foreach (var streamEvent in completedRun.Events)
                yield return streamEvent with { IsReplay = true };
        }
    }

    public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (!_activeStreamingRuns.TryGetValue(runId, out var runState))
            return Task.FromResult(false);

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
            throw new ArgumentException("User message is required.", nameof(request));

        var stopwatch = Stopwatch.StartNew();
        var traceBuilder = new PromptTraceBuilder(_tracingOptions);
        var sessionId = request.GetEffectiveSessionId();
        var runId = GetContextRunId(request.Context);
        var sharedStateRunId = string.IsNullOrWhiteSpace(runId)
            ? Guid.NewGuid().ToString("N")
            : runId!;
        var inspectorRunId = Guid.NewGuid().ToString("N");
        var inspectorStartedAt = DateTimeOffset.UtcNow;
        var inspectorEvents = new List<AgentBlazor.Core.Paid.InspectorEvent>();
        ActionPlan? inspectorPlan = null;
        var conversationSessionId = sessionId;
        void AppendInspectorEvent(
            string kind,
            string? detail = null,
            string? componentId = null,
            string? actionId = null)
        {
            inspectorEvents.Add(new AgentBlazor.Core.Paid.InspectorEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Kind: kind,
                ComponentId: componentId,
                ActionId: actionId,
                Detail: detail));
        }

        // Resolve the per-circuit registry for this session
        IAgentComponentRegistry? registry = null;
        _registryHub?.TryGet(sessionId, out registry);
        var mountedComponents = GetMountedComponents(registry);
        var currentRoute = ResolveCurrentRoute(request.Context, mountedComponents);
        AppendInspectorEvent("RunStarted", $"User message: {request.UserMessage}");
        if (!string.IsNullOrWhiteSpace(currentRoute))
        {
            AppendInspectorEvent("CurrentRoute", currentRoute);
        }

        var registration = ResolveAgent(request.AgentName, request.Context, currentRoute);
        if (registration is not null)
        {
            AppendInspectorEvent("AgentResolved", registration.Name);
        }
        else
        {
            AppendInspectorEvent("AgentResolutionFailed", BuildNoAgentReasonDetail(request.AgentName, request.Context, currentRoute));
        }

        if (request.Context is not null &&
            request.Context.TryGetValue(AgentRuntimeContextKeys.AgentHandoffFrom, out var handoffFrom) &&
            !string.IsNullOrWhiteSpace(handoffFrom) &&
            request.Context.TryGetValue(AgentRuntimeContextKeys.AgentHandoffTo, out var handoffTo) &&
            !string.IsNullOrWhiteSpace(handoffTo))
        {
            var handoffAt = request.Context.TryGetValue(AgentRuntimeContextKeys.AgentHandoffAt, out var rawHandoffAt)
                ? rawHandoffAt
                : null;
            var handoffDetail = string.IsNullOrWhiteSpace(handoffAt)
                ? $"{handoffFrom} -> {handoffTo}"
                : $"{handoffFrom} -> {handoffTo} @ {handoffAt}";
            AppendInspectorEvent("AgentHandoff", handoffDetail);
        }

        traceBuilder.RecordEntry(request, registration?.Name);

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.RunStarted,
            AgentName = registration?.Name ?? "none"
        });
        await NotifyTurnStartedAsync(
            sessionId,
            runId,
            registration?.Name ?? "none",
            request.UserMessage,
            CancellationToken.None);

        if (registration is null)
        {
            var noAgentResponse = await HandleNoAgentAsync(
                traceBuilder,
                request.AgentName,
                request.Context,
                cancellationToken);
            AppendInspectorEvent("RunFinished", noAgentResponse.ResponseText);
            await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, noAgentResponse, cancellationToken);
            await EmitTextDeltasAsync(noAgentResponse.AgentName, noAgentResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(noAgentResponse, emitEvent);
            RecordInspectorRun(
                inspectorRunId,
                sessionId,
                noAgentResponse.AgentName,
                inspectorStartedAt,
                plan: null,
                executionResults: [],
                events: inspectorEvents,
                succeeded: false,
                errorMessage: noAgentResponse.ResponseText);
            await NotifyTurnFinishedAsync(
                sessionId,
                runId,
                noAgentResponse.AgentName,
                request.UserMessage,
                noAgentResponse,
                CancellationToken.None);
            return noAgentResponse;
        }

        conversationSessionId = ResolveConversationSessionId(sessionId, registration.Name, request);
        var conversationHistory = await BuildConversationHistoryAsync(conversationSessionId, cancellationToken);
        AppendInspectorEvent("ConversationHydrated", $"Turns loaded: {conversationHistory.Count}");
        var allowedPolicy = GetAllowedComponents(registration);
        var allowedComponents = allowedPolicy.AllowedComponents;
        // Augment with any custom [AgentAction] components not already in the catalog
        allowedComponents = AugmentAllowedWithMounted(allowedComponents, mountedComponents);
        AppendInspectorEvent(
            "CapabilitiesReady",
            $"Allowed components: {allowedComponents.Count}, blocked-by-policy: {allowedPolicy.BlockedByAgentPolicy.Count}, blocked-by-tier: {allowedPolicy.BlockedByTier.Count}");
        var sharedStateBase = new Dictionary<string, string>(
            _sharedStateStore.GetSnapshot(registration.Name, sessionId).Values,
            StringComparer.OrdinalIgnoreCase);
        ApplyContextSharedStateSnapshot(sharedStateBase, request.Context);
        ApplyContextSharedStateDelta(sharedStateBase, request.Context);
        var latestSharedState = BuildSharedStateSnapshot(
            sharedStateBase,
            mountedComponents,
            currentRoute);
        AppendInspectorEvent("StateSnapshot", SerializeInspectorPayload(latestSharedState));
        _sharedStateStore.SaveSnapshot(registration.Name, sessionId, sharedStateRunId, latestSharedState);
        await EmitSharedStateSnapshotAsync(registration.Name, latestSharedState, emitEvent);
        var providerConfigured = _planner.IsProviderConfigured;

        await TrackStartedAsync(registration.Name, request, providerConfigured);

        if (allowedComponents.Count == 0)
        {
            var policyBlockedMessage = BuildNoAllowedActionsResponseText(
                allowedPolicy.BlockedByAgentPolicy,
                allowedPolicy.BlockedByTier);
            AppendInspectorEvent("PolicyBlocked", policyBlockedMessage);

            if (traceBuilder.IsEnabled)
            {
                traceBuilder.RecordFailure(policyBlockedMessage);
                await StoreTraceAsync(traceBuilder, cancellationToken);
            }

            var policyBlockedResponse = new AgentTurnResponse(
                AgentName: registration.Name,
                ResponseText: policyBlockedMessage,
                PlannedActions: [],
                ExecutionResults: []);
            await TrackFinishedAsync(
                registration.Name,
                request,
                AgentBlazorRunOutcome.Failed,
                plannedCount: 0,
                executedCount: 0,
                providerConfigured: providerConfigured);
            await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, policyBlockedResponse, cancellationToken);
            await EmitTextDeltasAsync(registration.Name, policyBlockedResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(policyBlockedResponse, emitEvent);
            RecordInspectorRun(
                inspectorRunId,
                sessionId,
                registration.Name,
                inspectorStartedAt,
                plan: null,
                executionResults: [],
                events: inspectorEvents,
                succeeded: false,
                errorMessage: policyBlockedResponse.ResponseText);
            await NotifyTurnFinishedAsync(
                sessionId,
                runId,
                registration.Name,
                request.UserMessage,
                policyBlockedResponse,
                CancellationToken.None);
            return policyBlockedResponse;
        }

        if (!providerConfigured)
        {
            var providerMissingResponse = await BuildProviderMissingResponseAsync(
                registration.Name, traceBuilder, cancellationToken);
            AppendInspectorEvent("ProviderMissing", providerMissingResponse.ResponseText);

            await TrackFinishedAsync(registration.Name, request, AgentBlazorRunOutcome.ProviderMissing,
                plannedCount: 0, executedCount: 0, providerConfigured: false);
            await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, providerMissingResponse, cancellationToken);
            await EmitTextDeltasAsync(providerMissingResponse.AgentName, providerMissingResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(providerMissingResponse, emitEvent);
            RecordInspectorRun(
                inspectorRunId,
                sessionId,
                registration.Name,
                inspectorStartedAt,
                plan: null,
                executionResults: [],
                events: inspectorEvents,
                succeeded: false,
                errorMessage: providerMissingResponse.ResponseText);
            await NotifyTurnFinishedAsync(
                sessionId,
                runId,
                providerMissingResponse.AgentName,
                request.UserMessage,
                providerMissingResponse,
                CancellationToken.None);
            return providerMissingResponse;
        }

        try
        {
            // PHASE 1: PLAN
            _logger?.LogInformation("Planning: {Request}", request.UserMessage);
            AppendInspectorEvent("PlanningStarted");

            var availableRoutes = _routeRegistry.GetAll()
                .Select(r => new AvailableRoute { Path = r.Path, Description = r.Description, Aliases = r.Aliases })
                .ToList();

            // Gather service tools from registry + MCP providers
            var serviceTools = await GatherServiceToolsAsync(cancellationToken);

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
                SharedState = latestSharedState,
                AvailableRoutes = availableRoutes,
                AgentInstructions = registration.Instructions,
                CurrentRoute = currentRoute,
                ServiceTools = serviceTools
            };

            var plan = await _planner.PlanAsync(planRequest, cancellationToken);
            inspectorPlan = plan;

            // Resolve agentId-based steps to canonical component types for validation
            plan = ResolveComponentTypes(plan, mountedComponents, allowedComponents);
            plan = EnforceGeneratedUiActionPolicies(plan, request.GeneratedUiAction, request.UserMessage);
            inspectorPlan = plan;
            AppendInspectorEvent("PlanningFinished", $"Steps: {plan.Steps.Count}, Clarification: {plan.RequiresClarification}");
            foreach (var step in plan.Steps)
            {
                AppendInspectorEvent(
                    "PlannedAction",
                    SerializeInspectorPayload(step.Arguments),
                    step.ComponentId,
                    step.ActionId);
            }

            // Determine response text from the plan's message or build one
            var planMessage = plan.Message;

            if (plan.RequiresClarification &&
                TryRecoverSingleFieldFormEditPlan(
                    request.UserMessage,
                    mountedComponents,
                    allowedComponents,
                    plan,
                    out var recoveredPlan))
            {
                plan = recoveredPlan;
                inspectorPlan = recoveredPlan;
                planMessage = recoveredPlan.Message;

                var recoveredStep = recoveredPlan.Steps[0];
                AppendInspectorEvent(
                    "ClarificationAutoRecovered",
                    $"Recovered as {recoveredStep.ComponentId}.{recoveredStep.ActionId}",
                    recoveredStep.ComponentId,
                    recoveredStep.ActionId);
                AppendInspectorEvent(
                    "PlannedAction",
                    SerializeInspectorPayload(recoveredStep.Arguments),
                    recoveredStep.ComponentId,
                    recoveredStep.ActionId);
            }

            if (plan.RequiresClarification)
            {
                _logger?.LogInformation("Clarification needed: {Question}", plan.ClarificationNeeded);
                var clarificationText = plan.ClarificationNeeded!;
                AppendInspectorEvent("ClarificationRequired", clarificationText);
                var clarificationResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: clarificationText,
                    PlannedActions: [],
                    ExecutionResults: []), plan);
                await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, clarificationResponse, cancellationToken);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ClarificationRequired,
                    AgentName = registration.Name,
                    ClarificationQuestion = clarificationText
                });
                await EmitTextDeltasAsync(registration.Name, planMessage ?? clarificationText, emitEvent);
                await EmitRunFinishedAsync(clarificationResponse, emitEvent);
                RecordInspectorRun(
                    inspectorRunId,
                    sessionId,
                    registration.Name,
                    inspectorStartedAt,
                    inspectorPlan,
                    executionResults: [],
                    events: inspectorEvents,
                    succeeded: false,
                    errorMessage: clarificationText);
                await NotifyTurnFinishedAsync(
                    sessionId,
                    runId,
                    registration.Name,
                    request.UserMessage,
                    clarificationResponse,
                    CancellationToken.None);
                return clarificationResponse;
            }

            if (plan.IsEmpty)
            {
                _logger?.LogInformation("Plan is empty — no actions");
                var emptyText = planMessage ?? "I understood your request but no actions are needed.";
                AppendInspectorEvent("PlanEmpty", emptyText);
                var emptyResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: emptyText,
                    PlannedActions: [],
                    ExecutionResults: []), plan);
                await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, emptyResponse, cancellationToken);
                await EmitTextDeltasAsync(registration.Name, emptyText, emitEvent);
                await EmitRunFinishedAsync(emptyResponse, emitEvent);
                RecordInspectorRun(
                    inspectorRunId,
                    sessionId,
                    registration.Name,
                    inspectorStartedAt,
                    inspectorPlan,
                    executionResults: [],
                    events: inspectorEvents,
                    succeeded: true,
                    errorMessage: null);
                await NotifyTurnFinishedAsync(
                    sessionId,
                    runId,
                    registration.Name,
                    request.UserMessage,
                    emptyResponse,
                    CancellationToken.None);
                return emptyResponse;
            }

            _logger?.LogInformation("Plan has {StepCount} steps", plan.Steps.Count);

            var plannedActions = CreatePlannedActions(plan);
            await EmitPlannedActionsAsync(plannedActions, emitEvent);
            await NotifyToolExecutionStartedAsync(
                sessionId,
                runId,
                registration.Name,
                plannedActions,
                CancellationToken.None);

            var runtimeContext = request.Context is null
                ? null
                : new Dictionary<string, string>(request.Context, StringComparer.OrdinalIgnoreCase);
            var approvedActions = GetApprovedActions(plan, allowedComponents, runtimeContext);
            var pendingApprovals = GetPendingApprovals(plan, allowedComponents, approvedActions);
            if (pendingApprovals.Count > 0)
            {
                AppendInspectorEvent("ApprovalRequired", SerializeInspectorPayload(pendingApprovals));
            }

            if (pendingApprovals.Count > 0)
            {
                var blockedResults = pendingApprovals
                    .Select(static p => new ComponentActionExecutionResult(
                        p.ComponentId, p.ActionId,
                        Outcome: ActionOutcome.Blocked,
                        Message: $"Approval required for {p.ComponentId}.{p.ActionId}."))
                    .ToArray();
                var approvalText = BuildApprovalRequiredResponseText(pendingApprovals);

                if (traceBuilder.IsEnabled)
                {
                    traceBuilder.RecordPlanning(plannedActions, allowedComponents.Count)
                        .RecordExecution(blockedResults)
                        .RecordSuccess(approvalText);
                    await StoreTraceAsync(traceBuilder, cancellationToken);
                }

                var approvalResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: approvalText,
                    PlannedActions: plannedActions,
                    ExecutionResults: blockedResults)
                {
                    RequiresApproval = true,
                    PendingApprovals = pendingApprovals
                }, plan);
                await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, approvalResponse, cancellationToken);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ApprovalRequired,
                    AgentName = registration.Name,
                    PendingApprovals = pendingApprovals
                });
                await EmitExecutionResultsAsync(blockedResults, emitEvent);
                await EmitTextDeltasAsync(registration.Name, approvalText, emitEvent);
                await EmitRunFinishedAsync(approvalResponse, emitEvent);
                RecordInspectorRun(
                    inspectorRunId,
                    sessionId,
                    registration.Name,
                    inspectorStartedAt,
                    inspectorPlan,
                    blockedResults,
                    inspectorEvents,
                    succeeded: false,
                    errorMessage: approvalText);
                await NotifyTurnFinishedAsync(
                    sessionId,
                    runId,
                    registration.Name,
                    request.UserMessage,
                    approvalResponse,
                    CancellationToken.None);
                return approvalResponse;
            }

            // PHASE 2: VALIDATE
            _logger?.LogInformation("Validating plan");
            AppendInspectorEvent("ValidationStarted");

            var validationContext = new PlanValidationContext
            {
                AllowedComponents = allowedComponents,
                MountedComponents = mountedComponents,
                ApprovedActions = approvedActions
            };

            var validationResult = _validator.Validate(plan, validationContext);

            if (!validationResult.IsValid)
            {
                var validationFailures = BuildValidationFailureResults(validationResult);
                var clarification = validationFailures
                    .Select(static f => f.Message)
                    .FirstOrDefault(static message =>
                        message.Contains("Current tier:", StringComparison.OrdinalIgnoreCase))
                    ?? validationResult.BuildClarificationQuestion()
                    ?? "The plan could not be validated.";
                AppendInspectorEvent("ValidationFailed", SerializeInspectorPayload(validationFailures));

                if (traceBuilder.IsEnabled)
                {
                    traceBuilder.RecordPlanning(plannedActions, allowedComponents.Count)
                        .RecordExecution(validationFailures)
                        .RecordSuccess(clarification);
                    await StoreTraceAsync(traceBuilder, cancellationToken);
                }

                var validationResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: clarification,
                    PlannedActions: [],
                    ExecutionResults: validationFailures), plan);
                await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, validationResponse, cancellationToken);
                await EmitExecutionResultsAsync(validationFailures, emitEvent);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ClarificationRequired,
                    AgentName = registration.Name,
                    ClarificationQuestion = clarification
                });
                await EmitTextDeltasAsync(registration.Name, clarification, emitEvent);
                await EmitRunFinishedAsync(validationResponse, emitEvent);
                RecordInspectorRun(
                    inspectorRunId,
                    sessionId,
                    registration.Name,
                    inspectorStartedAt,
                    inspectorPlan,
                    validationFailures,
                    inspectorEvents,
                    succeeded: false,
                    errorMessage: clarification);
                await NotifyTurnFinishedAsync(
                    sessionId,
                    runId,
                    registration.Name,
                    request.UserMessage,
                    validationResponse,
                    CancellationToken.None);
                return validationResponse;
            }
            AppendInspectorEvent("ValidationPassed");

            // PHASE 3: EXECUTE
            _logger?.LogInformation("Executing plan");
            AppendInspectorEvent("ExecutionStarted");

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

            // Partition plan steps: service tool steps vs component steps
            var toolSteps = plan.Steps.Where(s => string.Equals(s.ComponentId, "tool", StringComparison.OrdinalIgnoreCase)).ToList();
            var componentPlan = toolSteps.Count > 0
                ? plan with { Steps = plan.Steps.Where(s => !string.Equals(s.ComponentId, "tool", StringComparison.OrdinalIgnoreCase)).ToList() }
                : plan;

            var executionResult = await _executor.ExecuteAsync(componentPlan, executionOptions, cancellationToken);

            // Execute service tool steps
            var toolResults = new List<ComponentActionExecutionResult>();
            foreach (var toolStep in toolSteps)
            {
                var toolResult = await ExecuteServiceToolAsync(toolStep, cancellationToken);
                toolResults.Add(toolResult);
            }

            var executionResults = executionResult.StepResults
                .Select(r => new ComponentActionExecutionResult(
                    r.Step.ComponentId, r.Step.ActionId, r.Outcome, r.Message))
                .Concat(toolResults)
                .ToArray();
            await EmitExecutionResultsAsync(executionResults, emitEvent);
            await NotifyToolExecutionFinishedAsync(
                sessionId,
                runId,
                registration.Name,
                executionResults,
                CancellationToken.None);

            var mountedComponentsAfterExecution = GetMountedComponents(registry);
            var currentRouteAfterExecution = NormalizeRoutePath(ExtractCurrentRoute(mountedComponentsAfterExecution)) ?? currentRoute;
            var sharedStateAfterExecution = BuildSharedStateSnapshot(
                latestSharedState,
                mountedComponentsAfterExecution,
                currentRouteAfterExecution);
            var sharedStateDelta = BuildSharedStateDelta(latestSharedState, sharedStateAfterExecution);
            if (sharedStateDelta.Count > 0)
            {
                _sharedStateStore.ApplyDelta(registration.Name, sessionId, sharedStateRunId, sharedStateDelta);
                await EmitSharedStateDeltaAsync(registration.Name, sharedStateDelta, emitEvent);
                latestSharedState = sharedStateAfterExecution;
                await EmitSharedStateSnapshotAsync(registration.Name, latestSharedState, emitEvent);
                AppendInspectorEvent("StateDelta", SerializeInspectorPayload(sharedStateDelta));
                AppendInspectorEvent("StateSnapshot", SerializeInspectorPayload(latestSharedState));
            }

            // When execution fully succeeded, use the LLM's plan message if available.
            // When any step failed (NeedsClarification, Failed, etc.), surface the execution message instead.
            var allToolsSucceeded = toolResults.All(r => r.Succeeded);
            var overallSucceeded = executionResult.Succeeded && allToolsSucceeded;
            var responseText = overallSucceeded
                ? (planMessage ?? BuildResponseText(executionResult))
                : BuildResponseText(executionResult);
            stopwatch.Stop();

            if (traceBuilder.IsEnabled)
            {
                traceBuilder.RecordPlanning(plannedActions, allowedComponents.Count)
                    .RecordExecution(executionResults)
                    .RecordSuccess(responseText);
                await StoreTraceAsync(traceBuilder, cancellationToken);
            }

            await TrackFinishedAsync(registration.Name, request,
                overallSucceeded ? AgentBlazorRunOutcome.Succeeded : AgentBlazorRunOutcome.Failed,
                plannedActions.Length, executionResults.Length, providerConfigured);

            _logger?.LogInformation(
                "Turn completed in {Duration}ms — {Success}/{Total} steps succeeded",
                stopwatch.ElapsedMilliseconds, executionResult.SuccessCount, executionResult.StepResults.Count);

            var successResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                AgentName: registration.Name,
                ResponseText: responseText,
                PlannedActions: plannedActions,
                ExecutionResults: executionResults), plan);
            AppendInspectorEvent("ExecutionFinished", $"Succeeded: {overallSucceeded}");
            AppendInspectorEvent("RunFinished", responseText);
            await StoreConversationTurnAsync(conversationSessionId, request.UserMessage, successResponse, cancellationToken);

            // Record inspector data
            RecordInspectorRun(
                inspectorRunId,
                sessionId,
                registration.Name,
                inspectorStartedAt,
                inspectorPlan,
                executionResults,
                inspectorEvents,
                succeeded: overallSucceeded,
                errorMessage: overallSucceeded ? null : responseText);
            await RecordActionHistoryAsync(
                sessionId,
                request.GetEffectiveUserId(),
                request.UserMessage,
                executionResults,
                plan,
                cancellationToken);
            await EmitReasoningEventsAsync(registration.Name, plan.ReasoningContent, emitEvent);
            await EmitTextDeltasAsync(registration.Name, responseText, emitEvent);
            await EmitRunFinishedAsync(successResponse, emitEvent);
            await NotifyTurnFinishedAsync(
                sessionId,
                runId,
                registration.Name,
                request.UserMessage,
                successResponse,
                CancellationToken.None);
            return successResponse;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            traceBuilder.RecordCanceled();
            await StoreTraceAsync(traceBuilder, CancellationToken.None);
            AppendInspectorEvent("RunCanceled", "Turn canceled.");
            RecordInspectorRun(
                inspectorRunId,
                sessionId,
                registration?.Name ?? "none",
                inspectorStartedAt,
                inspectorPlan,
                executionResults: [],
                events: inspectorEvents,
                succeeded: false,
                errorMessage: "Run canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Turn failed");
            traceBuilder.RecordFailure(ex.Message);
            await StoreTraceAsync(traceBuilder, CancellationToken.None);
            AppendInspectorEvent("RunError", ex.Message);
            RecordInspectorRun(
                inspectorRunId,
                sessionId,
                registration?.Name ?? "none",
                inspectorStartedAt,
                inspectorPlan,
                executionResults: [],
                events: inspectorEvents,
                succeeded: false,
                errorMessage: ex.Message);
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunError,
                AgentName = registration?.Name,
                ErrorMessage = ex.Message
            });
            await NotifyErrorAsync(
                sessionId,
                runId,
                registration?.Name ?? "none",
                request.UserMessage,
                ex.Message,
                CancellationToken.None);
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Registry / component helpers
    // -------------------------------------------------------------------------

    private AgentRegistration? ResolveAgent(
        string? requestedAgentName,
        IDictionary<string, string>? context,
        string? currentRoute)
    {
        var lockRequested = IsAgentLockRequested(context);

        if (TryResolveNamedAgent(requestedAgentName, out var requested))
        {
            return requested;
        }

        if (TryGetContextAgentName(context, out var contextAgentName) &&
            TryResolveNamedAgent(contextAgentName, out var contextAgent))
        {
            return contextAgent;
        }

        if (_options.Value.EnableRouteAgentResolution &&
            TryResolveRouteScopedAgent(currentRoute, out var routeAgent))
        {
            return routeAgent;
        }

        if (lockRequested)
        {
            return null;
        }

        if (_agentRegistry.TryGet(_options.Value.DefaultAgent.Name, out var configuredDefault))
            return configuredDefault;

        return _agentRegistry.GetAll()
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private bool TryResolveNamedAgent(string? candidateName, out AgentRegistration registration)
    {
        registration = null!;
        return !string.IsNullOrWhiteSpace(candidateName) &&
               _agentRegistry.TryGet(candidateName, out registration!);
    }

    private static bool TryGetContextAgentName(
        IDictionary<string, string>? context,
        out string? agentName)
    {
        agentName = null;
        return context is not null &&
               context.TryGetValue(AgentRuntimeContextKeys.AgentName, out agentName) &&
               !string.IsNullOrWhiteSpace(agentName);
    }

    private static bool IsAgentLockRequested(IDictionary<string, string>? context)
    {
        if (context is null ||
            !context.TryGetValue(AgentRuntimeContextKeys.AgentLock, out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        return rawValue.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               rawValue.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveRouteScopedAgent(string? currentRoute, out AgentRegistration registration)
    {
        registration = null!;
        var normalizedRoute = NormalizeRoutePath(currentRoute);
        if (string.IsNullOrWhiteSpace(normalizedRoute))
        {
            return false;
        }

        if (TryResolveAgentFromRouteMetadata(normalizedRoute, out registration))
        {
            return true;
        }

        return TryResolveAgentFromAgentMetadata(normalizedRoute, out registration);
    }

    private bool TryResolveAgentFromRouteMetadata(string currentRoute, out AgentRegistration registration)
    {
        registration = null!;

        var route = _routeRegistry.GetAll()
            .FirstOrDefault(r => string.Equals(
                NormalizeRoutePath(r.Path),
                currentRoute,
                StringComparison.OrdinalIgnoreCase));
        if (route?.Metadata is null || route.Metadata.Count == 0)
        {
            return false;
        }

        if (!TryReadRouteAgentMetadata(route.Metadata, out var routeAgentName))
        {
            return false;
        }

        return TryResolveNamedAgent(routeAgentName, out registration);
    }

    private bool TryResolveAgentFromAgentMetadata(string currentRoute, out AgentRegistration registration)
    {
        registration = null!;
        AgentRegistration? bestMatch = null;
        var bestScore = -1;

        foreach (var candidate in _agentRegistry.GetAll())
        {
            foreach (var routePattern in EnumerateAgentRoutePatterns(candidate))
            {
                if (string.IsNullOrWhiteSpace(routePattern))
                {
                    continue;
                }

                var normalizedPattern = NormalizeRoutePath(routePattern);
                if (string.IsNullOrWhiteSpace(normalizedPattern))
                {
                    continue;
                }

                if (!currentRoute.StartsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = normalizedPattern.Length;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestMatch = candidate;
            }
        }

        if (bestMatch is null)
        {
            return false;
        }

        registration = bestMatch;
        return true;
    }

    private static bool TryReadRouteAgentMetadata(
        IReadOnlyDictionary<string, string> metadata,
        out string? agentName)
    {
        agentName = null;
        foreach (var key in RouteAgentMetadataKeys)
        {
            if (metadata.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                agentName = value.Trim();
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateAgentRoutePatterns(AgentRegistration registration)
    {
        foreach (var key in AgentRouteMetadataKeys)
        {
            if (!registration.Metadata.TryGetValue(key, out var rawValue) ||
                string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            foreach (var token in rawValue.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    yield return token;
                }
            }
        }
    }

    private string ResolveConversationSessionId(
        string sessionId,
        string agentName,
        AgentTurnRequest request)
    {
        if (!_options.Value.IsolateConversationsByAgent)
        {
            return sessionId;
        }

        var hasMultipleAgents = _agentRegistry.GetAll().Count > 1;
        if (!hasMultipleAgents)
        {
            return sessionId;
        }

        var explicitAgentTargeted =
            !string.IsNullOrWhiteSpace(request.AgentName) ||
            (request.Context is not null &&
             request.Context.ContainsKey(AgentRuntimeContextKeys.AgentName)) ||
            IsAgentLockRequested(request.Context);
        if (!explicitAgentTargeted)
        {
            return sessionId;
        }

        return AgentConversationScope.BuildSessionKey(sessionId, agentName, isolateByAgent: true);
    }

    private static string? ResolveCurrentRoute(
        IDictionary<string, string>? context,
        IReadOnlyList<MountedComponentState> mountedComponents)
    {
        if (context is not null &&
            context.TryGetValue(AgentRuntimeContextKeys.CurrentRoute, out var contextRoute) &&
            !string.IsNullOrWhiteSpace(contextRoute))
        {
            return NormalizeRoutePath(contextRoute);
        }

        return NormalizeRoutePath(ExtractCurrentRoute(mountedComponents));
    }

    private static string? NormalizeRoutePath(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var normalized = route.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
        {
            normalized = absolute.AbsolutePath;
        }
        else
        {
            var queryOrFragmentIndex = normalized.IndexOfAny(['?', '#']);
            if (queryOrFragmentIndex >= 0)
            {
                normalized = normalized[..queryOrFragmentIndex];
            }
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        normalized = normalized.TrimEnd('/');
        return normalized.Length == 0
            ? "/"
            : normalized.ToLowerInvariant();
    }

    private AllowedComponentPolicyResult GetAllowedComponents(AgentRegistration registration)
    {
        var components = _componentCatalog.GetComponents();
        var agentPolicyEvaluation = ComponentActionPolicy.EvaluateAllowedCapabilities(
            components, registration.AllowedComponents, registration.AllowedActions);
        var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
        var tierFiltered = new List<ComponentCapability>();
        var blockedByTier = new List<string>();

        foreach (var component in agentPolicyEvaluation.AllowedComponents)
        {
            var componentCopy = new ComponentCapability(component.ComponentId, component.Description);
            foreach (var action in component.Actions)
            {
                var requiredTier = AgentComponentTierBoundaries.GetRequiredTier(component.ComponentId, action.ActionId);
                if (effectiveTier < requiredTier)
                {
                    blockedByTier.Add(ComponentActionPolicy.ToActionKey(component.ComponentId, action.ActionId));
                    continue;
                }

                componentCopy.UpsertAction(action);
            }

            if (componentCopy.Actions.Count > 0)
            {
                tierFiltered.Add(componentCopy);
            }
        }

        // Catalog-sourced components drive validation and approval-gating.
        // Parameters here are for the validator only; the prompt uses mounted.Actions from discovery.
        var allowedComponents = tierFiltered
            .Select(c => new AvailableComponent
            {
                ComponentId = c.ComponentId,
                Description = c.Description ?? $"{c.ComponentId} component",
                Actions = c.Actions.Select(a => new AvailableAction
                {
                    ActionId = a.ActionId,
                    Description = a.Description ?? a.ActionId,
                    RequiresApproval = a.RequiresApproval,
                    Parameters = GetCatalogActionParameters(c.ComponentId, a.ActionId)
                }).ToList()
            })
            .ToList();

        return new AllowedComponentPolicyResult(
            AllowedComponents: allowedComponents,
            BlockedByAgentPolicy: agentPolicyEvaluation.BlockedActionKeys,
            BlockedByTier: blockedByTier
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    /// <summary>
    /// Returns required-parameter info for catalog built-in actions.
    /// Used only by the validator — the prompt uses [AgentAction] discovery on mounted instances.
    /// </summary>
    private static IReadOnlyList<ActionParameter> GetCatalogActionParameters(string componentId, string actionId)
    {
        return (componentId, actionId) switch
        {
            (AgentComponentCapabilityProfile.AgentNavMenuComponentId,
                AgentComponentCapabilityProfile.NavigationNavigateToActionId) =>
            [
                new ActionParameter { Name = "uri", Type = "string", Required = true, Description = "The URI to navigate to" }
            ],
            (AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridFilterActionId) =>
            [
                new ActionParameter { Name = "column", Type = "string", Required = true },
                new ActionParameter { Name = "operator", Type = "string", Required = true, AllowedValues = ["eq","neq","gt","gte","lt","lte","contains","startswith","endswith","in","notin","isnull","notnull"] }
            ],
            (AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridSortActionId) =>
            [
                new ActionParameter { Name = "column", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridSelectRowActionId) =>
            [
                new ActionParameter { Name = "rowKey", Type = "string", Required = false }
            ],
            (AgentComponentCapabilityProfile.AgentFormComponentId,
                AgentComponentCapabilityProfile.FormSetFieldActionId) =>
            [
                new ActionParameter { Name = "field", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentTabsComponentId,
                AgentComponentCapabilityProfile.TabsSwitchTabActionId) =>
            [
                new ActionParameter { Name = "index", Type = "integer", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentSelectComponentId,
                AgentComponentCapabilityProfile.SelectSetValueActionId) =>
            [
                new ActionParameter { Name = "value", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentAutocompleteComponentId,
                AgentComponentCapabilityProfile.AutocompleteSetQueryActionId) =>
            [
                new ActionParameter { Name = "query", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentAutocompleteComponentId,
                AgentComponentCapabilityProfile.AutocompleteSelectOptionActionId) =>
            [
                new ActionParameter { Name = "value", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentDatePickerComponentId,
                AgentComponentCapabilityProfile.DatePickerSetDateActionId) =>
            [
                new ActionParameter { Name = "date", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentDateRangePickerComponentId,
                AgentComponentCapabilityProfile.DateRangePickerSetRangeActionId) =>
            [
                new ActionParameter { Name = "startDate", Type = "string", Required = true },
                new ActionParameter { Name = "endDate", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentTreeViewComponentId,
                AgentComponentCapabilityProfile.TreeViewExpandActionId or
                AgentComponentCapabilityProfile.TreeViewCollapseActionId or
                AgentComponentCapabilityProfile.TreeViewSelectNodeActionId) =>
            [
                new ActionParameter { Name = "nodeId", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentStepperComponentId,
                AgentComponentCapabilityProfile.StepperGoToStepActionId) =>
            [
                new ActionParameter { Name = "index", Type = "integer", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentCommandBarComponentId,
                AgentComponentCapabilityProfile.CommandBarInvokeCommandActionId) =>
            [
                new ActionParameter { Name = "command", Type = "string", Required = true }
            ],
            (AgentComponentCapabilityProfile.AgentFileUploadComponentId,
                AgentComponentCapabilityProfile.FileUploadAttachActionId or
                AgentComponentCapabilityProfile.FileUploadRemoveActionId) =>
            [
                new ActionParameter { Name = "fileName", Type = "string", Required = true }
            ],
            _ => []
        };
    }

    /// <summary>
    /// Adds any mounted custom components (not already in the catalog) to the allowed list
    /// so that the planner prompt and validator both see their [AgentAction]-discovered capabilities.
    /// </summary>
    private static IReadOnlyList<AvailableComponent> AugmentAllowedWithMounted(
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyList<MountedComponentState> mountedComponents)
    {
        if (mountedComponents.Count == 0) return allowedComponents;

        List<AvailableComponent>? augmented = null;
        var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mounted in mountedComponents)
        {
            if (mounted.Actions.Count == 0) continue;

            // Already covered by the catalog (e.g. "DataGrid" → "AgentDataGrid")?
            var canonicalId = ResolveAllowedComponentId(mounted.ComponentType, allowedComponents);
            if (canonicalId is not null)
            {
                seenTypes.Add(mounted.ComponentType);
                continue;
            }

            // Custom component: add once per type with its discovered actions
            if (!seenTypes.Add(mounted.ComponentType)) continue;

            augmented ??= [..allowedComponents];
            augmented.Add(new AvailableComponent
            {
                ComponentId = mounted.ComponentType,
                Description = $"{mounted.ComponentType} component",
                Actions = mounted.Actions.ToList()
            });
        }

        return augmented ?? allowedComponents;
    }

    private static string? ExtractCurrentRoute(IReadOnlyList<MountedComponentState> mountedComponents)
    {
        var navMenu = mountedComponents.FirstOrDefault(m =>
            string.Equals(m.ComponentType, "NavMenu", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.ComponentType, "AgentNavMenu", StringComparison.OrdinalIgnoreCase));

        return navMenu?.State.TryGetValue("uri", out var uri) == true
            ? uri
            : null;
    }

    private static IReadOnlyList<MountedComponentState> GetMountedComponents(IAgentComponentRegistry? registry)
    {
        if (registry is null) return [];

        return registry.GetAll()
            .Select(c => new MountedComponentState
            {
                AgentId = c.AgentId,
                ComponentType = c.ComponentType,
                // Try attribute discovery first, then fall back to GetCapability() for components
                // that generate actions dynamically (like AgentFormPageBase<TModel>).
                Actions = GetActionsForComponent(c),
                State = c.GetCurrentState()
                    .ToDictionary(kv => kv.Key, kv => FormatMountedStateValue(kv.Value))
            })
            .ToList();
    }

    private static Dictionary<string, string> BuildSharedStateSnapshot(
        IReadOnlyDictionary<string, string> baseState,
        IReadOnlyList<MountedComponentState> mountedComponents,
        string? currentRoute)
    {
        var snapshot = new Dictionary<string, string>(baseState, StringComparer.OrdinalIgnoreCase);

        foreach (var mounted in mountedComponents)
        {
            if (string.IsNullOrWhiteSpace(mounted.AgentId))
            {
                continue;
            }

            var prefix = $"component.{mounted.AgentId}.";
            foreach (var staleKey in snapshot.Keys
                         .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                snapshot.Remove(staleKey);
            }

            snapshot[$"{prefix}type"] = mounted.ComponentType;
            foreach (var (stateKey, stateValue) in mounted.State)
            {
                snapshot[$"{prefix}state.{stateKey}"] = stateValue;
            }
        }

        if (string.IsNullOrWhiteSpace(currentRoute))
        {
            snapshot.Remove("route.current");
        }
        else
        {
            snapshot["route.current"] = currentRoute;
        }

        return snapshot;
    }

    private void ApplyContextSharedStateSnapshot(
        IDictionary<string, string> sharedState,
        IDictionary<string, string>? context)
    {
        if (context is null ||
            !context.TryGetValue(AgentRuntimeContextKeys.SharedStateSnapshot, out var serialized) ||
            string.IsNullOrWhiteSpace(serialized))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
            if (parsed is null)
            {
                return;
            }

            sharedState.Clear();
            foreach (var (key, value) in parsed)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                sharedState[key] = value ?? string.Empty;
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Invalid shared-state snapshot context payload.");
        }
    }

    private void ApplyContextSharedStateDelta(
        IDictionary<string, string> sharedState,
        IDictionary<string, string>? context)
    {
        if (context is null ||
            !context.TryGetValue(AgentRuntimeContextKeys.SharedStateDelta, out var serialized) ||
            string.IsNullOrWhiteSpace(serialized))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(serialized);
            if (parsed is null)
            {
                return;
            }

            foreach (var (key, value) in parsed)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (value is null)
                {
                    sharedState.Remove(key);
                }
                else
                {
                    sharedState[key] = value;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Invalid shared-state delta context payload.");
        }
    }

    private static Dictionary<string, string?> BuildSharedStateDelta(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> current)
    {
        var delta = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in current)
        {
            if (!previous.TryGetValue(key, out var oldValue) ||
                !string.Equals(oldValue, value, StringComparison.Ordinal))
            {
                delta[key] = value;
            }
        }

        foreach (var key in previous.Keys)
        {
            if (!current.ContainsKey(key))
            {
                delta[key] = null;
            }
        }

        return delta;
    }

    private static List<AvailableAction> GetActionsForComponent(IAgentControllable component)
    {
        // First try [AgentAction] attribute discovery
        var discoveredActions = AgentActionDiscovery.GetDiscoveredActions(component);
        if (discoveredActions.Count > 0)
        {
            return discoveredActions
                .Select(static a => new AvailableAction
                {
                    ActionId = a.ActionId,
                    Description = a.Description,
                    RequiresApproval = a.RequiresApproval,
                    Instructions = a.Instructions,
                    Parameters = a.Parameters.Select(static p => new ActionParameter
                    {
                        Name = p.Name,
                        Type = p.Type,
                        Required = p.Required,
                        Description = p.Description,
                        AllowedValues = p.AllowedValues?.ToList()
                    }).ToList()
                }).ToList();
        }

        // Fall back to GetCapability() for components that override it dynamically
        // (e.g., AgentFormPageBase<TModel> which generates actions from model properties)
        try
        {
            var capability = component.GetCapability();
            if (capability.Actions.Count > 0)
            {
                return capability.Actions
                    .Select(static a => new AvailableAction
                    {
                        ActionId = a.ActionId,
                        Description = a.Description,
                        RequiresApproval = a.RequiresApproval,
                        Parameters = ParseInputSchemaToParameters(a.InputSchema)
                    }).ToList();
            }
        }
        catch
        {
            // Keep runtime resilient to capability faults
        }

        return [];
    }

    /// <summary>
    /// Parses a simple input schema string like "(string field [required], string value)"
    /// into structured ActionParameter objects for the planner prompt.
    /// </summary>
    private static List<ActionParameter> ParseInputSchemaToParameters(string? inputSchema) =>
        InputSchemaParameterParser.Parse(inputSchema);

    private static string FormatMountedStateValue(object? value)
    {
        if (value is null) return "null";
        if (value is string s) return s;
        return value switch
        {
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };
    }

    /// <summary>
    /// After planning, the new AgentPlanner emits steps where ComponentId = agentId (instance ID).
    /// This method resolves each step's ComponentId to the canonical component type so validation works.
    /// It also reroutes actions to the correct component when the LLM targets the wrong one.
    /// </summary>
    /// <summary>
    /// Resolves agentId-based component targeting to canonical component types.
    /// This handles the case where the LLM uses an agentId (like "supplier-onboarding")
    /// instead of a component type (like "SupplierOnboardingWorkflow").
    /// </summary>
    private static ActionPlan ResolveComponentTypes(
        ActionPlan plan,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlyList<AvailableComponent> allowedComponents)
    {
        if (plan.Steps.Count == 0) return plan;

        var mountedByAgentId = mountedComponents.ToDictionary(
            static m => m.AgentId, StringComparer.OrdinalIgnoreCase);

        var anyChanged = false;
        var steps = new List<PlannedStep>(plan.Steps.Count);

        foreach (var step in plan.Steps)
        {
            var componentId = step.ComponentId;
            var targetAgentId = step.TargetAgentId;
            var actionId = step.ActionId;
            var stepChanged = false;

            // If componentId looks like an agentId (not a known component type), resolve it
            if (!IsAllowedComponent(componentId, allowedComponents))
            {
                if (mountedByAgentId.TryGetValue(componentId, out var mounted))
                {
                    // Resolve agentId to component type
                    var canonical = ResolveAllowedComponentId(mounted.ComponentType, allowedComponents);
                    if (!string.IsNullOrWhiteSpace(canonical))
                    {
                        targetAgentId ??= componentId;
                        componentId = canonical;

                        // Normalize actionId to match component's actual action ID (camelCase/snake_case)
                        var matchingAction = mounted.Actions.FirstOrDefault(a => ActionIdMatches(a.ActionId, actionId));
                        if (matchingAction is not null)
                        {
                            actionId = matchingAction.ActionId;
                        }

                        stepChanged = true;
                    }
                }
                else
                {
                    // Try resolving by type name directly
                    var canonical = ResolveAllowedComponentId(componentId, allowedComponents);
                    if (!string.IsNullOrWhiteSpace(canonical))
                    {
                        componentId = canonical;
                        stepChanged = true;
                    }
                }
            }

            if (stepChanged)
            {
                anyChanged = true;
                steps.Add(step with { ComponentId = componentId, TargetAgentId = targetAgentId, ActionId = actionId });
            }
            else
            {
                steps.Add(step);
            }
        }

        return anyChanged ? plan with { Steps = steps } : plan;
    }

    private static bool IsAllowedComponent(string componentId, IReadOnlyList<AvailableComponent> allowedComponents)
        => allowedComponents.Any(c => string.Equals(c.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Compares action IDs, handling both camelCase (LLM output) and snake_case (discovery output).
    /// e.g., "setField" matches "set_field"
    /// </summary>
    private static bool ActionIdMatches(string actionA, string actionB)
    {
        if (string.Equals(actionA, actionB, StringComparison.OrdinalIgnoreCase))
            return true;

        // Normalize both to lowercase without underscores for comparison
        var normalizedA = actionA.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        var normalizedB = actionB.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return string.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
    }

    private static string? ResolveAllowedComponentId(string? typeOrId, IReadOnlyList<AvailableComponent> allowedComponents)
    {
        if (string.IsNullOrWhiteSpace(typeOrId)) return null;
        var raw = typeOrId.Trim();

        var exact = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, raw, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.ComponentId;

        var typeName = raw.StartsWith("Agent", StringComparison.OrdinalIgnoreCase) ? raw[5..] : raw;
        var prefixed = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, $"Agent{typeName}", StringComparison.OrdinalIgnoreCase));
        return prefixed?.ComponentId;
    }


    // -------------------------------------------------------------------------
    // Plan post-processing
    // -------------------------------------------------------------------------

    private static ActionPlan EnforceGeneratedUiActionPolicies(
        ActionPlan plan,
        GeneratedUiActionInvocation? generatedUiAction,
        string userMessage)
    {
        if (plan.Steps.Count == 0) return plan;

        if (IsExplicitSubmitIntent(userMessage) || IsExplicitSubmitIntent(generatedUiAction?.ActionId))
            return plan;

        var filtered = plan.Steps
            .Where(s => !(
                string.Equals(s.ComponentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.ActionId, AgentComponentCapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return filtered.Length == plan.Steps.Count ? plan : plan with { Steps = filtered };
    }

    private static bool IsExplicitSubmitIntent(string? text)
        => !string.IsNullOrWhiteSpace(text) && (
            text.Contains("submit", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("finalize", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("send", StringComparison.OrdinalIgnoreCase));

    private static bool TryRecoverSingleFieldFormEditPlan(
        string userMessage,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlyList<AvailableComponent> allowedComponents,
        ActionPlan sourcePlan,
        out ActionPlan recoveredPlan)
    {
        recoveredPlan = sourcePlan;
        if (!sourcePlan.RequiresClarification ||
            sourcePlan.Steps.Count > 0 ||
            !TryParseSingleFieldEdit(userMessage, out var fieldHint, out var value))
        {
            return false;
        }

        foreach (var mounted in mountedComponents)
        {
            var setFieldAction = mounted.Actions.FirstOrDefault(static action =>
                ActionIdMatches(action.ActionId, AgentComponentCapabilityProfile.FormSetFieldActionId));
            if (setFieldAction is null)
            {
                continue;
            }

            var resolvedField = ResolveMountedFormField(fieldHint, mounted.State);
            if (string.IsNullOrWhiteSpace(resolvedField))
            {
                continue;
            }

            var resolvedComponentId = ResolveAllowedComponentId(mounted.ComponentType, allowedComponents)
                                      ?? mounted.ComponentType;
            recoveredPlan = sourcePlan with
            {
                ClarificationNeeded = null,
                Steps =
                [
                    new PlannedStep
                    {
                        ComponentId = resolvedComponentId,
                        ActionId = setFieldAction.ActionId,
                        Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["field"] = resolvedField,
                            ["value"] = value
                        },
                        TargetAgentId = mounted.AgentId
                    }
                ],
                Message = string.IsNullOrWhiteSpace(sourcePlan.Message)
                    ? $"Updated '{resolvedField}'."
                    : sourcePlan.Message
            };

            return true;
        }

        return false;
    }

    private static bool TryParseSingleFieldEdit(
        string userMessage,
        out string fieldHint,
        out string value)
    {
        fieldHint = string.Empty;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var normalized = userMessage.ReplaceLineEndings(" ").Trim();
        var toIndex = normalized.LastIndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (toIndex <= 0 || toIndex + 4 >= normalized.Length)
        {
            return false;
        }

        var left = normalized[..toIndex].Trim();
        var right = normalized[(toIndex + 4)..].Trim();
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var padded = $" {left} ";
        var verbs = new[] { " set ", " update ", " change " };
        var verbIndex = -1;
        string? verbToken = null;
        foreach (var verb in verbs)
        {
            var index = padded.LastIndexOf(verb, StringComparison.OrdinalIgnoreCase);
            if (index > verbIndex)
            {
                verbIndex = index;
                verbToken = verb;
            }
        }

        if (verbIndex < 0 || verbToken is null)
        {
            return false;
        }

        fieldHint = padded[(verbIndex + verbToken.Length)..].Trim();
        value = TrimEditValue(right);
        return !string.IsNullOrWhiteSpace(fieldHint) && !string.IsNullOrWhiteSpace(value);
    }

    private static string TrimEditValue(string rawValue)
    {
        var trimmed = rawValue.Trim().TrimEnd('.', ';');
        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
            (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static string? ResolveMountedFormField(
        string fieldHint,
        IReadOnlyDictionary<string, string> mountedState)
    {
        var fields = ExtractMountedFormFields(mountedState);
        if (fields.Count == 0)
        {
            return null;
        }

        var reducedHint = ReduceFieldHint(fieldHint);
        var resolved = fields.FirstOrDefault(field =>
            FieldHintMatches(fieldHint, field) || FieldHintMatches(reducedHint, field));
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        if (NormalizeFieldText(reducedHint) is "recipe" or "therecipe")
        {
            return fields.FirstOrDefault(static field =>
                string.Equals(field, "Title", StringComparison.OrdinalIgnoreCase));
        }

        if (reducedHint.Contains("duration", StringComparison.OrdinalIgnoreCase) ||
            reducedHint.Contains("time", StringComparison.OrdinalIgnoreCase))
        {
            return fields.FirstOrDefault(field =>
                string.Equals(field, "Minutes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(field, "Duration", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static bool FieldHintMatches(string hint, string fieldName)
    {
        var normalizedHint = NormalizeFieldText(hint);
        var normalizedField = NormalizeFieldText(fieldName);
        if (string.IsNullOrWhiteSpace(normalizedHint) || string.IsNullOrWhiteSpace(normalizedField))
        {
            return false;
        }

        return string.Equals(normalizedHint, normalizedField, StringComparison.Ordinal) ||
               normalizedHint.EndsWith(normalizedField, StringComparison.Ordinal) ||
               normalizedHint.Contains(normalizedField, StringComparison.Ordinal) ||
               normalizedField.Contains(normalizedHint, StringComparison.Ordinal);
    }

    private static string NormalizeFieldText(string value)
        => string.Concat(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    private static string ReduceFieldHint(string hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return string.Empty;
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "field", "form", "value"
        };
        var tokens = hint
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !stopWords.Contains(token))
            .ToArray();
        return tokens.Length == 0 ? hint : string.Join(' ', tokens);
    }

    private static IReadOnlyList<string> ExtractMountedFormFields(IReadOnlyDictionary<string, string> mountedState)
    {
        if (!mountedState.TryGetValue("fields", out var serialized) || string.IsNullOrWhiteSpace(serialized))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(serialized);
            if (parsed is { Length: > 0 })
            {
                return parsed
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        catch
        {
            // Ignore parse failures and fall back below.
        }

        return serialized
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.Trim('"', '\''))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // -------------------------------------------------------------------------
    // Approval / Validation helpers
    // -------------------------------------------------------------------------

    private static IReadOnlySet<string> GetApprovedActions(
        ActionPlan plan,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyDictionary<string, string>? context)
    {
        var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null || context.Count == 0) return approved;

        foreach (var step in plan.Steps)
        {
            var action = allowedComponents
                .FirstOrDefault(c => string.Equals(c.ComponentId, step.ComponentId, StringComparison.OrdinalIgnoreCase))
                ?.Actions.FirstOrDefault(a => string.Equals(a.ActionId, step.ActionId, StringComparison.OrdinalIgnoreCase));

            if (action is null || !action.RequiresApproval) continue;

            if (ComponentActionApprovalPolicy.IsApprovalGranted(step.ComponentId, step.ActionId, context))
                approved.Add($"{step.ComponentId}.{step.ActionId}");
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
            var action = allowedComponents
                .FirstOrDefault(c => string.Equals(c.ComponentId, step.ComponentId, StringComparison.OrdinalIgnoreCase))
                ?.Actions.FirstOrDefault(a => string.Equals(a.ActionId, step.ActionId, StringComparison.OrdinalIgnoreCase));

            if (action is null || !action.RequiresApproval) continue;
            if (approvedActions.Contains($"{step.ComponentId}.{step.ActionId}")) continue;

            pending.Add(new PendingApproval(
                step.ComponentId,
                step.ActionId,
                action.Description,
                step.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)));
        }

        return pending;
    }

    private ComponentActionExecutionResult[] BuildValidationFailureResults(PlanValidationResult validationResult)
        => validationResult.StepResults
            .Where(static s => !s.IsValid)
            .Select(s =>
            {
                var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
                var requiredTier = AgentComponentTierBoundaries.GetRequiredTier(s.Step.ComponentId, s.Step.ActionId);

                var message = effectiveTier < requiredTier
                    ? $"Action '{s.Step.ComponentId}.{s.Step.ActionId}' requires '{requiredTier}' tier. Current tier: {effectiveTier}."
                    : s.MissingParameters.Count > 0
                        ? $"Action '{s.Step.ActionId}' requires '{s.MissingParameters[0]}' parameter."
                        : s.Errors.FirstOrDefault() ?? "Plan validation failed.";
                return new ComponentActionExecutionResult(s.Step.ComponentId, s.Step.ActionId,
                    Outcome: ActionOutcome.NeedsClarification, Message: message);
            })
            .ToArray();

    private static PlannedComponentAction[] CreatePlannedActions(ActionPlan plan)
        => plan.Steps
            .Select(static s => new PlannedComponentAction(
                s.ComponentId, s.ActionId,
                "Planned by AgentPlanner",
                s.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

    private static string BuildApprovalRequiredResponseText(IReadOnlyList<PendingApproval> pending)
        => pending.Count == 1
            ? $"Approval required for {pending[0].ComponentId}.{pending[0].ActionId}."
            : $"Approval required for {pending.Count} actions.";

    private static string BuildResponseText(PlanExecutionResult result)
    {
        if (result.StepResults.Count == 0) return "I understood your request but no actions were required.";

        var failures = result.StepResults.Where(r => r.Outcome is ActionOutcome.Failed).ToList();
        if (failures.Count > 0) return failures[0].Message;

        var clarification = result.StepResults.Where(r => r.Outcome is ActionOutcome.NeedsClarification).ToList();
        if (clarification.Count > 0) return clarification[0].Message;

        var blocked = result.StepResults.Where(r => r.Outcome is ActionOutcome.Blocked).ToList();
        if (blocked.Count > 0) return blocked.Count == 1 ? blocked[0].Message : $"Blocked {blocked.Count} actions pending approval.";

        var applied = result.AppliedCount;
        return applied == 1 ? "Done." : $"Completed {applied} actions.";
    }

    private AgentTurnResponse AttachPlannedGeneratedUi(AgentTurnResponse response, ActionPlan plan)
    {
        if (plan.UiToolCalls.Count == 0) return response;

        var generatedUi = _uiToolCatalog.BuildDocument(plan.UiToolCalls, out var renderErrors);
        if (generatedUi is null)
        {
            if (renderErrors.Count > 0)
                _logger?.LogWarning("Generated UI rendering failed: {Errors}", string.Join("; ", renderErrors));
            return response;
        }

        return response with { GeneratedUi = generatedUi };
    }

    // -------------------------------------------------------------------------
    // Conversation store
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<ConversationTurn>> BuildConversationHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (_conversationStore is null) return [];

        try
        {
            var history = await _conversationStore.GetHistoryAsync(sessionId, cancellationToken);
            if (history is null || history.Turns.Count == 0) return [];

            var plannerTurns = new List<ConversationTurn>(history.Turns.Count * 2);
            foreach (var turn in history.Turns.TakeLast(10))
            {
                if (!string.IsNullOrWhiteSpace(turn.UserMessage))
                    plannerTurns.Add(new ConversationTurn { Role = "user", Content = turn.UserMessage });
                if (!string.IsNullOrWhiteSpace(turn.AgentResponse))
                    plannerTurns.Add(new ConversationTurn { Role = "assistant", Content = turn.AgentResponse });
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
        if (_conversationStore is null) return;

        try
        {
            await _conversationStore.AppendTurnAsync(sessionId, new Conversation.ConversationTurn
            {
                Timestamp = DateTime.UtcNow,
                UserMessage = userMessage,
                AgentResponse = response.ResponseText,
                PlannedActions = response.PlannedActions,
                ExecutionResults = response.ExecutionResults,
                GeneratedUi = response.GeneratedUi
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to store conversation turn for session {SessionId}", sessionId);
        }
    }

    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    private void RecordInspectorRun(
        string runId,
        string sessionId,
        string agentName,
        DateTimeOffset startedAt,
        ActionPlan? plan,
        IReadOnlyList<AgentBlazor.Core.Runtime.Components.ComponentActionExecutionResult> executionResults,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> events,
        bool succeeded,
        string? errorMessage)
    {
        if (_inspectorStore is null) return;

        try
        {
            var allEvents = new List<AgentBlazor.Core.Paid.InspectorEvent>(events);
            foreach (var result in executionResults)
            {
                allEvents.Add(new AgentBlazor.Core.Paid.InspectorEvent(
                    DateTimeOffset.UtcNow,
                    result.Succeeded ? "ToolCallResult" : "ToolCallFailed",
                    result.ComponentId,
                    result.ActionId,
                    result.Message));
            }

            _inspectorStore.RecordRun(new AgentBlazor.Core.Paid.InspectorRunRecord(
                RunId: runId,
                SessionId: sessionId,
                AgentName: agentName,
                StartedAt: startedAt,
                FinishedAt: DateTimeOffset.UtcNow,
                SystemPrompt: plan?.SystemPrompt,
                RawPlanResponse: plan?.RawResponse,
                Events: allEvents,
                Succeeded: succeeded,
                ErrorMessage: errorMessage));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to record inspector run");
        }
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

    // -------------------------------------------------------------------------
    // Service tools
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<AgentServiceTool>> GatherServiceToolsAsync(CancellationToken cancellationToken)
    {
        var registryTools = _serviceToolRegistry?.GetTools() ?? [];

        if (_mcpToolProviders is null)
            return registryTools;

        try
        {
            var mcpResults = await Task.WhenAll(
                _mcpToolProviders.Select(p => p.GetToolsAsync(cancellationToken)));
            var all = registryTools.ToList();
            foreach (var batch in mcpResults)
                all.AddRange(batch);
            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to fetch MCP tools");
            return registryTools;
        }
    }

    internal async Task<ComponentActionExecutionResult> ExecuteServiceToolAsync(
        PlannedStep step,
        CancellationToken cancellationToken)
    {
        AgentServiceTool? tool = null;

        // First try the hand-registered service tool registry
        if (_serviceToolRegistry is not null)
            _serviceToolRegistry.TryGetTool(step.ActionId, out tool);

        // Fall back to MCP providers
        if (tool is null && _mcpToolProviders is not null)
        {
            foreach (var provider in _mcpToolProviders)
            {
                var mcpTools = await provider.GetToolsAsync(cancellationToken);
                tool = mcpTools.FirstOrDefault(t =>
                    string.Equals(t.Name, step.ActionId, StringComparison.OrdinalIgnoreCase));
                if (tool is not null) break;
            }
        }

        if (tool is null)
        {
            return new ComponentActionExecutionResult(
                step.ComponentId, step.ActionId,
                Outcome: ActionOutcome.Failed,
                Message: $"Unknown tool: {step.ActionId}");
        }

        try
        {
            var args = (IReadOnlyDictionary<string, object?>)step.Arguments;
            var sp = _serviceProvider ?? throw new InvalidOperationException("IServiceProvider not available for tool execution.");
            var result = await tool.Handler(args, sp, cancellationToken);
            return new ComponentActionExecutionResult(
                step.ComponentId, step.ActionId,
                Outcome: ActionOutcome.Applied,
                Message: result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Service tool {ToolName} failed", step.ActionId);
            return new ComponentActionExecutionResult(
                step.ComponentId, step.ActionId,
                Outcome: ActionOutcome.Failed,
                Message: $"Tool error: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Streaming infrastructure
    // -------------------------------------------------------------------------

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

    private async Task ExecuteStreamingRunAsync(StreamingRunState runState, AgentTurnRequest request)
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

    private async ValueTask PublishStreamingEventAsync(StreamingRunState runState, AgentTurnStreamEvent streamEvent)
    {
        List<Channel<AgentTurnStreamEvent>> subscribers;
        AgentTurnStreamEvent normalized;

        lock (runState.Gate)
        {
            if (runState.Completed) return;

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
                lock (runState.Gate) { runState.Subscribers.Remove(subscriber); }
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
            await PublishStreamingEventAsync(runState, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.TextMessageEnd });
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
            subscriber.Writer.TryComplete();

        _activeStreamingRuns.TryRemove(runState.RunId, out _);
        _completedStreamingRuns[runState.RunId] = new StreamingRunHistory(runState.RunId, historySnapshot, DateTimeOffset.UtcNow);
        _completedRunOrder.Enqueue(runState.RunId);
        TrimCompletedRunHistoryIfNeeded();
        runState.Cancellation.Dispose();
    }

    private void TrimCompletedRunHistoryIfNeeded()
    {
        while (_completedRunOrder.Count > MaxRetainedRuns && _completedRunOrder.TryDequeue(out var runId))
            _completedStreamingRuns.TryRemove(runId, out _);
    }

    // -------------------------------------------------------------------------
    // Emit helpers
    // -------------------------------------------------------------------------

    private static async ValueTask EmitEventAsync(Func<AgentTurnStreamEvent, ValueTask>? emitEvent, AgentTurnStreamEvent streamEvent)
    {
        if (emitEvent is not null) await emitEvent(streamEvent);
    }

    private static async ValueTask EmitSharedStateSnapshotAsync(
        string? agentName,
        IReadOnlyDictionary<string, string> state,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        if (emitEvent is null)
        {
            return;
        }

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StateSnapshot,
            AgentName = agentName,
            SharedStateSnapshot = state
        });
    }

    private static async ValueTask EmitSharedStateDeltaAsync(
        string? agentName,
        IReadOnlyDictionary<string, string?> delta,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        if (emitEvent is null || delta.Count == 0)
        {
            return;
        }

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.StateDelta,
            AgentName = agentName,
            SharedStateDelta = delta
        });
    }

    private static async ValueTask EmitRunFinishedAsync(AgentTurnResponse response, Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
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
            var action = plannedActions[index];
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.StepStarted, StepIndex = index, PlannedAction = action });
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.ToolCallStart, StepIndex = index, PlannedAction = action });

            if (action.Arguments is { Count: > 0 })
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.ToolCallArgs, StepIndex = index, PlannedAction = action, ToolArguments = action.Arguments });

            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.ToolCallEnd, StepIndex = index, PlannedAction = action });
        }
    }

    private static async ValueTask EmitExecutionResultsAsync(
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        for (var index = 0; index < executionResults.Count; index++)
        {
            var result = executionResults[index];
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.ToolCallResult, StepIndex = index, ExecutionResult = result });
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.StepFinished, StepIndex = index, StepSucceeded = result.Succeeded, ExecutionResult = result });
        }
    }

    private static async ValueTask EmitReasoningEventsAsync(
        string? agentName,
        string? reasoningContent,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        if (emitEvent is null || string.IsNullOrWhiteSpace(reasoningContent)) return;

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ReasoningStart,
            AgentName = agentName
        });

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ReasoningContent,
            AgentName = agentName,
            ReasoningDelta = reasoningContent
        });

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ReasoningEnd,
            AgentName = agentName
        });
    }

    private static async ValueTask EmitTextDeltasAsync(
        string agentName,
        string responseText,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        if (emitEvent is null || string.IsNullOrWhiteSpace(responseText)) return;

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.TextMessageStart, AgentName = agentName });

        var buffer = new StringBuilder();
        foreach (var ch in responseText)
        {
            buffer.Append(ch);
            if (char.IsWhiteSpace(ch))
            {
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.TextMessageContent, AgentName = agentName, TextDelta = buffer.ToString() });
                buffer.Clear();
                await Task.Yield();
            }
        }

        if (buffer.Length > 0)
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.TextMessageContent, AgentName = agentName, TextDelta = buffer.ToString() });

        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent { Kind = AgentTurnStreamEventKind.TextMessageEnd, AgentName = agentName });
    }

    // -------------------------------------------------------------------------
    // Runtime event subscribers
    // -------------------------------------------------------------------------

    private static string? GetContextRunId(IDictionary<string, string>? context)
        => context is not null &&
           context.TryGetValue(RunIdContextKey, out var runId) &&
           !string.IsNullOrWhiteSpace(runId)
            ? runId
            : null;

    private async ValueTask NotifyTurnStartedAsync(
        string sessionId,
        string? runId,
        string agentName,
        string userMessage,
        CancellationToken cancellationToken)
    {
        if (_runtimeEventSubscribers.Count == 0) return;

        var runtimeEvent = new AgentRuntimeTurnStartedEvent(
            SessionId: sessionId,
            RunId: runId,
            AgentName: agentName,
            UserMessage: userMessage,
            OccurredAt: DateTimeOffset.UtcNow);

        foreach (var subscriber in _runtimeEventSubscribers)
        {
            try
            {
                await subscriber.OnTurnStartedAsync(runtimeEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Runtime event subscriber failed for turn start.");
            }
        }
    }

    private async ValueTask NotifyTurnFinishedAsync(
        string sessionId,
        string? runId,
        string agentName,
        string userMessage,
        AgentTurnResponse response,
        CancellationToken cancellationToken)
    {
        if (_runtimeEventSubscribers.Count == 0) return;

        var runtimeEvent = new AgentRuntimeTurnFinishedEvent(
            SessionId: sessionId,
            RunId: runId,
            AgentName: agentName,
            UserMessage: userMessage,
            Response: response,
            OccurredAt: DateTimeOffset.UtcNow);

        foreach (var subscriber in _runtimeEventSubscribers)
        {
            try
            {
                await subscriber.OnTurnFinishedAsync(runtimeEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Runtime event subscriber failed for turn finish.");
            }
        }
    }

    private async ValueTask NotifyToolExecutionStartedAsync(
        string sessionId,
        string? runId,
        string agentName,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        CancellationToken cancellationToken)
    {
        if (_runtimeEventSubscribers.Count == 0 || plannedActions.Count == 0) return;

        for (var index = 0; index < plannedActions.Count; index++)
        {
            var runtimeEvent = new AgentRuntimeToolExecutionStartedEvent(
                SessionId: sessionId,
                RunId: runId,
                AgentName: agentName,
                StepIndex: index,
                Action: plannedActions[index],
                OccurredAt: DateTimeOffset.UtcNow);

            foreach (var subscriber in _runtimeEventSubscribers)
            {
                try
                {
                    await subscriber.OnToolExecutionStartedAsync(runtimeEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Runtime event subscriber failed for tool start {ComponentId}.{ActionId}.",
                        plannedActions[index].ComponentId,
                        plannedActions[index].ActionId);
                }
            }
        }
    }

    private async ValueTask NotifyToolExecutionFinishedAsync(
        string sessionId,
        string? runId,
        string agentName,
        IReadOnlyList<ComponentActionExecutionResult> results,
        CancellationToken cancellationToken)
    {
        if (_runtimeEventSubscribers.Count == 0 || results.Count == 0) return;

        for (var index = 0; index < results.Count; index++)
        {
            var runtimeEvent = new AgentRuntimeToolExecutionFinishedEvent(
                SessionId: sessionId,
                RunId: runId,
                AgentName: agentName,
                StepIndex: index,
                Result: results[index],
                OccurredAt: DateTimeOffset.UtcNow);

            foreach (var subscriber in _runtimeEventSubscribers)
            {
                try
                {
                    await subscriber.OnToolExecutionFinishedAsync(runtimeEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Runtime event subscriber failed for tool finish {ComponentId}.{ActionId}.",
                        results[index].ComponentId,
                        results[index].ActionId);
                }
            }
        }
    }

    private async ValueTask NotifyErrorAsync(
        string sessionId,
        string? runId,
        string agentName,
        string userMessage,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        if (_runtimeEventSubscribers.Count == 0) return;

        var runtimeEvent = new AgentRuntimeErrorEvent(
            SessionId: sessionId,
            RunId: runId,
            AgentName: agentName,
            UserMessage: userMessage,
            ErrorMessage: errorMessage,
            OccurredAt: DateTimeOffset.UtcNow);

        foreach (var subscriber in _runtimeEventSubscribers)
        {
            try
            {
                await subscriber.OnErrorAsync(runtimeEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Runtime event subscriber failed for error event.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Action history recording (Paid tier)
    // -------------------------------------------------------------------------

    private async Task RecordActionHistoryAsync(
        string sessionId,
        string? userId,
        string userMessage,
        ComponentActionExecutionResult[] executionResults,
        ActionPlan plan,
        CancellationToken cancellationToken)
    {
        if (_actionHistoryStore is null) return;

        foreach (var result in executionResults)
        {
            if (!result.Succeeded) continue;

            // Match result back to its planned step to extract args
            var step = plan.Steps.FirstOrDefault(s =>
                string.Equals(s.ActionId, result.ActionId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.ComponentId, result.ComponentId, StringComparison.OrdinalIgnoreCase));

            IReadOnlyDictionary<string, object?> args = step?.Arguments
                ?? (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>();

            try
            {
                await _actionHistoryStore.RecordAsync(new AgentBlazor.Core.Paid.ActionHistoryEntry(
                    SessionId: sessionId,
                    UserId: userId,
                    Timestamp: DateTimeOffset.UtcNow,
                    UserMessage: userMessage,
                    ActionId: result.ActionId,
                    AgentId: result.ComponentId,
                    Args: args),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to record action history for {ActionId}", result.ActionId);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Telemetry / trace
    // -------------------------------------------------------------------------

    private async Task StoreTraceAsync(PromptTraceBuilder traceBuilder, CancellationToken cancellationToken)
    {
        if (_traceStore is null || !traceBuilder.IsEnabled) return;

        try
        {
            var trace = traceBuilder.Build();
            if (trace is not null) await _traceStore.StoreAsync(trace, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to store trace");
        }
    }

    private async Task TrackStartedAsync(string agentName, AgentTurnRequest request, bool providerConfigured)
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

    private async Task TrackFinishedAsync(string agentName, AgentTurnRequest request, AgentBlazorRunOutcome outcome,
        int plannedCount, int executedCount, bool providerConfigured)
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

    // -------------------------------------------------------------------------
    // Response builders
    // -------------------------------------------------------------------------

    private async Task<AgentTurnResponse> HandleNoAgentAsync(
        PromptTraceBuilder traceBuilder,
        string? requestedAgentName,
        IDictionary<string, string>? context,
        CancellationToken cancellationToken)
    {
        var registeredCount = _agentRegistry.GetAll().Count;
        var responseText = BuildNoAgentResponseText(registeredCount, requestedAgentName, context);

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure(responseText);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse("none", responseText, [], []);
    }

    private async Task<AgentTurnResponse> BuildProviderMissingResponseAsync(string agentName, PromptTraceBuilder traceBuilder, CancellationToken cancellationToken)
    {
        const string message =
            "**No AI provider configured.** " +
            "Add one of the following to your `Program.cs`:\n\n" +
            "```csharp\n" +
            "// OpenAI\n" +
            "options.UseOpenAI(apiKey: \"sk-...\", model: \"gpt-4o-mini\");\n\n" +
            "// Azure OpenAI\n" +
            "options.UseAzureOpenAI(endpoint: \"https://...\", deploymentName: \"...\");\n\n" +
            "// Ollama (free, local)\n" +
            "options.UseOllama(model: \"llama3.2\");\n" +
            "```\n\n" +
            "Set your API key via environment variable `OPENAI_API_KEY` or in `appsettings.json`.";

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure("No AI provider configured");
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse(agentName, message, [], []);
    }

    private string BuildNoAllowedActionsResponseText(
        IReadOnlyList<string> blockedByAgentPolicy,
        IReadOnlyList<string> blockedByTier)
    {
        var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
        var allBlocked = blockedByAgentPolicy
            .Concat(blockedByTier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = ComponentActionPolicy.SummarizeBlockedActions(allBlocked);

        if (allBlocked.Length == 0)
        {
            return $"No allowed component actions are available for this agent policy.\n\nCurrent tier: {effectiveTier}";
        }

        return
            "No allowed component actions are available for this agent policy.\n\n" +
            $"Current tier: {effectiveTier}\n" +
            $"Filtered actions: {summary}";
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static bool IsGeneratedUiRequested(IDictionary<string, string>? context)
        => context is not null &&
           context.TryGetValue(AgentGenerativeUiSpec.GenerateUiContextKey, out var raw) &&
           bool.TryParse(raw, out var val) && val;

    private static string BuildNoAgentResponseText(
        int registeredCount,
        string? requestedAgentName,
        IDictionary<string, string>? context)
    {
        if (registeredCount == 0)
        {
            return "No agents are registered.";
        }

        if (!string.IsNullOrWhiteSpace(requestedAgentName))
        {
            return $"Requested agent '{requestedAgentName}' is not registered.";
        }

        if (context is not null &&
            context.TryGetValue(AgentRuntimeContextKeys.AgentName, out var contextAgentName) &&
            !string.IsNullOrWhiteSpace(contextAgentName))
        {
            return $"Requested agent '{contextAgentName}' is not registered.";
        }

        if (IsAgentLockRequested(context))
        {
            if (context is not null &&
                context.TryGetValue(AgentRuntimeContextKeys.CurrentRoute, out var currentRoute) &&
                !string.IsNullOrWhiteSpace(currentRoute))
            {
                return $"No route-locked agent is configured for '{currentRoute}'.";
            }

            return "Agent lock is enabled, but no matching registered agent could be resolved.";
        }

        return "No matching agent could be resolved for this request.";
    }

    private static string BuildNoAgentReasonDetail(
        string? requestedAgentName,
        IDictionary<string, string>? context,
        string? currentRoute)
    {
        if (!string.IsNullOrWhiteSpace(requestedAgentName))
        {
            return $"Requested agent '{requestedAgentName}' was not found.";
        }

        if (context is not null &&
            context.TryGetValue(AgentRuntimeContextKeys.AgentName, out var contextAgentName) &&
            !string.IsNullOrWhiteSpace(contextAgentName))
        {
            return $"Context agent '{contextAgentName}' was not found.";
        }

        if (IsAgentLockRequested(context))
        {
            if (!string.IsNullOrWhiteSpace(currentRoute))
            {
                return $"Agent lock enabled; no route agent for '{currentRoute}'.";
            }

            return "Agent lock enabled with no matching registered agent.";
        }

        return "Agent could not be resolved from request context.";
    }
}
