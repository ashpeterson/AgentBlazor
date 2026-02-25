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

    /// <summary>
    /// Optional run ID used to correlate downstream execution events.
    /// </summary>
    public string? RunId { get; init; }
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

    public int SuccessCount => StepResults.Count(r => r.Outcome is ActionOutcome.Applied or ActionOutcome.Queued);
    public int FailureCount => StepResults.Count(r =>
        r.Outcome is ActionOutcome.Failed or ActionOutcome.Blocked or ActionOutcome.NeedsClarification);
    public int AppliedCount => StepResults.Count(r => r.Outcome is ActionOutcome.Applied);
    public int QueuedCount => StepResults.Count(r => r.Outcome is ActionOutcome.Queued);
    public int BlockedCount => StepResults.Count(r => r.Outcome is ActionOutcome.Blocked);
    public int ClarificationCount => StepResults.Count(r => r.Outcome is ActionOutcome.NeedsClarification);
}

/// <summary>
/// Result of executing a single step.
/// </summary>
public sealed record StepExecutionResult
{
    public required PlannedStep Step { get; init; }
    public required ActionOutcome Outcome { get; init; }
    public required string Message { get; init; }
    public TimeSpan Duration { get; init; }

    public bool Succeeded => Outcome is ActionOutcome.Applied or ActionOutcome.Queued;
}
