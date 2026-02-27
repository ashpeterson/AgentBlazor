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
    public AgentActionAttribute(string description)
    {
        Description = description;
    }

    /// <summary>Human-readable description shown to the agent in the system prompt.</summary>
    public string Description { get; }

    /// <summary>
    /// Override the action id sent to the agent. Defaults to the method name snake_cased.
    /// Example: FilterByStatus → filter_by_status
    /// </summary>
    public string? ActionId { get; set; }

    /// <summary>
    /// When true, the runtime will request user approval before executing this action.
    /// </summary>
    public bool RequiresApproval { get; set; }
}
