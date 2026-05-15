using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Data;

namespace AgentBlazor.Services;

internal sealed class AgentBlazorConfigurationStore
{
    public List<AgentRegistration> AgentRegistrations { get; } = [];

    public List<Action<ComponentCapabilityCatalogBuilder>> ComponentCatalogConfigurators { get; } = [];

    public List<Type> CapabilityTypes { get; } = [];

    public List<AgentDataSchemaSet> DataSchemaSets { get; } = [];

    public List<Func<IServiceProvider, AgentDataSchemaSet>> DataSchemaFactories { get; } = [];
}
