using System.Collections.Concurrent;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Routing;

/// <summary>
/// In-memory component→route registry. Populated when agent components mount.
/// </summary>
internal sealed class InMemoryComponentRouteRegistry : IComponentRouteRegistry
{
    private readonly ConcurrentDictionary<string, string> _componentToPath = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string componentId, string path)
    {
        if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(path))
            return;
        var normalized = path.Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        var queryIndex = normalized.IndexOf('?');
        if (queryIndex >= 0)
            normalized = normalized[..queryIndex];
        _componentToPath[componentId] = normalized;
    }

    public bool TryGetRoute(string componentId, out string? path)
    {
        if (_componentToPath.TryGetValue(componentId, out path) && !string.IsNullOrWhiteSpace(path))
            return true;
        var fallback = componentId.StartsWith("Agent", StringComparison.OrdinalIgnoreCase)
            ? componentId[5..]
            : componentId;
        if (_componentToPath.TryGetValue(fallback, out path) && !string.IsNullOrWhiteSpace(path))
            return true;
        path = null;
        return false;
    }

    public void Unregister(string componentId)
    {
        // Keep the last known route to support deterministic navigation and deferred-action routing
        // even when a component is not currently mounted.
        _ = componentId;
    }
}
