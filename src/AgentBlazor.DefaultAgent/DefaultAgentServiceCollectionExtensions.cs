using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor.DefaultAgent;

[Obsolete("AgentBlazor.DefaultAgent is a legacy compatibility package. Prefer explicit agent registration through AgentBlazorBuilder.AddAgent(...) and the runtime adapter path.", false)]
public static class DefaultAgentServiceCollectionExtensions
{
    [Obsolete("AgentBlazor.DefaultAgent is a legacy compatibility registration. Prefer explicit agent registration through AgentBlazorBuilder.AddAgent(...).", false)]
    public static IServiceCollection AddAgentBlazorDefaultAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDefaultComponentAwareAgentDescriptorProvider, DefaultComponentAwareAgentDescriptorProvider>();
        return services;
    }
}
