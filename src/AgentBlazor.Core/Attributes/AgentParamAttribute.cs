namespace AgentBlazor.Attributes;

/// <summary>
/// Decorates a parameter on an [AgentAction] method to give the agent
/// a description and optional constraints for that parameter.
/// </summary>
/// <example>
/// [AgentAction("Sort the grid")]
/// public Task Sort(
///     [AgentParam("Column name", Required = true)] string column,
///     [AgentParam("Direction", Required = true, AllowedValues = "asc,desc")] string direction) { ... }
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class AgentParamAttribute : Attribute
{
    public AgentParamAttribute(string description)
    {
        Description = description;
    }

    /// <summary>Human-readable description of what this parameter expects.</summary>
    public string Description { get; }

    /// <summary>
    /// Whether the agent must always supply this parameter.
    /// When true and the value is missing, the runtime returns a clarification request.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Comma-separated list of allowed values for enum-style parameters.
    /// Example: "asc,desc" or "high,medium,low"
    /// </summary>
    public string? AllowedValues { get; set; }
}
