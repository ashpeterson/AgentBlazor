using Microsoft.Extensions.DependencyInjection;
using AgentBlazor.Hosting;
using AgentBlazor;

namespace AgentBlazor.Services;

public static class AgentBlazorUnifiedServiceCollectionExtensions
{
    public static AgentBlazorBuilder AddAgentBlazor(
        this IServiceCollection services,
        Action<AgentBlazorRegistrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registration = new AgentBlazorRegistrationOptions();
        configure?.Invoke(registration);

        registration.ApplyProvider(services);
        services.AddAgentBlazorHosting();
        var builder = services.AddAgentBlazorServices(registration.ApplyOptions);
        registration.ApplyBuilder(builder);

        return builder;
    }
}
