using AgentBlazor.Core.Runtime;
using AgentBlazor.Licensing;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimeEarlyExitResponsesTests
{
    [Fact]
    public void BuildNoAgentResponse_UsesSharedPreflightMessage()
    {
        var response = RuntimeEarlyExitResponses.BuildNoAgentResponse(
            registeredCount: 1,
            requestedAgentName: "sales",
            context: null);

        Assert.Equal("none", response.AgentName);
        Assert.Contains("Requested agent 'sales' is not registered", response.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNoAllowedActionsResponse_UsesSharedPolicyMessage()
    {
        var response = RuntimeEarlyExitResponses.BuildNoAllowedActionsResponse(
            "agent",
            blockedByAgentPolicy: ["AgentForm.submit"],
            blockedByTier: [],
            effectiveTier: AgentBlazorTier.Free,
            actionLabel: "component actions");

        Assert.Equal("agent", response.AgentName);
        Assert.Contains("No allowed component actions are available for this agent policy.", response.ResponseText, StringComparison.Ordinal);
        Assert.Contains("Current tier: Free", response.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProviderMissingResponse_ReturnsSetupGuidance()
    {
        var response = RuntimeEarlyExitResponses.BuildProviderMissingResponse("agent");

        Assert.Equal("agent", response.AgentName);
        Assert.Contains("No AI provider configured", response.ResponseText, StringComparison.Ordinal);
        Assert.Contains("options.UseOpenAI", response.ResponseText, StringComparison.Ordinal);
        Assert.Contains("options.UseOllama", response.ResponseText, StringComparison.Ordinal);
    }
}
