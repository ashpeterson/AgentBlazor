namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Validates action plans before execution.
/// No inference. No auto-correction. Just validation.
/// </summary>
public interface IPlanValidator
{
    /// <summary>
    /// Validates that all steps in the plan are executable.
    /// Returns validation errors, not auto-corrected plans.
    /// </summary>
    PlanValidationResult Validate(
        ActionPlan plan,
        PlanValidationContext context);
}

/// <summary>
/// Context for plan validation.
/// </summary>
public sealed record PlanValidationContext
{
    /// <summary>
    /// Components and actions allowed by policy.
    /// </summary>
    public required IReadOnlyList<AvailableComponent> AllowedComponents { get; init; }

    /// <summary>
    /// Currently mounted component instances.
    /// </summary>
    public IReadOnlyList<MountedComponentState> MountedComponents { get; init; } = [];

    /// <summary>
    /// Actions that have been approved for execution.
    /// </summary>
    public IReadOnlySet<string> ApprovedActions { get; init; } = new HashSet<string>();
}
