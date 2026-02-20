using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AgentBlazor.Telemetry;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Services;

public static class AgentBlazorServiceCollectionExtensions
{
    public static AgentBlazorBuilder AddAgentBlazorServices(
        this IServiceCollection services,
        Action<AgentBlazorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AgentBlazorOptions>();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        var store = GetOrAddStore(services);

        services.TryAddSingleton(store);
        services.TryAddSingleton<IComponentCapabilityCatalog>(sp => BuildComponentCatalog(
            sp.GetRequiredService<IOptions<AgentBlazorOptions>>().Value,
            sp.GetRequiredService<AgentBlazorConfigurationStore>()));
        services.TryAddSingleton<IAgentRegistry>(sp => BuildAgentRegistry(
            sp.GetRequiredService<IOptions<AgentBlazorOptions>>().Value,
            sp.GetRequiredService<AgentBlazorConfigurationStore>(),
            sp.GetRequiredService<IComponentCapabilityCatalog>()));
        services.TryAddSingleton<IDataGridActionExecutor, NoOpDataGridActionExecutor>();
        services.TryAddSingleton<IDialogActionExecutor, NoOpDialogActionExecutor>();
        services.TryAddSingleton<IFormActionExecutor, NoOpFormActionExecutor>();
        services.TryAddSingleton<INavigationActionExecutor, NoOpNavigationActionExecutor>();
        services.TryAddSingleton<ITabsActionExecutor, NoOpTabsActionExecutor>();
        services.TryAddSingleton<IAgentChatWidgetState, AgentChatWidgetState>();
        services.TryAddSingleton<IChatWidgetActionExecutor, NoOpChatWidgetActionExecutor>();
        services.TryAddSingleton<IComponentActionExecutor, NoOpComponentActionExecutor>();
        services.TryAddSingleton<IAgentComponentRegistry, InMemoryAgentComponentRegistry>();
        services.TryAddSingleton<IAgentRuntime, AgentRuntime>();
        services.TryAddSingleton<IAgentBlazorTelemetrySink, NoOpAgentBlazorTelemetrySink>();
        services.TryAddSingleton<IAgentNavigationIntentService, InMemoryAgentNavigationIntentService>();

        return new AgentBlazorBuilder(services, store);
    }

    public static AgentBlazorBuilder AgentBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new AgentBlazorBuilder(services, GetOrAddStore(services));
    }

    private static AgentBlazorConfigurationStore GetOrAddStore(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(static d => d.ServiceType == typeof(AgentBlazorConfigurationStore));
        if (descriptor?.ImplementationInstance is AgentBlazorConfigurationStore existing)
        {
            return existing;
        }

        var created = new AgentBlazorConfigurationStore();
        services.AddSingleton(created);
        return created;
    }

    private static IComponentCapabilityCatalog BuildComponentCatalog(
        AgentBlazorOptions options,
        AgentBlazorConfigurationStore store)
    {
        var catalog = options.DefaultAgent.ComponentCatalogMode == ComponentCatalogMode.AllShippedComponents
            ? DefaultShippedComponents.CreateCatalog()
            : new ComponentCapabilityCatalog();

        if (options.DefaultAgent.AllowedComponents.Count > 0 &&
            options.DefaultAgent.ComponentCatalogMode == ComponentCatalogMode.AllShippedComponents)
        {
            var filtered = new ComponentCapabilityCatalog();
            foreach (var componentId in options.DefaultAgent.AllowedComponents)
            {
                if (catalog.TryGet(componentId, out var capability))
                {
                    filtered.AddOrUpdate(capability);
                }
            }

            catalog = filtered;
        }

        var builder = new ComponentCapabilityCatalogBuilder(catalog);
        foreach (var configureCatalog in store.ComponentCatalogConfigurators)
        {
            configureCatalog(builder);
        }

        return catalog;
    }

    private static IAgentRegistry BuildAgentRegistry(
        AgentBlazorOptions options,
        AgentBlazorConfigurationStore store,
        IComponentCapabilityCatalog componentCatalog)
    {
        var registry = new InMemoryAgentRegistry();

        if (options.DefaultAgent.Enabled)
        {
            var allowedComponents = options.DefaultAgent.AllowedComponents.Count > 0
                ? options.DefaultAgent.AllowedComponents.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : componentCatalog.GetComponents()
                    .Select(static c => c.ComponentId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            registry.AddOrUpdate(new AgentRegistration
            {
                Name = options.DefaultAgent.Name,
                Description = options.DefaultAgent.Description,
                Instructions = options.DefaultAgent.Instructions,
                AllowedComponents = allowedComponents,
                AllowedActions = options.DefaultAgent.AllowedActions.ToHashSet(StringComparer.OrdinalIgnoreCase)
            });
        }

        foreach (var registration in store.AgentRegistrations)
        {
            registry.AddOrUpdate(registration);
        }

        return registry;
    }
}
