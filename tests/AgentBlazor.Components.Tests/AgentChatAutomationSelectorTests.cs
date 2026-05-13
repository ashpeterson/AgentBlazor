using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Components.Chat;
using AgentBlazor.Components.Render;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Execution;
using AgentBlazor.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Components.Tests;

public sealed class AgentChatAutomationSelectorTests : TestContext
{
    [Fact]
    public void ChatSurface_RendersStableAutomationSelector()
    {
        AddChatServices();

        var cut = RenderComponent<AgentChatSurface>(parameters => parameters
            .Add(static surface => surface.ShowAgentSelector, false)
            .Add(static surface => surface.DefaultAgentName, "Test Agent"));

        Assert.NotNull(cut.Find("[data-testid='agent-chat-surface']"));
        Assert.NotNull(cut.Find("textarea[aria-label='Message input']"));
        Assert.NotNull(cut.Find("button[aria-label='Send message']"));
    }

    [Fact]
    public void ChatPanel_RendersStableAutomationSelector()
    {
        AddChatServices();

        var cut = RenderComponent<AgentChatPanel>(parameters => parameters
            .Add(static panel => panel.ShowAgentSelector, false)
            .Add(static panel => panel.DefaultAgentName, "Test Agent"));

        Assert.NotNull(cut.Find("[data-testid='agent-chat-panel']"));
        Assert.NotNull(cut.Find("[data-testid='agent-chat-surface']"));
    }

    [Fact]
    public void ChatPanel_DefaultDescription_DoesNotShowNoAgentGuidance()
    {
        AddChatServices();

        var cut = RenderComponent<AgentChatPanel>(parameters => parameters
            .Add(static panel => panel.ShowAgentSelector, false)
            .Add(static panel => panel.DefaultAgentName, "Test Agent"));

        Assert.Contains("Ask an agent to work with this Blazor app.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Register an agent", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatWidget_DefaultDescription_DoesNotShowNoAgentGuidance()
    {
        AddChatServices();

        var cut = RenderComponent<AgentChatWidget>(parameters => parameters
            .Add(static widget => widget.ShowAgentSelector, false)
            .Add(static widget => widget.DefaultAgentName, "Test Agent"));

        cut.Find("button.ab-chat-widget__bubble").Click();

        Assert.Contains("Ask an agent to work with this Blazor app.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Register an agent", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatBar_RendersStableAutomationSelectorsAndAccessibleControls()
    {
        AddChatServices();

        var cut = RenderComponent<AgentChatBar>();

        Assert.NotNull(cut.Find("[data-testid='agent-chat-bar']"));
        Assert.NotNull(cut.Find("[data-testid='agent-chat-bar-input'][aria-label='Message input']"));
        Assert.NotNull(cut.Find("[data-testid='agent-chat-bar-send'][aria-label='Send message']"));
    }

    private void AddChatServices()
    {
        Services.AddAgentBlazorServices();
        Services.AgentBlazor().AddAgent("Test Agent");
        Services.AddSingleton<IAgentActionRenderRegistry, NullActionRenderRegistry>();
        Services.AddSingleton<IAgentRuntimeAdapter, EchoRuntimeAdapter>();
    }

    private sealed class EchoRuntimeAdapter : IAgentRuntimeAdapter
    {
        public bool SupportsStreaming => false;

        public bool SupportsReconnect => false;

        public bool SupportsCancellation => false;

        public Task<AgentTurnResponse> RunTurnAsync(
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AgentTurnResponse("Test Agent", $"Echo: {request.UserMessage}", [], []));
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = runId;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class NullActionRenderRegistry : IAgentActionRenderRegistry
    {
        public void Register(string agentId, string actionId, ActionRenderFragments fragments)
        {
            _ = agentId;
            _ = actionId;
            _ = fragments;
        }

        public void Unregister(string agentId, string actionId)
        {
            _ = agentId;
            _ = actionId;
        }

        public ActionRenderFragments? TryGet(string agentId, string actionId)
        {
            _ = agentId;
            _ = actionId;
            return null;
        }
    }
}
