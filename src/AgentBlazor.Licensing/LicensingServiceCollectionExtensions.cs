using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor.Licensing;

public static class LicensingServiceCollectionExtensions
{
    public static IServiceCollection AddAgentBlazorLicensing(
        this IServiceCollection services,
        AgentBlazorTier tier = AgentBlazorTier.Free)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAgentBlazorEntitlementService>(new AgentBlazorEntitlementService(tier));
        return services;
    }
}
