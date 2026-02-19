using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor.DefaultAgent;

public static class DefaultAgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentBlazorDefaultAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDefaultComponentAwareAgentDescriptorProvider, DefaultComponentAwareAgentDescriptorProvider>();
        return services;
    }
}
