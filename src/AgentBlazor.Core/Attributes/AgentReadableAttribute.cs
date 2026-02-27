namespace AgentBlazor.Attributes;

/// <summary>
/// Marks a public property as readable state the agent can see in the system prompt.
/// Apply to properties on components that inherit AgentControllableComponentBase.
/// </summary>
/// <example>
/// [AgentReadable("Currently selected row")]
/// public SupplierRow? SelectedRow { get; private set; }
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AgentReadableAttribute : Attribute
{
    public AgentReadableAttribute(string description)
    {
        Description = description;
    }

    /// <summary>Human-readable description included in the state snapshot.</summary>
    public string Description { get; }

    /// <summary>
    /// Override the state key name. Defaults to the property name camelCased.
    /// </summary>
    public string? StateKey { get; set; }

    /// <summary>
    /// Maximum number of items to include when the property is a collection.
    /// Defaults to 5. Set to -1 for no limit.
    /// </summary>
    public int MaxItems { get; set; } = 5;
}
