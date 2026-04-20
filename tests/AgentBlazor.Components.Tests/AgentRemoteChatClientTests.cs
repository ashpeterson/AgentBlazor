using System.Net;
using System.Text;
using System.Text.Json;
using AgentBlazor.Client.Chat;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Components.Tests;

public sealed class AgentRemoteChatClientTests : TestContext
{
    [Fact]
    public void RemoteSurface_PostsPromptAndRendersAssistantResponse()
    {
        var handler = new RecordingRemoteChatHandler(new
        {
            agentName = "assistant",
            responseText = "Remote answer from server.",
            requiresClarification = false,
            clarificationQuestion = (string?)null,
            requiresApproval = false,
            pendingApprovalCount = 0
        });
        Services.AddSingleton(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://app.test/")
        });

        var cut = RenderComponent<AgentRemoteChatSurface>(parameters => parameters
            .Add(static component => component.AgentName, "assistant")
            .Add(static component => component.SessionId, "wasm-session"));

        cut.Find("[data-testid='agent-remote-chat-input']").Input("Summarize open invoices");
        cut.Find("[data-testid='agent-remote-chat-send']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Summarize open invoices", cut.Markup);
            Assert.Contains("Remote answer from server.", cut.Markup);
            Assert.Equal("/agentblazor/chat/run", handler.RequestPath);
            Assert.Contains("\"userMessage\":\"Summarize open invoices\"", handler.RequestJson, StringComparison.Ordinal);
            Assert.Contains("\"agentName\":\"assistant\"", handler.RequestJson, StringComparison.Ordinal);
            Assert.Contains("\"sessionId\":\"wasm-session\"", handler.RequestJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RemoteSurface_RendersHttpFailure()
    {
        Services.AddSingleton(new HttpClient(new RecordingRemoteChatHandler(HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://app.test/")
        });

        var cut = RenderComponent<AgentRemoteChatSurface>();

        cut.Find("[data-testid='agent-remote-chat-input']").Input("Trigger failure");
        cut.Find("[data-testid='agent-remote-chat-send']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Remote chat failed with HTTP 500.", cut.Markup);
        });
    }

    [Fact]
    public void RemoteWidget_CanMinimizeAndReopen()
    {
        Services.AddSingleton(new HttpClient(new RecordingRemoteChatHandler(new
        {
            agentName = "assistant",
            responseText = "ok",
            requiresClarification = false,
            clarificationQuestion = (string?)null,
            requiresApproval = false,
            pendingApprovalCount = 0
        }))
        {
            BaseAddress = new Uri("https://app.test/")
        });

        var cut = RenderComponent<AgentRemoteChatWidget>(parameters => parameters
            .Add(static component => component.InitiallyOpen, true));

        Assert.Single(cut.FindAll("[data-testid='agent-remote-chat-widget-window']"));

        cut.Find("[data-testid='agent-remote-chat-widget-minimize']").Click();
        Assert.Empty(cut.FindAll("[data-testid='agent-remote-chat-widget-window']"));
        Assert.Single(cut.FindAll("[data-testid='agent-remote-chat-widget-open']"));

        cut.Find("[data-testid='agent-remote-chat-widget-open']").Click();
        Assert.Single(cut.FindAll("[data-testid='agent-remote-chat-widget-window']"));
    }

    [Fact]
    public void RemoteWidget_AllowsHostLayoutOverrides()
    {
        var cut = RenderComponent<AgentRemoteChatWidget>(parameters => parameters
            .Add(static component => component.CssClass, "app-support-widget")
            .Add(static component => component.Style, "right: 5rem; bottom: 6rem; z-index: 42;"));

        var widget = cut.Find("[data-testid='agent-remote-chat-widget']");
        Assert.Contains("app-support-widget", widget.GetAttribute("class"));
        Assert.Equal("right: 5rem; bottom: 6rem; z-index: 42;", widget.GetAttribute("style"));
    }

    [Fact]
    public void RemotePanelAndBar_RenderBrowserSafeSurface()
    {
        Services.AddSingleton(new HttpClient(new RecordingRemoteChatHandler(new
        {
            agentName = "assistant",
            responseText = "ok",
            requiresClarification = false,
            clarificationQuestion = (string?)null,
            requiresApproval = false,
            pendingApprovalCount = 0
        }))
        {
            BaseAddress = new Uri("https://app.test/")
        });

        var panel = RenderComponent<AgentRemoteChatPanel>();
        var bar = RenderComponent<AgentRemoteChatBar>();

        Assert.Single(panel.FindAll("[data-testid='agent-remote-chat-panel']"));
        Assert.Single(panel.FindAll("[data-testid='agent-remote-chat-surface']"));
        Assert.Single(bar.FindAll("[data-testid='agent-remote-chat-bar']"));
        Assert.Single(bar.FindAll("[data-testid='agent-remote-chat-surface']"));
    }

    private sealed class RecordingRemoteChatHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object? _response;

        public RecordingRemoteChatHandler(object response)
        {
            _response = response;
            _statusCode = HttpStatusCode.OK;
        }

        public RecordingRemoteChatHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public string? RequestPath { get; private set; }
        public string RequestJson { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestJson = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = _response is null
                    ? new StringContent(string.Empty)
                    : new StringContent(JsonSerializer.Serialize(_response), Encoding.UTF8, "application/json")
            };
        }
    }
}
