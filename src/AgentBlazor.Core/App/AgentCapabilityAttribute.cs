namespace AgentBlazor.App;

/// <summary>
/// Marks a class as a semantic capability container that can be projected to an external runtime.
/// Capability methods should use <c>[AgentAction]</c> for action metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentCapabilityAttribute : Attribute
{
    public AgentCapabilityAttribute()
    {
    }

    public AgentCapabilityAttribute(string capabilityId)
    {
        CapabilityId = capabilityId;
    }

    /// <summary>
    /// Stable capability identifier. Defaults to the type name converted to snake case,
    /// with a trailing "Capabilities" suffix removed when present.
    /// </summary>
    public string? CapabilityId { get; }

    /// <summary>
    /// Human-readable title shown in capability listings and developer tooling.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Longer description of the capability area.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional category used for grouping capabilities in tooling and projections.
    /// </summary>
    public string? Category { get; set; }
}
