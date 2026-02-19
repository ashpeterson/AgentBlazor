using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor.Hosting;

public static class AgentBlazorHostingServiceCollectionExtensions
{
    public static IServiceCollection AddAgentBlazorHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAGUI();
        services.TryAddSingleton<AgentBlazorHostedAgentFactory>();
        return services;
    }
}
