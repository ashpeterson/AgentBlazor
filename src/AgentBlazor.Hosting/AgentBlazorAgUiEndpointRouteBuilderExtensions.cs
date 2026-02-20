using AgentBlazor.Telemetry;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Hosting;

public static class AgentBlazorAgUiEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAgentBlazorEndpoints(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/agentblazor/agui/run")
        => endpoints.MapAgentBlazorAgUiRun(pattern);

    public static IEndpointConventionBuilder MapAgentBlazorAgUiRun(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/agentblazor/agui/run")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var factory = endpoints.ServiceProvider.GetRequiredService<AgentBlazorHostedAgentFactory>();
        var telemetrySink = endpoints.ServiceProvider.GetRequiredService<IAgentBlazorTelemetrySink>();
        var logger = endpoints.ServiceProvider.GetService<ILogger<FactoryBackedHostedAgent>>();
        var agent = new FactoryBackedHostedAgent(factory, telemetrySink, logger);

        return endpoints.MapAGUI(pattern, agent)
            .WithDisplayName("AgentBlazor AG-UI Run Stream");
    }
}
