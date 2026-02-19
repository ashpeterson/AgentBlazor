namespace AgentBlazor.Runtime;

public interface IAgentComponentRegistry
{
    void Register(IAgentControllable component);

    bool Unregister(string agentId);

    bool TryGet(string agentId, out IAgentControllable component);

    IReadOnlyCollection<IAgentControllable> GetAll();
}
