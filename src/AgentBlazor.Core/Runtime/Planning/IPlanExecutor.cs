using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Executes validated action plans.
/// No heuristics. No fallbacks. Just step-by-step execution.
/// </summary>
public interface IPlanExecutor
{
    /// <summary>
    /// Executes all steps in the plan in order.
    /// Stops on first failure unless configured otherwise.
    /// </summary>
    Task<PlanExecutionResult> ExecuteAsync(
        ActionPlan plan,
        PlanExecutionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for plan execution.
/// </summary>
public sealed record PlanExecutionOptions
{
    /// <summary>
    /// If true, continue executing subsequent steps after a failure.
    /// Default is false (stop on first failure).
    /// </summary>
    public bool ContinueOnFailure { get; init; } = false;

    /// <summary>
    /// Session ID for component targeting.
    /// </summary>
    public string? SessionId { get; init; }
}

/// <summary>
/// Result of plan execution.
/// </summary>
public sealed record PlanExecutionResult
{
    public required ActionPlan Plan { get; init; }
    public required IReadOnlyList<StepExecutionResult> StepResults { get; init; }
    public required bool Succeeded { get; init; }
    public TimeSpan Duration { get; init; }

    public int SuccessCount => StepResults.Count(r => r.Succeeded);
    public int FailureCount => StepResults.Count(r => !r.Succeeded);
}

/// <summary>
/// Result of executing a single step.
/// </summary>
public sealed record StepExecutionResult
{
    public required PlannedStep Step { get; init; }
    public required bool Succeeded { get; init; }
    public required string Message { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// If the action required approval and it wasn't granted.
    /// </summary>
    public bool BlockedByApproval { get; init; }

    /// <summary>
    /// If the component wasn't mounted, action was queued.
    /// </summary>
    public bool Queued { get; init; }
}
