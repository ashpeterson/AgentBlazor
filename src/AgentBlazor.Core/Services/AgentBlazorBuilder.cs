using AgentBlazor.Agents;
using AgentBlazor.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Services;

public sealed class AgentBlazorBuilder
{
    private readonly AgentBlazorConfigurationStore _store;

    internal AgentBlazorBuilder(IServiceCollection services, AgentBlazorConfigurationStore store)
    {
        Services = services;
        _store = store;
    }

    public IServiceCollection Services { get; }

    public AgentBlazorBuilder AddAgent(string name, Action<AgentRegistrationBuilder>? configure = null)
    {
        var builder = new AgentRegistrationBuilder(name);
        configure?.Invoke(builder);
        var registration = builder.Build();

        _store.AgentRegistrations.RemoveAll(r => string.Equals(r.Name, registration.Name, StringComparison.OrdinalIgnoreCase));
        _store.AgentRegistrations.Add(registration);
        return this;
    }

    public AgentBlazorBuilder ConfigureComponentCatalog(Action<ComponentCapabilityCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _store.ComponentCatalogConfigurators.Add(configure);
        return this;
    }

    public AgentBlazorBuilder UseAgentCapabilityPreset(AgentCapabilityPreset preset)
    {
        _store.ComponentCatalogConfigurators.Add(builder =>
            AgentCapabilityPresets.Apply(builder, preset));
        return this;
    }
}
