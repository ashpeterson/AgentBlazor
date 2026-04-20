using AgentBlazor.Components.Render;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Adapters;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor;

public static class AgentBlazorServiceExtensions
{
    public static IServiceCollection AddAgentBlazor(
        this IServiceCollection services,
        Action<AgentBlazorRegistrationOptions>? configure = null)
    {
        AgentBlazor.Services.AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(
            services,
            configure);

        // Component-layer scoped services
        services.TryAddScoped<IAgentActionRenderRegistry, InMemoryAgentActionRenderRegistry>();

        return services;
    }

    public static IServiceCollection AddAgentBlazorTelemetrySink<TSink>(this IServiceCollection services)
        where TSink : class, AgentBlazor.Telemetry.IAgentBlazorTelemetrySink
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AgentBlazor.Telemetry.IAgentBlazorTelemetrySink>(
            static sp => sp.GetRequiredService<TSink>());
        return services;
    }

    public static IServiceCollection AddAgentBlazorRuntimeAdapter<TAdapter>(this IServiceCollection services)
        where TAdapter : class, IAgentRuntimeAdapter
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAgentRuntimeAdapter, TAdapter>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorChatClientRuntimeAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IAgentRuntimeAdapter, ChatClientRuntimeAdapter>());
        return services;
    }

    public static IServiceCollection AddAgentBlazorDataGridExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, IDataGridActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDataGridActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorDialogExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, IDialogActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDialogActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorFormExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, IFormActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IFormActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorNavigationExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, INavigationActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<INavigationActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorTabsExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, ITabsActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ITabsActionExecutor, TExecutor>();
        return services;
    }

    public static IEndpointConventionBuilder MapAgentBlazorAgUiRun(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/agentblazor/agui/run")
    {
        return AgentBlazor.Hosting.AgentBlazorAgUiEndpointRouteBuilderExtensions.MapAgentBlazorAgUiRun(
            endpoints,
            pattern);
    }

    public static IEndpointConventionBuilder MapAgentBlazorEndpoints(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/agentblazor/agui/run")
    {
        return AgentBlazor.Hosting.AgentBlazorAgUiEndpointRouteBuilderExtensions.MapAgentBlazorEndpoints(
            endpoints,
            pattern);
    }

    public static IEndpointConventionBuilder MapAgentBlazorRemoteChat(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/agentblazor/chat/run")
    {
        return AgentBlazor.Hosting.AgentBlazorRemoteChatEndpointRouteBuilderExtensions.MapAgentBlazorRemoteChat(
            endpoints,
            pattern);
    }
}
