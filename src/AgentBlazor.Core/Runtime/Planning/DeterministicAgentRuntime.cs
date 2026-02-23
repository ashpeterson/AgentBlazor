using System.Diagnostics;
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
internal sealed class DeterministicAgentRuntime : IAgentRuntime
{
    private readonly IStructuredActionPlanner _planner;
    private readonly IPlanValidator _validator;
    private readonly IPlanExecutor _executor;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IComponentCapabilityCatalog _componentCatalog;
    private readonly IAgentComponentRegistry? _componentRegistry;
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
        IAgentComponentRegistry? componentRegistry,
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
        _componentRegistry = componentRegistry;
        _options = options;
        _telemetrySink = telemetrySink;
        _tracingOptions = tracingOptions;
        _traceStore = traceStore;
        _logger = logger;
    }

    public async Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            throw new ArgumentException("User message is required.", nameof(request));
        }

        var stopwatch = Stopwatch.StartNew();
        var traceBuilder = new PromptTraceBuilder(_tracingOptions);
        var sessionId = request.GetEffectiveSessionId();

        // Resolve agent
        var registration = ResolveAgent(request.AgentName);
        traceBuilder.RecordEntry(request, registration?.Name);

        if (registration is null)
        {
            return await HandleNoAgentAsync(request, traceBuilder, cancellationToken);
        }

        // Build context
        var allowedComponents = GetAllowedComponents(registration);
        var mountedComponents = GetMountedComponents();

        await TrackStartedAsync(registration.Name, request);

        try
        {
            // PHASE 1: PLAN
            _logger?.LogInformation("Phase 1: Planning for request: {Request}", request.UserMessage);

            var planRequest = new ActionPlanRequest
            {
                UserMessage = request.UserMessage,
                SessionId = sessionId,
                UserId = request.GetEffectiveUserId(),
                AvailableComponents = allowedComponents,
                MountedComponents = mountedComponents
            };

            var plan = await _planner.PlanAsync(planRequest, cancellationToken);

            // Handle clarification requests
            if (plan.RequiresClarification)
            {
                _logger?.LogInformation("Plan requires clarification: {Question}", plan.ClarificationNeeded);
                return await BuildClarificationResponseAsync(
                    registration.Name,
                    plan.ClarificationNeeded!,
                    traceBuilder,
                    cancellationToken);
            }

            if (plan.IsEmpty)
            {
                _logger?.LogInformation("Plan is empty - no actions identified");
                return await BuildEmptyPlanResponseAsync(
                    registration.Name,
                    traceBuilder,
                    cancellationToken);
            }

            _logger?.LogInformation("Plan created with {StepCount} steps", plan.Steps.Count);

            // PHASE 2: VALIDATE
            _logger?.LogInformation("Phase 2: Validating plan");

            var validationContext = new PlanValidationContext
            {
                AllowedComponents = allowedComponents,
                MountedComponents = mountedComponents,
                ApprovedActions = GetApprovedActions()
            };

            var validationResult = _validator.Validate(plan, validationContext);

            if (!validationResult.IsValid)
            {
                var clarification = validationResult.BuildClarificationQuestion();
                _logger?.LogInformation("Plan validation failed: {Question}", clarification);
                return await BuildClarificationResponseAsync(
                    registration.Name,
                    clarification ?? "The plan could not be validated.",
                    traceBuilder,
                    cancellationToken);
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
            var plannedActions = plan.Steps
                .Select(s => new PlannedComponentAction(
                    s.ComponentId,
                    s.ActionId,
                    "Planned by structured planner",
                    s.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)))
                .ToArray();

            var executionResults = executionResult.StepResults
                .Select(r => new ComponentActionExecutionResult(
                    r.Step.ComponentId,
                    r.Step.ActionId,
                    r.Succeeded,
                    r.Message))
                .ToArray();

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
                executionResults.Length);

            _logger?.LogInformation(
                "Turn completed in {Duration}ms with {SuccessCount}/{TotalCount} successful steps",
                stopwatch.ElapsedMilliseconds,
                executionResult.SuccessCount,
                executionResult.StepResults.Count);

            return new AgentTurnResponse(
                AgentName: registration.Name,
                ResponseText: responseText,
                PlannedActions: plannedActions,
                ExecutionResults: executionResults);
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
                new ActionParameter { Name = "operator", Type = "string", Required = true, Description = "Filter operator", AllowedValues = ["eq", "neq", "gt", "gte", "lt", "lte", "contains", "startsWith", "endsWith"] },
                new ActionParameter { Name = "value", Type = "any", Required = true, Description = "Value to filter by" }
            ],
            ("AgentDataGrid", "sort") =>
            [
                new ActionParameter { Name = "column", Type = "string", Required = true, Description = "Column to sort by" },
                new ActionParameter { Name = "direction", Type = "string", Required = false, Description = "Sort direction", AllowedValues = ["asc", "desc"] }
            ],
            ("AgentDataGrid", "go_to_page") =>
            [
                new ActionParameter { Name = "page", Type = "int", Required = true, Description = "Page number" }
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
                    .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "")
            })
            .ToList();
    }

    private static IReadOnlySet<string> GetApprovedActions()
    {
        // In a real implementation, this would check approval state
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

    private async Task TrackStartedAsync(string agentName, AgentTurnRequest request)
    {
        await _telemetrySink.TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
        {
            Kind = AgentBlazorRunEventKind.Started,
            Source = AgentBlazorTelemetrySources.Runtime,
            AgentName = agentName,
            RequestedAgentName = request.AgentName,
            HasContext = request.Context?.Count > 0,
            ProviderConfigured = true
        });
    }

    private async Task TrackFinishedAsync(
        string agentName,
        AgentTurnRequest request,
        AgentBlazorRunOutcome outcome,
        int plannedCount,
        int executedCount)
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
            ProviderConfigured = true
        });
    }
}
