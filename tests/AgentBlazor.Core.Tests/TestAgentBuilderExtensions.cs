using AgentBlazor.Agents;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

internal static class TestAgentBuilderExtensions
{
    public static AgentBlazorBuilder AddBuiltInUiAgent(
        this AgentBlazorBuilder builder,
        Action<AgentRegistrationBuilder>? configure = null)
    {
        return builder.AddAgent("AgentBlazor UI Agent", agent =>
        {
            agent.WithDescription("Built-in AgentBlazor agent with first-party awareness of shipped UI components.");
            agent.WithInstructions("You are AgentBlazor's built-in component-aware agent. Prefer shipped components and their declared capabilities.");
            configure?.Invoke(agent);
        });
    }
}
