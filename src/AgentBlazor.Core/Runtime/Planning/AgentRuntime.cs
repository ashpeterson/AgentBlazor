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
using AgentBlazor.Core.Runtime;
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
// Legacy built-in runtime implementation. Kept temporarily behind IAgentRuntimeAdapter
// while the architecture moves to an external-runtime-backed adapter model.
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
        var inspector = new TurnInspectorCollector();
        ActionPlan? inspectorPlan = null;
        var conversationSessionId = sessionId;

        var turnPreamble = await HandleTurnPreambleAsync(
            request,
            sessionId,
            runId,
            traceBuilder,
            (kind, detail, componentId, actionId) => inspector.Append(kind, detail, componentId, actionId),
            emitEvent);
        var registry = turnPreamble.Registry;
        var mountedComponents = turnPreamble.MountedComponents;
        var currentRoute = turnPreamble.CurrentRoute;
        var registration = turnPreamble.Registration;

        if (registration is null)
        {
            return await HandleNoAgentTurnAsync(
                new EarlyExitTurnContext(
                    sessionId,
                    runId,
                    conversationSessionId,
                    request,
                    inspectorRunId,
                    inspectorStartedAt,
                    inspector.Events,
                    emitEvent,
                    cancellationToken),
                traceBuilder);
        }

        var turnSetup = await HandleTurnSetupPhaseAsync(
            registration,
            sessionId,
            sharedStateRunId,
            request,
            mountedComponents,
            currentRoute,
            (kind, detail) => inspector.Append(kind, detail),
            emitEvent,
            cancellationToken);
        conversationSessionId = turnSetup.ConversationSessionId;
        var conversationHistory = turnSetup.ConversationHistory;
        var allowedPolicy = turnSetup.AllowedPolicy;
        var allowedComponents = turnSetup.AllowedComponents;
        var latestSharedState = turnSetup.LatestSharedState;
        var providerConfigured = turnSetup.ProviderConfigured;

        if (allowedComponents.Count == 0)
        {
            return await HandlePolicyBlockedTurnAsync(
                new EarlyExitTurnContext(
                    sessionId,
                    runId,
                    conversationSessionId,
                    request,
                    inspectorRunId,
                    inspectorStartedAt,
                    inspector.Events,
                    emitEvent,
                    cancellationToken),
                registration.Name,
                traceBuilder,
                allowedPolicy.BlockedByAgentPolicy,
                allowedPolicy.BlockedByTier,
                providerConfigured,
                (kind, detail) => inspector.Append(kind, detail));
        }

        if (!providerConfigured)
        {
            return await HandleProviderMissingTurnAsync(
                new EarlyExitTurnContext(
                    sessionId,
                    runId,
                    conversationSessionId,
                    request,
                    inspectorRunId,
                    inspectorStartedAt,
                    inspector.Events,
                    emitEvent,
                    cancellationToken),
                registration.Name,
                traceBuilder,
                (kind, detail) => inspector.Append(kind, detail));
        }

        try
        {
            var flowResult = await HandleTurnFlowAsync(
                new TurnFlowContext(
                    registration,
                    sessionId,
                    runId,
                    sharedStateRunId,
                    conversationSessionId,
                    request,
                    inspectorRunId,
                    inspectorStartedAt,
                    inspector.Events,
                    traceBuilder,
                    allowedComponents,
                    mountedComponents,
                    conversationHistory,
                    latestSharedState,
                    currentRoute,
                    providerConfigured,
                    registry,
                    stopwatch,
                    inspector,
                    emitEvent,
                    cancellationToken));
            inspectorPlan = flowResult.InspectorPlan;
            return flowResult.Response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await HandleTurnCanceledAsync(
                sessionId,
                inspectorRunId,
                inspectorStartedAt,
                inspectorPlan,
                inspector.Events,
                registration?.Name ?? "none",
                traceBuilder,
                (kind, detail) => inspector.Append(kind, detail));
            throw;
        }
        catch (Exception ex)
        {
            await HandleTurnFailedAsync(
                sessionId,
                runId,
                request.UserMessage,
                inspectorRunId,
                inspectorStartedAt,
                inspectorPlan,
                inspector.Events,
                registration?.Name,
                ex,
                traceBuilder,
                (kind, detail) => inspector.Append(kind, detail),
                emitEvent);
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
        => RuntimeTurnPreflight.TryGetContextAgentName(context, out agentName);

    private static bool IsAgentLockRequested(IDictionary<string, string>? context)
        => RuntimeTurnPreflight.IsAgentLockRequested(context);

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
        var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
        var capabilityPolicy = RuntimeCapabilityPolicy.Evaluate(
            _componentCatalog.GetComponents(),
            registration.AllowedComponents,
            registration.AllowedActions,
            effectiveTier);

        // Catalog-sourced components drive validation and approval-gating.
        // Parameters here are for the validator only; the prompt uses mounted.Actions from discovery.
        var allowedComponents = capabilityPolicy.AllowedCapabilities
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
            BlockedByAgentPolicy: capabilityPolicy.BlockedByAgentPolicy,
            BlockedByTier: capabilityPolicy.BlockedByTier);
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
        var singleMountedActionComponent = mountedComponents
            .Where(static m => m.Actions.Count > 0)
            .ToArray() is var actionable && actionable.Length == 1
                ? actionable[0]
                : null;

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
                    else if (singleMountedActionComponent is not null)
                    {
                        canonical = ResolveAllowedComponentId(singleMountedActionComponent.ComponentType, allowedComponents);
                        if (!string.IsNullOrWhiteSpace(canonical))
                        {
                            componentId = canonical;
                            targetAgentId ??= singleMountedActionComponent.AgentId;

                            var matchingAction = singleMountedActionComponent.Actions
                                .FirstOrDefault(a => ActionIdMatches(a.ActionId, actionId));
                            if (matchingAction is not null)
                            {
                                actionId = matchingAction.ActionId;
                            }

                            stepChanged = true;
                        }
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

    private static PlannedComponentAction[] CreatePlannedActions(ActionPlan plan)
        => plan.Steps
            .Select(static s => new PlannedComponentAction(
                s.ComponentId, s.ActionId,
                "Planned by AgentPlanner",
                s.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

    private AgentUiDocument? BuildPlanGeneratedUi(ActionPlan plan)
        => RuntimeGeneratedUi.BuildDocument(
            _uiToolCatalog,
            plan.UiToolCalls,
            message => _logger?.LogWarning("Generated UI rendering failed: {Errors}", message));

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
            return RuntimeConversationHistory.ToPlannerTurns(history);
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
            await _conversationStore.AppendTurnAsync(
                sessionId,
                RuntimePersistenceRecords.CreateConversationTurn(userMessage, response),
                cancellationToken);
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
            _inspectorStore.RecordRun(RuntimePersistenceRecords.CreateInspectorRunRecord(
                runId,
                sessionId,
                agentName,
                startedAt,
                plan?.SystemPrompt,
                plan?.RawResponse,
                events,
                executionResults,
                succeeded,
                errorMessage));
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
        var response = RuntimeEarlyExitResponses.BuildNoAgentResponse(
            _agentRegistry.GetAll().Count,
            requestedAgentName,
            context);

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure(response.ResponseText);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return response;
    }

    private async Task<TurnPreambleResult> HandleTurnPreambleAsync(
        AgentTurnRequest request,
        string sessionId,
        string? runId,
        PromptTraceBuilder traceBuilder,
        Action<string, string?, string?, string?> appendInspectorEvent,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        IAgentComponentRegistry? registry = null;
        _registryHub?.TryGet(sessionId, out registry);
        var mountedComponents = GetMountedComponents(registry);
        var currentRoute = ResolveCurrentRoute(request.Context, mountedComponents);

        appendInspectorEvent("RunStarted", $"User message: {request.UserMessage}", null, null);
        if (!string.IsNullOrWhiteSpace(currentRoute))
        {
            appendInspectorEvent("CurrentRoute", currentRoute, null, null);
        }

        var registration = ResolveAgent(request.AgentName, request.Context, currentRoute);
        if (registration is not null)
        {
            appendInspectorEvent("AgentResolved", registration.Name, null, null);
        }
        else
        {
            appendInspectorEvent(
                "AgentResolutionFailed",
                BuildNoAgentReasonDetail(request.AgentName, request.Context, currentRoute),
                null,
                null);
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
            appendInspectorEvent("AgentHandoff", handoffDetail, null, null);
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

        return new TurnPreambleResult(registry, mountedComponents, currentRoute, registration);
    }

    private async Task<TurnSetupPhaseResult> HandleTurnSetupPhaseAsync(
        AgentRegistration registration,
        string sessionId,
        string sharedStateRunId,
        AgentTurnRequest request,
        IReadOnlyList<MountedComponentState> mountedComponents,
        string? currentRoute,
        Action<string, string?> appendInspectorEvent,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent,
        CancellationToken cancellationToken)
    {
        var conversationSessionId = ResolveConversationSessionId(sessionId, registration.Name, request);
        var conversationHistory = await BuildConversationHistoryAsync(conversationSessionId, cancellationToken);
        appendInspectorEvent("ConversationHydrated", $"Turns loaded: {conversationHistory.Count}");

        var allowedPolicy = GetAllowedComponents(registration);
        var allowedComponents = allowedPolicy.AllowedComponents;
        allowedComponents = AugmentAllowedWithMounted(allowedComponents, mountedComponents);
        appendInspectorEvent(
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
        appendInspectorEvent("StateSnapshot", SerializeInspectorPayload(latestSharedState));
        _sharedStateStore.SaveSnapshot(registration.Name, sessionId, sharedStateRunId, latestSharedState);
        await EmitSharedStateSnapshotAsync(registration.Name, latestSharedState, emitEvent);

        var providerConfigured = _planner.IsProviderConfigured;
        await TrackStartedAsync(registration.Name, request, providerConfigured);

        return new TurnSetupPhaseResult(
            conversationSessionId,
            conversationHistory,
            allowedPolicy,
            allowedComponents,
            latestSharedState,
            providerConfigured);
    }

    private async Task<AgentTurnResponse> HandleNoAgentTurnAsync(
        EarlyExitTurnContext context,
        PromptTraceBuilder traceBuilder)
    {
        var response = await HandleNoAgentAsync(
            traceBuilder,
            context.Request.AgentName,
            context.Request.Context,
            context.CancellationToken);
        var terminalTurn = new TerminalTurnContext(
            context.SessionId,
            context.RunId,
            context.ConversationSessionId,
            context.Request.UserMessage,
            context.InspectorRunId,
            context.InspectorStartedAt,
            response.AgentName,
            null,
            context.InspectorEvents,
            context.EmitEvent,
            context.CancellationToken);
        await FinalizeTurnResponseAsync(
            terminalTurn,
            response,
            executionResults: [],
            succeeded: false,
            errorMessage: response.ResponseText,
            appendRunFinishedInspectorEvent: true);
        return response;
    }

    private async Task<AgentTurnResponse> BuildProviderMissingResponseAsync(string agentName, PromptTraceBuilder traceBuilder, CancellationToken cancellationToken)
    {
        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure(RuntimeEarlyExitResponses.NoProviderConfiguredTraceMessage);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        return RuntimeEarlyExitResponses.BuildProviderMissingResponse(agentName);
    }

    private async Task<AgentTurnResponse> HandlePolicyBlockedTurnAsync(
        EarlyExitTurnContext context,
        string agentName,
        PromptTraceBuilder traceBuilder,
        IReadOnlyList<string> blockedByAgentPolicy,
        IReadOnlyList<string> blockedByTier,
        bool providerConfigured,
        Action<string, string?> appendInspectorEvent)
    {
        var policyBlockedMessage = BuildNoAllowedActionsResponseText(blockedByAgentPolicy, blockedByTier);
        appendInspectorEvent("PolicyBlocked", policyBlockedMessage);

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordFailure(policyBlockedMessage);
            await StoreTraceAsync(traceBuilder, context.CancellationToken);
        }

        var response = new AgentTurnResponse(
            AgentName: agentName,
            ResponseText: policyBlockedMessage,
            PlannedActions: [],
            ExecutionResults: []);
        var terminalTurn = new TerminalTurnContext(
            context.SessionId,
            context.RunId,
            context.ConversationSessionId,
            context.Request.UserMessage,
            context.InspectorRunId,
            context.InspectorStartedAt,
            agentName,
            null,
            context.InspectorEvents,
            context.EmitEvent,
            context.CancellationToken);
        await TrackFinishedAsync(
            agentName,
            context.Request,
            AgentBlazorRunOutcome.Failed,
            plannedCount: 0,
            executedCount: 0,
            providerConfigured: providerConfigured);
        await FinalizeTurnResponseAsync(
            terminalTurn,
            response,
            executionResults: [],
            succeeded: false,
            errorMessage: response.ResponseText);
        return response;
    }

    private async Task<AgentTurnResponse> HandleProviderMissingTurnAsync(
        EarlyExitTurnContext context,
        string agentName,
        PromptTraceBuilder traceBuilder,
        Action<string, string?> appendInspectorEvent)
    {
        var response = await BuildProviderMissingResponseAsync(agentName, traceBuilder, context.CancellationToken);
        appendInspectorEvent("ProviderMissing", response.ResponseText);
        var terminalTurn = new TerminalTurnContext(
            context.SessionId,
            context.RunId,
            context.ConversationSessionId,
            context.Request.UserMessage,
            context.InspectorRunId,
            context.InspectorStartedAt,
            response.AgentName,
            null,
            context.InspectorEvents,
            context.EmitEvent,
            context.CancellationToken);

        await TrackFinishedAsync(
            agentName,
            context.Request,
            AgentBlazorRunOutcome.ProviderMissing,
            plannedCount: 0,
            executedCount: 0,
            providerConfigured: false);
        await FinalizeTurnResponseAsync(
            terminalTurn,
            response,
            executionResults: [],
            succeeded: false,
            errorMessage: response.ResponseText);
        return response;
    }

    private async Task<PlanningPhaseResult> HandlePlanningPhaseAsync(
        AgentTurnRequest request,
        string sessionId,
        string? registrationInstructions,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlyList<ConversationTurn> conversationHistory,
        IReadOnlyDictionary<string, string> latestSharedState,
        string? currentRoute,
        Action<string, string?, string?, string?> appendInspectorEvent,
        CancellationToken cancellationToken)
    {
        var availableRoutes = _routeRegistry.GetAll()
            .Select(static route => new AvailableRoute
            {
                Path = route.Path,
                Description = route.Description,
                Aliases = route.Aliases
            })
            .ToList();

        var serviceTools = await GatherServiceToolsAsync(cancellationToken);
        var planRequest = RuntimePlanExecution.BuildPlanRequest(
            request,
            sessionId,
            allowedComponents,
            mountedComponents,
            conversationHistory,
            latestSharedState,
            availableRoutes,
            registrationInstructions,
            currentRoute,
            serviceTools);

        var plannerPlan = await _planner.PlanAsync(planRequest, cancellationToken);
        var plan = ResolveComponentTypes(plannerPlan, mountedComponents, allowedComponents);
        plan = EnforceGeneratedUiActionPolicies(plan, request.GeneratedUiAction, request.UserMessage);

        appendInspectorEvent("PlanningFinished", $"Steps: {plan.Steps.Count}, Clarification: {plan.RequiresClarification}", null, null);
        foreach (var step in plan.Steps)
        {
            appendInspectorEvent(
                "PlannedAction",
                SerializeInspectorPayload(step.Arguments),
                step.ComponentId,
                step.ActionId);
        }

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
            planMessage = recoveredPlan.Message;

            var recoveredStep = recoveredPlan.Steps[0];
            appendInspectorEvent(
                "ClarificationAutoRecovered",
                $"Recovered as {recoveredStep.ComponentId}.{recoveredStep.ActionId}",
                recoveredStep.ComponentId,
                recoveredStep.ActionId);
            appendInspectorEvent(
                "PlannedAction",
                SerializeInspectorPayload(recoveredStep.Arguments),
                recoveredStep.ComponentId,
                recoveredStep.ActionId);
        }

        return new PlanningPhaseResult(plan, plan, planMessage);
    }

    private async Task<TurnFlowResult> HandleTurnFlowAsync(TurnFlowContext flow)
    {
        _logger?.LogInformation("Planning: {Request}", flow.Request.UserMessage);
        flow.Inspector.Append("PlanningStarted");

        var planningPhase = await HandlePlanningPhaseAsync(
            flow.Request,
            flow.SessionId,
            flow.Registration.Instructions,
            flow.AllowedComponents,
            flow.MountedComponents,
            flow.ConversationHistory,
            flow.LatestSharedState,
            flow.CurrentRoute,
            (kind, detail, componentId, actionId) => flow.Inspector.Append(kind, detail, componentId, actionId),
            flow.CancellationToken);
        var plan = planningPhase.Plan;
        var inspectorPlan = planningPhase.InspectorPlan;
        var planMessage = planningPhase.PlanMessage;

        if (plan.RequiresClarification)
        {
            return new TurnFlowResult(
                inspectorPlan,
                await HandlePlannerClarificationRequiredAsync(new PlannerTerminalContext(
                    flow.Registration.Name,
                    flow.SessionId,
                    flow.RunId,
                    flow.ConversationSessionId,
                    flow.Request.UserMessage,
                    flow.InspectorRunId,
                    flow.InspectorStartedAt,
                    inspectorPlan,
                    flow.InspectorEvents,
                    (kind, detail) => flow.Inspector.Append(kind, detail),
                    plan,
                    planMessage,
                    flow.EmitEvent,
                    flow.CancellationToken)));
        }

        if (plan.IsEmpty)
        {
            return new TurnFlowResult(
                inspectorPlan,
                await HandleEmptyPlanAsync(new PlannerTerminalContext(
                    flow.Registration.Name,
                    flow.SessionId,
                    flow.RunId,
                    flow.ConversationSessionId,
                    flow.Request.UserMessage,
                    flow.InspectorRunId,
                    flow.InspectorStartedAt,
                    inspectorPlan,
                    flow.InspectorEvents,
                    (kind, detail) => flow.Inspector.Append(kind, detail),
                    plan,
                    planMessage,
                    flow.EmitEvent,
                    flow.CancellationToken)));
        }

        var preExecution = await HandlePreExecutionPhasesAsync(
            new PreExecutionContext(
                flow.Registration.Name,
                flow.SessionId,
                flow.RunId,
                flow.ConversationSessionId,
                flow.Request,
                flow.InspectorRunId,
                flow.InspectorStartedAt,
                inspectorPlan,
                flow.InspectorEvents,
                flow.TraceBuilder,
                flow.AllowedComponents,
                flow.MountedComponents,
                plan,
                (kind, detail) => flow.Inspector.Append(kind, detail),
                flow.EmitEvent,
                flow.CancellationToken));
        if (preExecution.TerminalResponse is not null)
        {
            return new TurnFlowResult(inspectorPlan, preExecution.TerminalResponse);
        }

        var plannedActions = preExecution.PlannedActions;

        _logger?.LogInformation("Executing plan");
        flow.Inspector.Append("ExecutionStarted");

        var executionPhase = await HandleExecutionPhaseAsync(
            flow.Registration.Name,
            flow.SessionId,
            flow.RunId,
            flow.SharedStateRunId,
            flow.Request.Context,
            plan,
            flow.LatestSharedState,
            flow.Registry,
            flow.CurrentRoute,
            (kind, detail) => flow.Inspector.Append(kind, detail),
            flow.EmitEvent,
            flow.CancellationToken);
        var executionResult = executionPhase.ExecutionResult;
        var executionResults = executionPhase.ExecutionResults;
        var toolResults = executionPhase.ToolResults;

        var allToolsSucceeded = toolResults.All(r => r.Succeeded);
        var overallSucceeded = executionResult.Succeeded && allToolsSucceeded;
        var responseText = overallSucceeded
            ? (planMessage ?? RuntimePlanResponses.BuildExecutionResponseText(executionResult))
            : RuntimePlanResponses.BuildExecutionResponseText(executionResult);
        flow.Stopwatch.Stop();
        _logger?.LogInformation(
            "Turn completed in {Duration}ms — {Success}/{Total} steps succeeded",
            flow.Stopwatch.ElapsedMilliseconds,
            executionResult.SuccessCount,
            executionResult.StepResults.Count);

        return new TurnFlowResult(
            inspectorPlan,
            await HandleExecutionCompletedAsync(
                new ExecutionCompletionContext(
                    flow.Registration.Name,
                    flow.SessionId,
                    flow.RunId,
                    flow.ConversationSessionId,
                    flow.Request,
                    flow.InspectorRunId,
                    flow.InspectorStartedAt,
                    inspectorPlan,
                    flow.InspectorEvents,
                    (kind, detail) => flow.Inspector.Append(kind, detail),
                    flow.TraceBuilder,
                    flow.AllowedComponents.Count,
                    plannedActions,
                    plan,
                    executionResult,
                    executionResults,
                    responseText,
                    overallSucceeded,
                    flow.ProviderConfigured,
                    flow.EmitEvent,
                    flow.CancellationToken)));
    }

    private async Task<PreExecutionPhaseResult> HandlePreExecutionPhasesAsync(PreExecutionContext context)
    {
        _logger?.LogInformation("Plan has {StepCount} steps", context.Plan.Steps.Count);

        var plannedActions = CreatePlannedActions(context.Plan);
        await EmitPlannedActionsAsync(plannedActions, context.EmitEvent);
        await NotifyToolExecutionStartedAsync(
            context.SessionId,
            context.RunId,
            context.AgentName,
            plannedActions,
            CancellationToken.None);

        var runtimeContext = context.Request.Context is null
            ? null
            : new Dictionary<string, string>(context.Request.Context, StringComparer.OrdinalIgnoreCase);
        var approvedActions = RuntimePlanApprovals.BuildApprovedActions(context.Plan, context.AllowedComponents, runtimeContext);
        var pendingApprovals = RuntimePlanApprovals.BuildPendingApprovals(context.Plan, context.AllowedComponents, approvedActions);
        if (pendingApprovals.Count > 0)
        {
            context.AppendInspectorEvent("ApprovalRequired", SerializeInspectorPayload(pendingApprovals));
            return new PreExecutionPhaseResult(
                plannedActions,
                await HandleApprovalRequiredAsync(
                    context.AgentName,
                    context.SessionId,
                    context.RunId,
                    context.ConversationSessionId,
                    context.Request.UserMessage,
                    context.InspectorRunId,
                    context.InspectorStartedAt,
                    context.InspectorPlan,
                    context.InspectorEvents,
                    context.TraceBuilder,
                    context.AllowedComponents.Count,
                    plannedActions,
                    pendingApprovals,
                    context.Plan,
                    context.EmitEvent,
                    context.CancellationToken));
        }

        _logger?.LogInformation("Validating plan");
        context.AppendInspectorEvent("ValidationStarted", null);

        var validationContext = RuntimePlanApprovals.BuildValidationContext(
            context.AllowedComponents,
            context.MountedComponents,
            approvedActions);
        var validationResult = _validator.Validate(context.Plan, validationContext);
        if (!validationResult.IsValid)
        {
            return new PreExecutionPhaseResult(
                plannedActions,
                await HandleValidationFailedAsync(
                    context.AgentName,
                    context.SessionId,
                    context.RunId,
                    context.ConversationSessionId,
                    context.Request.UserMessage,
                    context.InspectorRunId,
                    context.InspectorStartedAt,
                    context.InspectorPlan,
                    context.InspectorEvents,
                    context.AppendInspectorEvent,
                    context.TraceBuilder,
                    context.AllowedComponents.Count,
                    plannedActions,
                    validationResult,
                    context.Plan,
                    context.EmitEvent,
                    context.CancellationToken));
        }

        context.AppendInspectorEvent("ValidationPassed", null);
        return new PreExecutionPhaseResult(plannedActions, null);
    }

    private async Task<AgentTurnResponse> HandlePlannerClarificationRequiredAsync(PlannerTerminalContext context)
    {
        _logger?.LogInformation("Clarification needed: {Question}", context.Plan.ClarificationNeeded);
        var clarificationText = context.Plan.ClarificationNeeded!;
        context.AppendInspectorEvent("ClarificationRequired", clarificationText);

        var clarificationResponse = RuntimeTurnResponses.Build(
            context.AgentName,
            clarificationText,
            [],
            [],
            generatedUi: BuildPlanGeneratedUi(context.Plan),
            clarificationQuestion: clarificationText);
        var terminalTurn = new TerminalTurnContext(
            context.SessionId,
            context.RunId,
            context.ConversationSessionId,
            context.UserMessage,
            context.InspectorRunId,
            context.InspectorStartedAt,
            context.AgentName,
            context.InspectorPlan,
            context.InspectorEvents,
            context.EmitEvent,
            context.CancellationToken);
        await EmitEventAsync(context.EmitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ClarificationRequired,
            AgentName = context.AgentName,
            ClarificationQuestion = clarificationText
        });
        await FinalizeTurnResponseAsync(
            terminalTurn,
            clarificationResponse,
            executionResults: [],
            succeeded: false,
            errorMessage: clarificationText,
            textToEmit: context.PlanMessage ?? clarificationText);
        return clarificationResponse;
    }

    private async Task<AgentTurnResponse> HandleEmptyPlanAsync(PlannerTerminalContext context)
    {
        _logger?.LogInformation("Plan is empty — no actions");
        var emptyText = context.PlanMessage ?? "I understood your request but no actions are needed.";
        context.AppendInspectorEvent("PlanEmpty", emptyText);

        var emptyResponse = RuntimeTurnResponses.Build(
            context.AgentName,
            emptyText,
            [],
            [],
            generatedUi: BuildPlanGeneratedUi(context.Plan));
        var terminalTurn = new TerminalTurnContext(
            context.SessionId,
            context.RunId,
            context.ConversationSessionId,
            context.UserMessage,
            context.InspectorRunId,
            context.InspectorStartedAt,
            context.AgentName,
            context.InspectorPlan,
            context.InspectorEvents,
            context.EmitEvent,
            context.CancellationToken);
        await FinalizeTurnResponseAsync(
            terminalTurn,
            emptyResponse,
            executionResults: [],
            succeeded: true,
            errorMessage: null);
        return emptyResponse;
    }

    private async Task<AgentTurnResponse> HandleApprovalRequiredAsync(
        string agentName,
        string sessionId,
        string? runId,
        string conversationSessionId,
        string userMessage,
        string inspectorRunId,
        DateTimeOffset inspectorStartedAt,
        ActionPlan? inspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> inspectorEvents,
        PromptTraceBuilder traceBuilder,
        int allowedComponentCount,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<PendingApproval> pendingApprovals,
        ActionPlan plan,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent,
        CancellationToken cancellationToken)
    {
        var blockedResults = pendingApprovals
            .Select(static approval => new ComponentActionExecutionResult(
                approval.ComponentId,
                approval.ActionId,
                Outcome: ActionOutcome.Blocked,
                Message: $"Approval required for {approval.ComponentId}.{approval.ActionId}."))
            .ToArray();
        var approvalText = RuntimePlanApprovals.BuildApprovalRequiredResponseText(pendingApprovals);

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordPlanning(plannedActions, allowedComponentCount)
                .RecordExecution(blockedResults)
                .RecordSuccess(approvalText);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        var approvalResponse = RuntimeTurnResponses.Build(
            agentName,
            approvalText,
            plannedActions,
            blockedResults,
            generatedUi: BuildPlanGeneratedUi(plan),
            pendingApprovals: pendingApprovals,
            requiresApproval: true);
        var terminalTurn = new TerminalTurnContext(
            sessionId,
            runId,
            conversationSessionId,
            userMessage,
            inspectorRunId,
            inspectorStartedAt,
            agentName,
            inspectorPlan,
            inspectorEvents,
            emitEvent,
            cancellationToken);
        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ApprovalRequired,
            AgentName = agentName,
            PendingApprovals = pendingApprovals
        });
        await EmitExecutionResultsAsync(blockedResults, emitEvent);
        await FinalizeTurnResponseAsync(
            terminalTurn,
            approvalResponse,
            blockedResults,
            succeeded: false,
            errorMessage: approvalText);
        return approvalResponse;
    }

    private async Task<AgentTurnResponse> HandleValidationFailedAsync(
        string agentName,
        string sessionId,
        string? runId,
        string conversationSessionId,
        string userMessage,
        string inspectorRunId,
        DateTimeOffset inspectorStartedAt,
        ActionPlan? inspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> inspectorEvents,
        Action<string, string?> appendInspectorEvent,
        PromptTraceBuilder traceBuilder,
        int allowedComponentCount,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        PlanValidationResult validationResult,
        ActionPlan plan,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent,
        CancellationToken cancellationToken)
    {
        var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
        var validationFailures = RuntimePlanResponses.BuildValidationFailureResults(validationResult, effectiveTier);
        var clarification = RuntimePlanResponses.BuildValidationClarificationText(validationResult, validationFailures);
        appendInspectorEvent("ValidationFailed", SerializeInspectorPayload(validationFailures));

        if (traceBuilder.IsEnabled)
        {
            traceBuilder.RecordPlanning(plannedActions, allowedComponentCount)
                .RecordExecution(validationFailures)
                .RecordSuccess(clarification);
            await StoreTraceAsync(traceBuilder, cancellationToken);
        }

        var validationResponse = RuntimeTurnResponses.Build(
            agentName,
            clarification,
            [],
            validationFailures,
            generatedUi: BuildPlanGeneratedUi(plan),
            clarificationQuestion: clarification);
        var terminalTurn = new TerminalTurnContext(
            sessionId,
            runId,
            conversationSessionId,
            userMessage,
            inspectorRunId,
            inspectorStartedAt,
            agentName,
            inspectorPlan,
            inspectorEvents,
            emitEvent,
            cancellationToken);
        await EmitExecutionResultsAsync(validationFailures, emitEvent);
        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ClarificationRequired,
            AgentName = agentName,
            ClarificationQuestion = clarification
        });
        await FinalizeTurnResponseAsync(
            terminalTurn,
            validationResponse,
            validationFailures,
            succeeded: false,
            errorMessage: clarification);
        return validationResponse;
    }

    private async Task<AgentTurnResponse> HandleExecutionCompletedAsync(ExecutionCompletionContext context)
    {
        if (context.TraceBuilder.IsEnabled)
        {
            context.TraceBuilder.RecordPlanning(context.PlannedActions, context.AllowedComponentCount)
                .RecordExecution(context.ExecutionResults)
                .RecordSuccess(context.ResponseText);
            await StoreTraceAsync(context.TraceBuilder, context.CancellationToken);
        }

        await TrackFinishedAsync(
            context.AgentName,
            context.Request,
            context.OverallSucceeded ? AgentBlazorRunOutcome.Succeeded : AgentBlazorRunOutcome.Failed,
            context.PlannedActions.Count,
            context.ExecutionResults.Count,
            context.ProviderConfigured);

        var turnResponse = RuntimeTurnResponses.Build(
            context.AgentName,
            context.ResponseText,
            context.PlannedActions,
            context.ExecutionResults,
            generatedUi: BuildPlanGeneratedUi(context.Plan));
        var terminalTurn = new TerminalTurnContext(
            context.SessionId,
            context.RunId,
            context.ConversationSessionId,
            context.Request.UserMessage,
            context.InspectorRunId,
            context.InspectorStartedAt,
            context.AgentName,
            context.InspectorPlan,
            context.InspectorEvents,
            context.EmitEvent,
            context.CancellationToken);
        context.AppendInspectorEvent("ExecutionFinished", $"Succeeded: {context.OverallSucceeded}");
        context.AppendInspectorEvent("RunFinished", context.ResponseText);
        await RecordActionHistoryAsync(
            context.SessionId,
            context.Request.GetEffectiveUserId(),
            context.Request.UserMessage,
            [.. context.ExecutionResults],
            context.Plan,
            context.CancellationToken);
        await EmitReasoningEventsAsync(context.AgentName, context.Plan.ReasoningContent, context.EmitEvent);
        await FinalizeTurnResponseAsync(
            terminalTurn,
            turnResponse,
            context.ExecutionResults,
            succeeded: context.OverallSucceeded,
            errorMessage: context.OverallSucceeded ? null : context.ResponseText);
        return turnResponse;
    }

    private async Task HandleTurnCanceledAsync(
        string sessionId,
        string inspectorRunId,
        DateTimeOffset inspectorStartedAt,
        ActionPlan? inspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> inspectorEvents,
        string agentName,
        PromptTraceBuilder traceBuilder,
        Action<string, string?> appendInspectorEvent)
    {
        traceBuilder.RecordCanceled();
        await StoreTraceAsync(traceBuilder, CancellationToken.None);
        appendInspectorEvent("RunCanceled", "Turn canceled.");
        RecordInspectorRun(
            inspectorRunId,
            sessionId,
            agentName,
            inspectorStartedAt,
            inspectorPlan,
            executionResults: [],
            events: inspectorEvents,
            succeeded: false,
            errorMessage: "Run canceled.");
    }

    private async Task HandleTurnFailedAsync(
        string sessionId,
        string? runId,
        string userMessage,
        string inspectorRunId,
        DateTimeOffset inspectorStartedAt,
        ActionPlan? inspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> inspectorEvents,
        string? agentName,
        Exception exception,
        PromptTraceBuilder traceBuilder,
        Action<string, string?> appendInspectorEvent,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        _logger?.LogError(exception, "Turn failed");
        traceBuilder.RecordFailure(exception.Message);
        await StoreTraceAsync(traceBuilder, CancellationToken.None);
        appendInspectorEvent("RunError", exception.Message);
        RecordInspectorRun(
            inspectorRunId,
            sessionId,
            agentName ?? "none",
            inspectorStartedAt,
            inspectorPlan,
            executionResults: [],
            events: inspectorEvents,
            succeeded: false,
            errorMessage: exception.Message);
        await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.RunError,
            AgentName = agentName,
            ErrorMessage = exception.Message
        });
        await NotifyErrorAsync(
            sessionId,
            runId,
            agentName ?? "none",
            userMessage,
            exception.Message,
            CancellationToken.None);
    }

    private async Task<ExecutionPhaseResult> HandleExecutionPhaseAsync(
        string agentName,
        string sessionId,
        string? runId,
        string sharedStateRunId,
        IDictionary<string, string>? requestContext,
        ActionPlan plan,
        IReadOnlyDictionary<string, string> latestSharedState,
        IAgentComponentRegistry? registry,
        string? currentRoute,
        Action<string, string?> appendInspectorEvent,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent,
        CancellationToken cancellationToken)
    {
        var executionOptions = RuntimePlanExecution.BuildExecutionOptions(
            sessionId,
            requestContext,
            RunIdContextKey);
        var partition = RuntimePlanExecution.Partition(plan);
        var executionResult = await _executor.ExecuteAsync(partition.ComponentPlan, executionOptions, cancellationToken);

        var toolResults = new List<ComponentActionExecutionResult>();
        foreach (var toolStep in partition.ToolSteps)
        {
            var toolResult = await ExecuteServiceToolAsync(toolStep, cancellationToken);
            toolResults.Add(toolResult);
        }

        var executionResults = RuntimePlanExecution.CombineExecutionResults(executionResult, toolResults);
        await EmitExecutionResultsAsync(executionResults, emitEvent);
        await NotifyToolExecutionFinishedAsync(
            sessionId,
            runId,
            agentName,
            executionResults,
            CancellationToken.None);

        var nextSharedState = latestSharedState;
        var mountedComponentsAfterExecution = GetMountedComponents(registry);
        var currentRouteAfterExecution = NormalizeRoutePath(ExtractCurrentRoute(mountedComponentsAfterExecution)) ?? currentRoute;
        var sharedStateAfterExecution = BuildSharedStateSnapshot(
            latestSharedState,
            mountedComponentsAfterExecution,
            currentRouteAfterExecution);
        var sharedStateDelta = BuildSharedStateDelta(latestSharedState, sharedStateAfterExecution);
        if (sharedStateDelta.Count > 0)
        {
            _sharedStateStore.ApplyDelta(agentName, sessionId, sharedStateRunId, sharedStateDelta);
            await EmitSharedStateDeltaAsync(agentName, sharedStateDelta, emitEvent);
            nextSharedState = sharedStateAfterExecution;
            await EmitSharedStateSnapshotAsync(agentName, nextSharedState, emitEvent);
            appendInspectorEvent("StateDelta", SerializeInspectorPayload(sharedStateDelta));
            appendInspectorEvent("StateSnapshot", SerializeInspectorPayload(nextSharedState));
        }

        return new ExecutionPhaseResult(
            executionResult,
            executionResults,
            toolResults,
            nextSharedState);
    }

    private async Task FinalizeTurnResponseAsync(
        TerminalTurnContext terminalTurn,
        AgentTurnResponse response,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        bool succeeded,
        string? errorMessage,
        bool appendRunFinishedInspectorEvent = false,
        string? textToEmit = null)
    {
        await StoreConversationTurnAsync(
            terminalTurn.ConversationSessionId,
            terminalTurn.UserMessage,
            response,
            terminalTurn.CancellationToken);
        await EmitTextDeltasAsync(terminalTurn.AgentName, textToEmit ?? response.ResponseText, terminalTurn.EmitEvent);
        await EmitRunFinishedAsync(response, terminalTurn.EmitEvent);

        var recordedEvents = appendRunFinishedInspectorEvent
            ? [
                .. terminalTurn.InspectorEvents,
                new AgentBlazor.Core.Paid.InspectorEvent(
                    Timestamp: DateTimeOffset.UtcNow,
                    Kind: "RunFinished",
                    ComponentId: null,
                    ActionId: null,
                    Detail: response.ResponseText)
            ]
            : terminalTurn.InspectorEvents;

        RecordInspectorRun(
            terminalTurn.InspectorRunId,
            terminalTurn.SessionId,
            terminalTurn.AgentName,
            terminalTurn.InspectorStartedAt,
            terminalTurn.InspectorPlan,
            executionResults,
            recordedEvents,
            succeeded,
            errorMessage);
        await NotifyTurnFinishedAsync(
            terminalTurn.SessionId,
            terminalTurn.RunId,
            terminalTurn.AgentName,
            terminalTurn.UserMessage,
            response,
            CancellationToken.None);
    }

    private string BuildNoAllowedActionsResponseText(
        IReadOnlyList<string> blockedByAgentPolicy,
        IReadOnlyList<string> blockedByTier)
    {
        var effectiveTier = _entitlementService?.CurrentTier ?? _options.Value.LicensedTier;
        return RuntimeEarlyExitResponses.BuildNoAllowedActionsResponseText(
            blockedByAgentPolicy,
            blockedByTier,
            effectiveTier,
            "component actions");
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static string BuildNoAgentResponseText(
        int registeredCount,
        string? requestedAgentName,
        IDictionary<string, string>? context)
        => RuntimeTurnPreflight.BuildNoAgentResponseText(registeredCount, requestedAgentName, context);

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

    private sealed record PlanningPhaseResult(
        ActionPlan Plan,
        ActionPlan InspectorPlan,
        string? PlanMessage);

    private sealed record TurnPreambleResult(
        IAgentComponentRegistry? Registry,
        IReadOnlyList<MountedComponentState> MountedComponents,
        string? CurrentRoute,
        AgentRegistration? Registration);

    private sealed record TurnSetupPhaseResult(
        string ConversationSessionId,
        IReadOnlyList<ConversationTurn> ConversationHistory,
        AllowedComponentPolicyResult AllowedPolicy,
        IReadOnlyList<AvailableComponent> AllowedComponents,
        IReadOnlyDictionary<string, string> LatestSharedState,
        bool ProviderConfigured);

    private sealed record ExecutionPhaseResult(
        PlanExecutionResult ExecutionResult,
        ComponentActionExecutionResult[] ExecutionResults,
        IReadOnlyList<ComponentActionExecutionResult> ToolResults,
        IReadOnlyDictionary<string, string> LatestSharedState);

    private sealed record PreExecutionPhaseResult(
        IReadOnlyList<PlannedComponentAction> PlannedActions,
        AgentTurnResponse? TerminalResponse);

    private sealed record PreExecutionContext(
        string AgentName,
        string SessionId,
        string? RunId,
        string ConversationSessionId,
        AgentTurnRequest Request,
        string InspectorRunId,
        DateTimeOffset InspectorStartedAt,
        ActionPlan? InspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> InspectorEvents,
        PromptTraceBuilder TraceBuilder,
        IReadOnlyList<AvailableComponent> AllowedComponents,
        IReadOnlyList<MountedComponentState> MountedComponents,
        ActionPlan Plan,
        Action<string, string?> AppendInspectorEvent,
        Func<AgentTurnStreamEvent, ValueTask>? EmitEvent,
        CancellationToken CancellationToken);

    private sealed record ExecutionCompletionContext(
        string AgentName,
        string SessionId,
        string? RunId,
        string ConversationSessionId,
        AgentTurnRequest Request,
        string InspectorRunId,
        DateTimeOffset InspectorStartedAt,
        ActionPlan? InspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> InspectorEvents,
        Action<string, string?> AppendInspectorEvent,
        PromptTraceBuilder TraceBuilder,
        int AllowedComponentCount,
        IReadOnlyList<PlannedComponentAction> PlannedActions,
        ActionPlan Plan,
        PlanExecutionResult ExecutionResult,
        IReadOnlyList<ComponentActionExecutionResult> ExecutionResults,
        string ResponseText,
        bool OverallSucceeded,
        bool ProviderConfigured,
        Func<AgentTurnStreamEvent, ValueTask>? EmitEvent,
        CancellationToken CancellationToken);

    private sealed record PlannerTerminalContext(
        string AgentName,
        string SessionId,
        string? RunId,
        string ConversationSessionId,
        string UserMessage,
        string InspectorRunId,
        DateTimeOffset InspectorStartedAt,
        ActionPlan? InspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> InspectorEvents,
        Action<string, string?> AppendInspectorEvent,
        ActionPlan Plan,
        string? PlanMessage,
        Func<AgentTurnStreamEvent, ValueTask>? EmitEvent,
        CancellationToken CancellationToken);

    private sealed record EarlyExitTurnContext(
        string SessionId,
        string? RunId,
        string ConversationSessionId,
        AgentTurnRequest Request,
        string InspectorRunId,
        DateTimeOffset InspectorStartedAt,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> InspectorEvents,
        Func<AgentTurnStreamEvent, ValueTask>? EmitEvent,
        CancellationToken CancellationToken);

    private sealed record TurnFlowResult(
        ActionPlan? InspectorPlan,
        AgentTurnResponse Response);

    private sealed record TurnFlowContext(
        AgentRegistration Registration,
        string SessionId,
        string? RunId,
        string SharedStateRunId,
        string ConversationSessionId,
        AgentTurnRequest Request,
        string InspectorRunId,
        DateTimeOffset InspectorStartedAt,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> InspectorEvents,
        PromptTraceBuilder TraceBuilder,
        IReadOnlyList<AvailableComponent> AllowedComponents,
        IReadOnlyList<MountedComponentState> MountedComponents,
        IReadOnlyList<ConversationTurn> ConversationHistory,
        IReadOnlyDictionary<string, string> LatestSharedState,
        string? CurrentRoute,
        bool ProviderConfigured,
        IAgentComponentRegistry? Registry,
        Stopwatch Stopwatch,
        TurnInspectorCollector Inspector,
        Func<AgentTurnStreamEvent, ValueTask>? EmitEvent,
        CancellationToken CancellationToken);

    private sealed record TerminalTurnContext(
        string SessionId,
        string? RunId,
        string ConversationSessionId,
        string UserMessage,
        string InspectorRunId,
        DateTimeOffset InspectorStartedAt,
        string AgentName,
        ActionPlan? InspectorPlan,
        IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> InspectorEvents,
        Func<AgentTurnStreamEvent, ValueTask>? EmitEvent,
        CancellationToken CancellationToken);

    private sealed class TurnInspectorCollector
    {
        private readonly List<AgentBlazor.Core.Paid.InspectorEvent> _events = [];

        public IReadOnlyList<AgentBlazor.Core.Paid.InspectorEvent> Events => _events;

        public void Append(
            string kind,
            string? detail = null,
            string? componentId = null,
            string? actionId = null)
        {
            _events.Add(new AgentBlazor.Core.Paid.InspectorEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Kind: kind,
                ComponentId: componentId,
                ActionId: actionId,
                Detail: detail));
        }
    }
}
