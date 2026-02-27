using System.Collections.Concurrent;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Components;

/// <summary>
/// Singleton hub that maps session IDs to their circuit-scoped IAgentComponentRegistry.
/// The runtime uses this to find the correct registry for a given AgentTurnRequest.SessionId.
/// </summary>
public sealed class AgentComponentRegistryHub
{
    private readonly ConcurrentDictionary<string, IAgentComponentRegistry> _registries =
        new(StringComparer.OrdinalIgnoreCase);

    internal void Register(string sessionId, IAgentComponentRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(registry);
        _registries[sessionId] = registry;
    }

    internal void Remove(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            _registries.TryRemove(sessionId, out _);
    }

    public bool TryGet(string sessionId, out IAgentComponentRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            registry = null!;
            return false;
        }

        return _registries.TryGetValue(sessionId, out registry!);
    }

    /// <summary>Returns all currently active registries across all circuits.</summary>
    public IReadOnlyCollection<IAgentComponentRegistry> GetAll()
        => _registries.Values.ToArray();
}
