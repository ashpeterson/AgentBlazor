using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Planning;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for Part 6: Reasoning Token Streaming.
/// </summary>
public class ReasoningTokenTests
{
    [Fact]
    public void AgentTurnStreamEvent_HasReasoningDelta()
    {
        var ev = new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ReasoningContent,
            ReasoningDelta = "I'm thinking..."
        };

        Assert.Equal(AgentTurnStreamEventKind.ReasoningContent, ev.Kind);
        Assert.Equal("I'm thinking...", ev.ReasoningDelta);
    }

    [Fact]
    public void AgentTurnStreamEventKind_IncludesReasoningEvents()
    {
        var kinds = Enum.GetValues<AgentTurnStreamEventKind>();

        Assert.Contains(AgentTurnStreamEventKind.ReasoningStart, kinds);
        Assert.Contains(AgentTurnStreamEventKind.ReasoningContent, kinds);
        Assert.Contains(AgentTurnStreamEventKind.ReasoningEnd, kinds);
    }

    [Fact]
    public void ActionPlan_HasReasoningContent()
    {
        var plan = new ActionPlan
        {
            Steps = [],
            ReasoningContent = "Let me think about this step by step..."
        };

        Assert.Equal("Let me think about this step by step...", plan.ReasoningContent);
    }

    [Fact]
    public void ActionPlan_ReasoningContentIsNullByDefault()
    {
        var plan = new ActionPlan { Steps = [] };

        Assert.Null(plan.ReasoningContent);
    }

    [Fact]
    public void ActionPlan_HasSystemPromptAndRawResponse()
    {
        var plan = new ActionPlan
        {
            Steps = [],
            SystemPrompt = "# ROLE\nYou are an agent.",
            RawResponse = "{\"message\": \"Done\"}"
        };

        Assert.Equal("# ROLE\nYou are an agent.", plan.SystemPrompt);
        Assert.Equal("{\"message\": \"Done\"}", plan.RawResponse);
    }

    [Fact]
    public void ReasoningStartEvent_HasNoReasoningDelta()
    {
        var ev = new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ReasoningStart,
            AgentName = "TestAgent"
        };

        Assert.Null(ev.ReasoningDelta);
        Assert.Equal("TestAgent", ev.AgentName);
    }

    [Fact]
    public void ReasoningEndEvent_SignalsCompletion()
    {
        var ev = new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.ReasoningEnd
        };

        Assert.Equal(AgentTurnStreamEventKind.ReasoningEnd, ev.Kind);
        Assert.Null(ev.ReasoningDelta);
    }
}
