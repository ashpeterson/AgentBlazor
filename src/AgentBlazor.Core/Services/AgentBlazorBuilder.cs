using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Options;
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

    /// <summary>
    /// Enables prompt tracing for observability and debugging.
    /// When enabled, all prompt requests are traced through the pipeline with timing and results.
    /// </summary>
    /// <param name="configure">Optional configuration for tracing options.</param>
    /// <returns>The builder for chaining.</returns>
    public AgentBlazorBuilder EnablePromptTracing(Action<PromptTracingOptions>? configure = null)
    {
        Services.Configure<PromptTracingOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });
        return this;
    }
}
