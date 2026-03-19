using AgentBlazor.Agents;
using AgentBlazor.Components;

namespace AgentBlazor.Services;

internal sealed class AgentBlazorConfigurationStore
{
    public List<AgentRegistration> AgentRegistrations { get; } = [];

    public List<Action<ComponentCapabilityCatalogBuilder>> ComponentCatalogConfigurators { get; } = [];

    public List<Type> CapabilityTypes { get; } = [];
}
