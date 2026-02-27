namespace AgentBlazor.Core.Runtime.Interfaces;

public interface IAgentComponentRegistry
{
    /// <summary>
    /// Unique identifier for this registry instance (equals the circuit's session ID
    /// in Blazor Server). Used by the runtime to look up the correct registry for a request.
    /// </summary>
    string SessionId { get; }

    void Register(IAgentControllable component);

    bool Unregister(string agentId);

    bool TryGet(string agentId, out IAgentControllable component);

    IReadOnlyCollection<IAgentControllable> GetAll();
}
