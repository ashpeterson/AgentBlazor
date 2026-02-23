namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// A deterministic action plan produced by the LLM planner.
/// No heuristics. No fallbacks. Just structured steps.
/// </summary>
public sealed record ActionPlan
{
    public required IReadOnlyList<PlannedStep> Steps { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string? ClarificationNeeded { get; init; }

    public bool RequiresClarification => !string.IsNullOrWhiteSpace(ClarificationNeeded);
    public bool IsEmpty => Steps.Count == 0 && !RequiresClarification;

    public static ActionPlan Empty() => new() { Steps = [] };

    public static ActionPlan NeedsClarification(string question) => new()
    {
        Steps = [],
        ClarificationNeeded = question
    };
}

/// <summary>
/// A single step in the action plan.
/// Must be fully specified - no inference allowed during execution.
/// </summary>
public sealed record PlannedStep
{
    public required string ComponentId { get; init; }
    public required string ActionId { get; init; }
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    /// <summary>
    /// Target component instance (agentId). Required for multi-instance scenarios.
    /// </summary>
    public string? TargetAgentId { get; init; }
}

/// <summary>
/// Validation result for a planned step.
/// </summary>
public sealed record StepValidationResult
{
    public required PlannedStep Step { get; init; }
    public required bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> MissingParameters { get; init; } = [];
}

/// <summary>
/// Validation result for the entire plan.
/// </summary>
public sealed record PlanValidationResult
{
    public required ActionPlan Plan { get; init; }
    public required bool IsValid { get; init; }
    public IReadOnlyList<StepValidationResult> StepResults { get; init; } = [];

    public string? BuildClarificationQuestion()
    {
        if (IsValid) return null;

        var invalidSteps = StepResults.Where(s => !s.IsValid).ToList();
        if (invalidSteps.Count == 0) return null;

        var firstInvalid = invalidSteps[0];
        if (firstInvalid.MissingParameters.Count > 0)
        {
            var param = firstInvalid.MissingParameters[0];
            return $"What value should I use for '{param}' in {firstInvalid.Step.ComponentId}.{firstInvalid.Step.ActionId}?";
        }

        if (firstInvalid.Errors.Count > 0)
        {
            return firstInvalid.Errors[0];
        }

        return "I couldn't understand that request. Can you be more specific?";
    }
}
