using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Components;
using AgentBlazor.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AgentBlazor.Telemetry;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Adapters;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Planning;
using AgentBlazor.Core.Runtime.Middleware;
using AgentBlazor.Core.Runtime.Routing;
using AgentBlazor.Core.Runtime.State;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Paid;
using Microsoft.Extensions.AI;

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
        services.TryAddSingleton<IAgentCapabilityRegistry>(sp =>
            new ReflectionAgentCapabilityRegistry(
                sp.GetRequiredService<AgentBlazorConfigurationStore>().CapabilityTypes));
        services.TryAddSingleton<IAgentRegistry>(sp => BuildAgentRegistry(
            sp.GetRequiredService<IOptions<AgentBlazorOptions>>().Value,
            sp.GetRequiredService<AgentBlazorConfigurationStore>(),
            sp.GetRequiredService<IComponentCapabilityCatalog>()));

        // Component action executors (specialized, kept for backwards compatibility)
        services.TryAddSingleton<IDataGridActionExecutor, NoOpDataGridActionExecutor>();
        services.TryAddSingleton<IDialogActionExecutor, NoOpDialogActionExecutor>();
        services.TryAddSingleton<IFormActionExecutor, NoOpFormActionExecutor>();
        services.TryAddSingleton<INavigationActionExecutor, NoOpNavigationActionExecutor>();
        services.TryAddSingleton<ITabsActionExecutor, NoOpTabsActionExecutor>();
        services.TryAddSingleton<IChatWidgetActionExecutor, NoOpChatWidgetActionExecutor>();
        services.TryAddSingleton<IComponentActionExecutor, NoOpComponentActionExecutor>();

        // Chat widget state
        services.TryAddSingleton<IAgentChatWidgetState, AgentChatWidgetState>();
        services.TryAddScoped<IAgentChatSessionState, AgentChatSessionState>();
        services.TryAddSingleton<IAgentChatSessionEvents, AgentChatSessionEvents>();
        services.TryAddSingleton<IAgentExecutionScopeAccessor, AgentExecutionScopeAccessor>();

        // Circuit-scoped component registry — each Blazor Server circuit gets its own registry
        services.TryAddSingleton<AgentComponentRegistryHub>();
        services.TryAddScoped<IAgentComponentRegistry, CircuitAgentComponentRegistry>();

        services.TryAddSingleton<IAgentUiToolCatalog, DefaultAgentUiToolCatalog>();

        // Legacy runtime: Plan (AgentPlanner) -> Validate -> Execute.
        // Kept as a compatibility fallback while the adapter-first path becomes default.
        services.TryAddSingleton<IStructuredActionPlanner, AgentPlanner>();
        services.TryAddSingleton<IPlanValidator, PlanValidator>();
        services.TryAddSingleton<IPlanExecutor, PlanExecutor>();
        services.TryAddSingleton<IAgentRuntime, AgentRuntime>();
        services.TryAddSingleton<IAgentRuntimeAdapter>(sp =>
        {
            if (sp.GetService<IChatClient>() is not null)
            {
                return ActivatorUtilities.CreateInstance<ChatClientRuntimeAdapter>(sp);
            }

            return new LegacyAgentRuntimeAdapter(sp.GetRequiredService<IAgentRuntime>());
        });

        services.TryAddSingleton<IAgentBlazorTelemetrySink, NoOpAgentBlazorTelemetrySink>();
        services.TryAddSingleton<IAgentNavigationIntentService, InMemoryAgentNavigationIntentService>();
        services.TryAddSingleton<IAgentDeferredActionEvents, AgentDeferredActionEvents>();

        // Conversation store — default InMemory (can be replaced via builder.UseConversationStore*)
        services.AddOptions<ConversationOptions>();
        services.AddOptions<SharedStateOptions>();
        services.TryAddSingleton<IConversationStore, InMemoryConversationStore>();
        services.TryAddSingleton<IAgentSharedStateStore, InMemoryAgentSharedStateStore>();

        // Route registry for navigation planning
        services.TryAddSingleton<IRouteRegistry, InMemoryRouteRegistry>();

        // Prompt tracing (opt-in via EnablePromptTracing)
        services.AddOptions<PromptTracingOptions>();
        services.TryAddSingleton<IPromptTraceStore, InMemoryPromptTraceStore>();
        services.TryAddSingleton<IPromptTraceService, PromptTraceService>();

        // Paid-tier features — default to no-op implementations
        services.TryAddSingleton<IActionHistoryStore, NullActionHistoryStore>();
        services.TryAddSingleton<IAdaptiveSuggestionService, StaticSuggestionService>();
        services.TryAddSingleton<IProactiveInsightService, NullProactiveInsightService>();
        services.TryAddSingleton<IAgentInspectorStore, NullAgentInspectorStore>();

        // Service tools + MCP (null defaults so runtime works without them)
        services.TryAddSingleton<IAgentServiceToolRegistry, InMemoryAgentServiceToolRegistry>();
        services.TryAddSingleton<IMcpToolProvider, NullMcpToolProvider>();

        // Middleware pipeline (empty by default)
        services.TryAddSingleton(new AgentMiddlewarePipeline());

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
