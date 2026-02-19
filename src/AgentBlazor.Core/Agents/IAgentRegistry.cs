namespace AgentBlazor.Agents;

public interface IAgentRegistry
{
    IReadOnlyCollection<AgentRegistration> GetAll();

    bool TryGet(string name, out AgentRegistration registration);

    void AddOrUpdate(AgentRegistration registration);
}
