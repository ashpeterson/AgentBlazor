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
    private readonly IOptions<AgentBlazorOptions> _options;
    private readonly IAgentBlazorTelemetrySink _telemetrySink;
    private readonly IOptions<PromptTracingOptions>? _tracingOptions;
    private readonly IPromptTraceStore? _traceStore;
    private readonly AgentBlazor.Core.Paid.IActionHistoryStore? _actionHistoryStore;
    private readonly IAgentServiceToolRegistry? _serviceToolRegistry;
    private readonly IEnumerable<IMcpToolProvider>? _mcpToolProviders;
    private readonly IServiceProvider? _serviceProvider;
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
        IOptions<AgentBlazorOptions> options,
        IAgentBlazorTelemetrySink telemetrySink,
        IOptions<PromptTracingOptions>? tracingOptions = null,
        IPromptTraceStore? traceStore = null,
        AgentBlazor.Core.Paid.IActionHistoryStore? actionHistoryStore = null,
        IAgentServiceToolRegistry? serviceToolRegistry = null,
        IEnumerable<IMcpToolProvider>? mcpToolProviders = null,
        IServiceProvider? serviceProvider = null,
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
        _options = options;
        _telemetrySink = telemetrySink;
        _tracingOptions = tracingOptions;
        _traceStore = traceStore;
        _actionHistoryStore = actionHistoryStore;
        _serviceToolRegistry = serviceToolRegistry;
        _mcpToolProviders = mcpToolProviders;
        _serviceProvider = serviceProvider;
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
        var conversationHistory = await BuildConversationHistoryAsync(sessionId, cancellationToken);
        var inspectorRunId = Guid.NewGuid().ToString("N");
        var inspectorStartedAt = DateTimeOffset.UtcNow;
        var inspectorEvents = new List<AgentBlazor.Core.Paid.InspectorEvent>();

        // Resolve the per-circuit registry for this session
        IAgentComponentRegistry? registry = null;
        _registryHub?.TryGet(sessionId, out registry);

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

        var allowedComponents = GetAllowedComponents(registration);
        var mountedComponents = GetMountedComponents(registry);
        // Augment with any custom [AgentAction] components not already in the catalog
        allowedComponents = AugmentAllowedWithMounted(allowedComponents, mountedComponents);
        var currentRoute = ExtractCurrentRoute(mountedComponents);
        var providerConfigured = _planner.IsProviderConfigured;

        await TrackStartedAsync(registration.Name, request, providerConfigured);

        if (!providerConfigured)
        {
            var providerMissingResponse = await BuildProviderMissingResponseAsync(
                registration.Name, traceBuilder, cancellationToken);

            await TrackFinishedAsync(registration.Name, request, AgentBlazorRunOutcome.ProviderMissing,
                plannedCount: 0, executedCount: 0, providerConfigured: false);
            await StoreConversationTurnAsync(sessionId, request.UserMessage, providerMissingResponse, cancellationToken);
            await EmitTextDeltasAsync(providerMissingResponse.AgentName, providerMissingResponse.ResponseText, emitEvent);
            await EmitRunFinishedAsync(providerMissingResponse, emitEvent);
            return providerMissingResponse;
        }

        try
        {
            // PHASE 1: PLAN
            _logger?.LogInformation("Planning: {Request}", request.UserMessage);

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
                AvailableRoutes = availableRoutes,
                AgentInstructions = registration.Instructions,
                CurrentRoute = currentRoute,
                ServiceTools = serviceTools
            };

            var plan = await _planner.PlanAsync(planRequest, cancellationToken);

            // Resolve agentId-based steps to canonical component types for validation
            plan = ResolveComponentTypes(plan, mountedComponents, allowedComponents);
            plan = EnforceGeneratedUiActionPolicies(plan, request.GeneratedUiAction, request.UserMessage);

            // Determine response text from the plan's message or build one
            var planMessage = plan.Message;

            if (plan.RequiresClarification)
            {
                _logger?.LogInformation("Clarification needed: {Question}", plan.ClarificationNeeded);
                var clarificationText = plan.ClarificationNeeded!;
                var clarificationResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: clarificationText,
                    PlannedActions: [],
                    ExecutionResults: []), plan);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, clarificationResponse, cancellationToken);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ClarificationRequired,
                    AgentName = registration.Name,
                    ClarificationQuestion = clarificationText
                });
                await EmitTextDeltasAsync(registration.Name, planMessage ?? clarificationText, emitEvent);
                await EmitRunFinishedAsync(clarificationResponse, emitEvent);
                return clarificationResponse;
            }

            if (plan.IsEmpty)
            {
                _logger?.LogInformation("Plan is empty — no actions");
                var emptyText = planMessage ?? "I understood your request but no actions are needed.";
                var emptyResponse = AttachPlannedGeneratedUi(new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: emptyText,
                    PlannedActions: [],
                    ExecutionResults: []), plan);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, emptyResponse, cancellationToken);
                await EmitTextDeltasAsync(registration.Name, emptyText, emitEvent);
                await EmitRunFinishedAsync(emptyResponse, emitEvent);
                return emptyResponse;
            }

            _logger?.LogInformation("Plan has {StepCount} steps", plan.Steps.Count);

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
                await StoreConversationTurnAsync(sessionId, request.UserMessage, approvalResponse, cancellationToken);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ApprovalRequired,
                    AgentName = registration.Name,
                    PendingApprovals = pendingApprovals
                });
                await EmitExecutionResultsAsync(blockedResults, emitEvent);
                await EmitTextDeltasAsync(registration.Name, approvalText, emitEvent);
                await EmitRunFinishedAsync(approvalResponse, emitEvent);
                return approvalResponse;
            }

            // PHASE 2: VALIDATE
            _logger?.LogInformation("Validating plan");

            var validationContext = new PlanValidationContext
            {
                AllowedComponents = allowedComponents,
                MountedComponents = mountedComponents,
                ApprovedActions = approvedActions
            };

            var validationResult = _validator.Validate(plan, validationContext);

            if (!validationResult.IsValid)
            {
                var clarification = validationResult.BuildClarificationQuestion() ?? "The plan could not be validated.";
                var validationFailures = BuildValidationFailureResults(validationResult);

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
                await StoreConversationTurnAsync(sessionId, request.UserMessage, validationResponse, cancellationToken);
                await EmitExecutionResultsAsync(validationFailures, emitEvent);
                await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
                {
                    Kind = AgentTurnStreamEventKind.ClarificationRequired,
                    AgentName = registration.Name,
                    ClarificationQuestion = clarification
                });
                await EmitTextDeltasAsync(registration.Name, clarification, emitEvent);
                await EmitRunFinishedAsync(validationResponse, emitEvent);
                return validationResponse;
            }

            // PHASE 3: EXECUTE
            _logger?.LogInformation("Executing plan");

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
            await StoreConversationTurnAsync(sessionId, request.UserMessage, successResponse, cancellationToken);

            // Record inspector data
            RecordInspectorRun(inspectorRunId, sessionId, registration.Name,
                inspectorStartedAt, plan, executionResults, inspectorEvents, succeeded: overallSucceeded, errorMessage: null);
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
            _logger?.LogError(ex, "Turn failed");
            traceBuilder.RecordFailure(ex.Message);
            await StoreTraceAsync(traceBuilder, CancellationToken.None);
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.RunError,
                AgentName = registration?.Name,
                ErrorMessage = ex.Message
            });
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Registry / component helpers
    // -------------------------------------------------------------------------

    private AgentRegistration? ResolveAgent(string? requestedAgentName)
    {
        if (!string.IsNullOrWhiteSpace(requestedAgentName) &&
            _agentRegistry.TryGet(requestedAgentName, out var requested))
        {
            return requested;
        }

        if (_agentRegistry.TryGet(_options.Value.DefaultAgent.Name, out var configuredDefault))
            return configuredDefault;

        return _agentRegistry.GetAll()
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private IReadOnlyList<AvailableComponent> GetAllowedComponents(AgentRegistration registration)
    {
        var components = _componentCatalog.GetComponents();
        var evaluation = ComponentActionPolicy.EvaluateAllowedCapabilities(
            components, registration.AllowedComponents, registration.AllowedActions);

        // Catalog-sourced components drive validation and approval-gating.
        // Parameters here are for the validator only; the prompt uses mounted.Actions from discovery.
        return evaluation.AllowedComponents
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
    private static List<ActionParameter> ParseInputSchemaToParameters(string? inputSchema)
    {
        if (string.IsNullOrWhiteSpace(inputSchema) || inputSchema == "()")
            return [];

        var result = new List<ActionParameter>();

        // Remove outer parentheses
        var content = inputSchema.Trim();
        if (content.StartsWith('(')) content = content[1..];
        if (content.EndsWith(')')) content = content[..^1];

        // Split by comma (simple parsing, doesn't handle nested commas)
        var parts = content.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;

            // Parse: "type name [required] — description"
            var tokens = part.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (tokens.Length < 2) continue;

            var type = tokens[0];
            var rest = tokens[1];

            // Extract name (first word)
            var spaceIdx = rest.IndexOf(' ');
            var name = spaceIdx > 0 ? rest[..spaceIdx] : rest;
            var suffix = spaceIdx > 0 ? rest[spaceIdx..] : "";

            var required = suffix.Contains("[required]", StringComparison.OrdinalIgnoreCase);
            var descIdx = suffix.IndexOf('—');
            var description = descIdx > 0 ? suffix[(descIdx + 1)..].Trim() : null;

            result.Add(new ActionParameter
            {
                Name = name,
                Type = type,
                Required = required,
                Description = description
            });
        }

        return result;
    }

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
        if (generatedUiAction is null || plan.Steps.Count == 0) return plan;

        if (IsExplicitSubmitIntent(userMessage) || IsExplicitSubmitIntent(generatedUiAction.ActionId))
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

    private static ComponentActionExecutionResult[] BuildValidationFailureResults(PlanValidationResult validationResult)
        => validationResult.StepResults
            .Where(static s => !s.IsValid)
            .Select(static s =>
            {
                var message = s.MissingParameters.Count > 0
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
        ActionPlan plan,
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
                SystemPrompt: plan.SystemPrompt,
                RawPlanResponse: plan.RawResponse,
                Events: allEvents,
                Succeeded: succeeded,
                ErrorMessage: errorMessage));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to record inspector run");
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

    private async Task<AgentTurnResponse> HandleNoAgentAsync(PromptTraceBuilder traceBuilder, CancellationToken cancellationToken)
    {
        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure("No agents registered");
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return new AgentTurnResponse("none", "No agents are registered.", [], []);
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

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static bool IsGeneratedUiRequested(IDictionary<string, string>? context)
        => context is not null &&
           context.TryGetValue(AgentGenerativeUiSpec.GenerateUiContextKey, out var raw) &&
           bool.TryParse(raw, out var val) && val;
}
