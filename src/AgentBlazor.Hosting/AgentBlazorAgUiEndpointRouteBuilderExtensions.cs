using AgentBlazor.Telemetry;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

        _ = endpoints.ServiceProvider.GetRequiredService<IAgentBlazorTelemetrySink>();
        var agent = endpoints.ServiceProvider.GetRequiredService<DeterministicAgUiHostedAgent>();

        return endpoints.MapAGUI(pattern, agent)
            .WithDisplayName("AgentBlazor AG-UI Run Stream");
    }
}
