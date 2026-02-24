using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AgentBlazor.Agents;
using AgentBlazor.Components;
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
    private readonly IStructuredActionPlanner _planner;
    private readonly IPlanValidator _validator;
    private readonly IPlanExecutor _executor;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IComponentCapabilityCatalog _componentCatalog;
    private readonly IConversationManager? _conversationManager;
    private readonly IAgentComponentRegistry? _componentRegistry;
    private readonly IComponentRouteRegistry _componentRouteRegistry;
    private readonly IRouteRegistry _routeRegistry;
    private readonly IOptions<AgentBlazorOptions> _options;
    private readonly IAgentBlazorTelemetrySink _telemetrySink;
    private readonly IOptions<PromptTracingOptions>? _tracingOptions;
    private readonly IPromptTraceStore? _traceStore;
    private readonly ILogger<DeterministicAgentRuntime>? _logger;

    public DeterministicAgentRuntime(
        IStructuredActionPlanner planner,
        IPlanValidator validator,
        IPlanExecutor executor,
        IAgentRegistry agentRegistry,
        IComponentCapabilityCatalog componentCatalog,
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
        var channel = Channel.CreateUnbounded<AgentTurnStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await RunTurnCoreAsync(
                    request,
                    streamEvent => channel.Writer.WriteAsync(streamEvent, cancellationToken),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await foreach (var streamEvent in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return streamEvent;
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
            var noAgentResponse = await HandleNoAgentAsync(request, traceBuilder, cancellationToken);
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
                var clarificationResponse = await BuildClarificationResponseAsync(
                    registration.Name,
                    plan.ClarificationNeeded!,
                    traceBuilder,
                    cancellationToken);
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
                var emptyPlanResponse = await BuildEmptyPlanResponseAsync(
                    registration.Name,
                    traceBuilder,
                    cancellationToken);
                await StoreConversationTurnAsync(sessionId, request.UserMessage, emptyPlanResponse, cancellationToken);
                await EmitTextDeltasAsync(emptyPlanResponse.AgentName, emptyPlanResponse.ResponseText, emitEvent);
                await EmitRunFinishedAsync(emptyPlanResponse, emitEvent);
                return emptyPlanResponse;
            }

            _logger?.LogInformation("Plan created with {StepCount} steps", plan.Steps.Count);

            plan = EnsureNavigationWhenTargetUnmounted(plan, mountedComponents, request.UserMessage);
            plan = EnsureDialogOpenBeforeUnmountedFormAction(plan, mountedComponents, allowedComponents);
            plan = NormalizePlanArguments(plan);
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
                        Succeeded: false,
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

                var approvalResponse = new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: approvalResponseText,
                    PlannedActions: plannedActions,
                    ExecutionResults: blockedResults)
                {
                    RequiresApproval = true,
                    PendingApprovals = pendingApprovals
                };
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

                var validationResponse = new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: clarificationText,
                    PlannedActions: [],
                    ExecutionResults: validationFailures);
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
                SessionId = sessionId
            };

            var executionResult = await _executor.ExecuteAsync(plan, executionOptions, cancellationToken);

            // Build response
            var executionResults = executionResult.StepResults
                .Select(r => new ComponentActionExecutionResult(
                    r.Step.ComponentId,
                    r.Step.ActionId,
                    r.Succeeded,
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

            var successResponse = new AgentTurnResponse(
                AgentName: registration.Name,
                ResponseText: responseText,
                PlannedActions: plannedActions,
                ExecutionResults: executionResults);
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
    /// This enables one-turn flows like "open onboarding and set name" where forms live inside dialogs.
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
                    Succeeded: false,
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
        if (result.Succeeded)
        {
            var actionCount = result.StepResults.Count;
            return actionCount == 1
                ? "Done."
                : $"Completed {actionCount} actions.";
        }

        var failures = result.StepResults.Where(r => !r.Succeeded).ToList();
        if (failures.Count == 1)
        {
            return failures[0].Message;
        }

        return $"Completed {result.SuccessCount} of {result.StepResults.Count} actions. {failures[0].Message}";
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
        AgentTurnRequest request,
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
        foreach (var plannedAction in plannedActions)
        {
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.PlannedAction,
                AgentName = null,
                PlannedAction = plannedAction
            });
        }
    }

    private static async ValueTask EmitExecutionResultsAsync(
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        Func<AgentTurnStreamEvent, ValueTask>? emitEvent)
    {
        foreach (var executionResult in executionResults)
        {
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.ExecutionResult,
                AgentName = null,
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

        foreach (var delta in SplitTextDeltas(responseText))
        {
            await EmitEventAsync(emitEvent, new AgentTurnStreamEvent
            {
                Kind = AgentTurnStreamEventKind.TextDelta,
                AgentName = agentName,
                TextDelta = delta
            });
            await Task.Yield();
        }
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
