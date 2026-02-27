using AgentBlazor.Core.Runtime.Interfaces;
using System.Collections.Concurrent;

namespace AgentBlazor.Core.Runtime.Components;

internal sealed class InMemoryAgentComponentRegistry : IAgentComponentRegistry
{
    private readonly ConcurrentDictionary<string, IAgentControllable> _components =
        new(StringComparer.OrdinalIgnoreCase);

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public void Register(IAgentControllable component)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(component.AgentId);
        _components[component.AgentId] = component;
    }

    public bool Unregister(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _components.TryRemove(agentId, out _);
    }

    public bool TryGet(string agentId, out IAgentControllable component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _components.TryGetValue(agentId, out component!);
    }

    public IReadOnlyCollection<IAgentControllable> GetAll() => _components.Values.ToArray();
}
