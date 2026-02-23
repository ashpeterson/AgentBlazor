namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Registry of which route path hosts which agent component.
/// Populated at runtime when agent components mount (no page-level declaration required).
/// </summary>
public interface IComponentRouteRegistry
{
    /// <summary>
    /// Registers that the given component type is currently at the given path.
    /// Called by agent components when they mount.
    /// </summary>
    void Register(string componentId, string path);

    /// <summary>
    /// Tries to get the route path where the component is hosted.
    /// </summary>
    bool TryGetRoute(string componentId, out string? path);

    /// <summary>
    /// Removes registration for the component (e.g. when it unmounts).
    /// Optional; the registry may overwrite on next mount.
    /// </summary>
    void Unregister(string componentId);
}
