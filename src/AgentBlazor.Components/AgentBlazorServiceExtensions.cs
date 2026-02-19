using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

    public static IServiceCollection AddAgentBlazorDataGridExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, AgentBlazor.Runtime.IDataGridActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AgentBlazor.Runtime.IDataGridActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorDialogExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, AgentBlazor.Runtime.IDialogActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AgentBlazor.Runtime.IDialogActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorFormExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, AgentBlazor.Runtime.IFormActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AgentBlazor.Runtime.IFormActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorNavigationExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, AgentBlazor.Runtime.INavigationActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AgentBlazor.Runtime.INavigationActionExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddAgentBlazorTabsExecutor<TExecutor>(this IServiceCollection services)
        where TExecutor : class, AgentBlazor.Runtime.ITabsActionExecutor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<AgentBlazor.Runtime.ITabsActionExecutor, TExecutor>();
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
}
