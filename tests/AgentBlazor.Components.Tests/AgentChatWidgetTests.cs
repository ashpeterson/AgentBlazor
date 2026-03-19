using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Components.Tests;

public sealed class AgentChatWidgetTests : TestContext
{
    [Fact]
    public void Render_StartsMinimized_EvenWhenSharedStateWasPreviouslyOpen()
    {
        var state = new TestChatWidgetState();
        state.Open();

        Services.AddSingleton<IAgentChatWidgetState>(state);
        ComponentFactories.AddStub<AgentChatSurface>();

        var cut = RenderComponent<AgentChatWidget>();

        Assert.False(state.IsOpen);
        Assert.DoesNotContain("ab-chat-widget--open", cut.Find("section.ab-chat-widget").ClassName);
    }

    [Fact]
    public void BubbleAndMinimizeButton_ToggleWidgetState()
    {
        var state = new TestChatWidgetState();

        Services.AddSingleton<IAgentChatWidgetState>(state);
        ComponentFactories.AddStub<AgentChatSurface>();

        var cut = RenderComponent<AgentChatWidget>();

        cut.Find("button.ab-chat-widget__bubble").Click();
        Assert.True(state.IsOpen);
        Assert.Contains("ab-chat-widget--open", cut.Find("section.ab-chat-widget").ClassName);

        cut.Find("button.ab-chat-widget__icon-btn").Click();
        Assert.False(state.IsOpen);
        Assert.DoesNotContain("ab-chat-widget--open", cut.Find("section.ab-chat-widget").ClassName);
    }

    private sealed class TestChatWidgetState : IAgentChatWidgetState
    {
        private bool _isOpen;

        public bool IsOpen => _isOpen;

        public event Action? Changed;

        public void Open()
        {
            if (_isOpen)
            {
                return;
            }

            _isOpen = true;
            Changed?.Invoke();
        }

        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            _isOpen = false;
            Changed?.Invoke();
        }

        public void Toggle()
        {
            if (_isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }
    }
}
