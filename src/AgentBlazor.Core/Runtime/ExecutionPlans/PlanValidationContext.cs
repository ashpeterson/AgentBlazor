namespace AgentBlazor.Core.Runtime.ExecutionPlans;

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
