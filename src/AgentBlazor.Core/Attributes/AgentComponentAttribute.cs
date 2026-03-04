namespace AgentBlazor.Attributes;

/// <summary>
/// Optional class-level configuration for agent-controllable components.
/// Works with <see cref="AgentControllableComponentBase"/> to reduce boilerplate.
/// </summary>
/// <example>
/// [AgentComponent(ComponentType = "SupplierStatusPanel", AgentIdPrefix = "supplier-panel")]
/// public partial class SupplierStatusPanel : AgentControllableComponentBase { }
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AgentComponentAttribute : Attribute
{
    /// <summary>
    /// Optional explicit component type exposed to the runtime.
    /// Defaults to the class name.
    /// </summary>
    public string? ComponentType { get; set; }

    /// <summary>
    /// Optional explicit default AgentId.
    /// If omitted, runtime will use AgentIdPrefix + random suffix.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Optional prefix used when generating AgentId automatically.
    /// Defaults to a kebab-cased component name.
    /// </summary>
    public string? AgentIdPrefix { get; set; }
}

