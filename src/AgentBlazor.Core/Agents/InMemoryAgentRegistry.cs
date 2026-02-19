using System.Collections.Concurrent;

namespace AgentBlazor.Agents;

public sealed class InMemoryAgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<AgentRegistration> GetAll() => _registrations.Values.ToArray();

    public bool TryGet(string name, out AgentRegistration registration) =>
        _registrations.TryGetValue(name, out registration!);

    public void AddOrUpdate(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations[registration.Name] = registration;
    }
}
