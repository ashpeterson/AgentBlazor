using System.Diagnostics;
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
            var plannedAction = new PlannedComponentAction(
                step.ComponentId,
                step.ActionId,
                Reason: "Planned action from structured planner",
                Arguments: step.Arguments.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase));

            var executionResult = await _actionExecutor.ExecuteAsync(plannedAction, cancellationToken);

            stopwatch.Stop();

            return new StepExecutionResult
            {
                Step = step,
                Succeeded = executionResult.Succeeded,
                Message = executionResult.Message,
                Duration = stopwatch.Elapsed,
                Queued = executionResult.Message.Contains("Queued", StringComparison.OrdinalIgnoreCase),
                BlockedByApproval = executionResult.Message.Contains("Approval required", StringComparison.OrdinalIgnoreCase)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new StepExecutionResult
            {
                Step = step,
                Succeeded = false,
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
                Succeeded = false,
                Message = $"Execution error: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }
}
