using System.Diagnostics;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Executes validated action plans.
/// No heuristics. No fallbacks. Just step-by-step execution.
/// </summary>
internal sealed class PlanExecutor : IPlanExecutor
{
    private readonly IComponentActionExecutor _actionExecutor;
    private readonly ILogger<PlanExecutor>? _logger;

    public PlanExecutor(
        IComponentActionExecutor actionExecutor,
        ILogger<PlanExecutor>? logger = null)
    {
        _actionExecutor = actionExecutor;
        _logger = logger;
    }

    public async Task<PlanExecutionResult> ExecuteAsync(
        ActionPlan plan,
        PlanExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var stepResults = new List<StepExecutionResult>();
        var allSucceeded = true;

        foreach (var step in plan.Steps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogInformation("Plan execution cancelled at step {Index}", stepResults.Count);
                break;
            }

            var stepResult = await ExecuteStepAsync(step, options, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            stepResults.Add(stepResult);

            if (!stepResult.Succeeded)
            {
                allSucceeded = false;
                _logger?.LogWarning(
                    "Step {ComponentId}.{ActionId} failed: {Message}",
                    step.ComponentId,
                    step.ActionId,
                    stepResult.Message);

                if (!options.ContinueOnFailure)
                {
                    _logger?.LogInformation("Stopping execution due to failure (ContinueOnFailure=false)");
                    break;
                }
            }
            else
            {
                _logger?.LogInformation(
                    "Step {ComponentId}.{ActionId} succeeded: {Message}",
                    step.ComponentId,
                    step.ActionId,
                    stepResult.Message);
            }
        }

        stopwatch.Stop();

        return new PlanExecutionResult
        {
            Plan = plan,
            StepResults = stepResults,
            Succeeded = allSucceeded && stepResults.Count == plan.Steps.Count,
            Duration = stopwatch.Elapsed
        };
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(
        PlannedStep step,
        PlanExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Convert to PlannedComponentAction for the executor
            var executionArguments = BuildExecutionArguments(step, options);
            var plannedAction = new PlannedComponentAction(
                step.ComponentId,
                step.ActionId,
                Reason: "Planned action from structured planner",
                Arguments: executionArguments);

            var executionResult = await _actionExecutor.ExecuteAsync(plannedAction, cancellationToken);

            stopwatch.Stop();

            return new StepExecutionResult
            {
                Step = step,
                Outcome = executionResult.Outcome,
                Message = executionResult.Message,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new StepExecutionResult
            {
                Step = step,
                Outcome = ActionOutcome.Failed,
                Message = "Execution cancelled",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "Exception executing step {ComponentId}.{ActionId}", step.ComponentId, step.ActionId);

            return new StepExecutionResult
            {
                Step = step,
                Outcome = ActionOutcome.Failed,
                Message = $"Execution error: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildExecutionArguments(
        PlannedStep step,
        PlanExecutionOptions options)
    {
        var merged = step.Arguments.ToDictionary(
            kv => kv.Key,
            kv => kv.Value,
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(step.TargetAgentId) &&
            !merged.ContainsKey("agentId"))
        {
            merged["agentId"] = step.TargetAgentId;
        }

        if (!string.IsNullOrWhiteSpace(options.SessionId) &&
            !merged.ContainsKey(AgentRuntimeContextKeys.SessionId))
        {
            merged[AgentRuntimeContextKeys.SessionId] = options.SessionId;
        }

        if (!string.IsNullOrWhiteSpace(options.RunId) &&
            !merged.ContainsKey(AgentRuntimeContextKeys.RunId))
        {
            merged[AgentRuntimeContextKeys.RunId] = options.RunId;
        }

        return merged;
    }
}
