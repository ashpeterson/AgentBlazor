namespace AgentBlazor.Attributes;

/// <summary>
/// Marks a public method as an action the agent can invoke.
/// Apply to methods on components that inherit AgentControllableComponentBase.
/// </summary>
/// <example>
/// [AgentAction("Filter rows by status")]
/// public async Task FilterByStatus([AgentParam("Status: active, inactive")] string status) { ... }
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AgentActionAttribute : Attribute
{
    public AgentActionAttribute()
    {
    }

    public AgentActionAttribute(string description)
    {
        Description = description;
    }

    /// <summary>Human-readable description shown to the agent in the system prompt.</summary>
    public string? Description { get; }

    /// <summary>
    /// Override the action id sent to the agent. Defaults to the method name snake_cased.
    /// Example: FilterByStatus → filter_by_status
    /// </summary>
    public string? ActionId { get; set; }

    /// <summary>
    /// When true, the runtime will request user approval before executing this action.
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Name of a bool property or parameterless bool method on the component that gates availability.
    /// When the member returns false the action is excluded from the agent's capability list at runtime.
    /// Example: AvailableWhen = nameof(CanDelete)
    /// </summary>
    public string? AvailableWhen { get; set; }

    /// <summary>
    /// Whether the agent should follow up after this action completes. Defaults to true.
    /// </summary>
    public bool FollowUp { get; set; } = true;

    /// <summary>
    /// Imperative behavioral guidance injected directly into the planner prompt.
    /// Use ALWAYS/NEVER language to steer the LLM — e.g. "ALWAYS call this when the user asks
    /// for a poem. NEVER write the poem text in the message field."
    /// Eliminates the need for a separate agent-instructions file for per-action behavior.
    /// </summary>
    public string? Instructions { get; set; }
}
