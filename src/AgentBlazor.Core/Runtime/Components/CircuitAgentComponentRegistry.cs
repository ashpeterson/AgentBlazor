using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Components;

/// <summary>
/// Scoped (per-Blazor-circuit) registry of agent-controllable components.
/// Self-registers with AgentComponentRegistryHub on construction and deregisters on dispose.
/// The SessionId exposed here is the stable key used by AgentTurnRequest to address this circuit.
/// </summary>
internal sealed class CircuitAgentComponentRegistry : IAgentComponentRegistry, IDisposable
{
    private readonly InMemoryAgentComponentRegistry _inner = new();
    private readonly AgentComponentRegistryHub _hub;

    public CircuitAgentComponentRegistry(AgentComponentRegistryHub hub)
    {
        _hub = hub;
        // Use the inner registry's stable session ID and register with the hub
        _hub.Register(SessionId, this);
    }

    /// <summary>
    /// Stable session ID for this circuit. Components and the chat surface both read this
    /// so that every AgentTurnRequest from this circuit carries the same ID.
    /// </summary>
    public string SessionId => _inner.SessionId;

    public void Register(IAgentControllable component) => _inner.Register(component);

    public bool Unregister(string agentId) => _inner.Unregister(agentId);

    public bool TryGet(string agentId, out IAgentControllable component)
        => _inner.TryGet(agentId, out component);

    public IReadOnlyCollection<IAgentControllable> GetAll() => _inner.GetAll();

    public void Dispose() => _hub.Remove(SessionId);
}
