using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class MiddlewareIntegrationTests
{
    [Fact]
    public async Task AddAgentBlazor_InlineMiddleware_CanShortCircuitRuntimeTurns()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazor(options =>
        {
            options.UseChatClientRuntimeAdapter();
            options.UseMiddleware((context, _, _) =>
            {
                context.Response = new AgentTurnResponse("chat-agent", "short-circuited", [], []);
                return Task.CompletedTask;
            });
            options.ConfigureBuilder(builder => builder.AddAgent("chat-agent"));
        });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            AgentName: "chat-agent",
            SessionId: "middleware-inline"));

        Assert.Equal("short-circuited", response.ResponseText);
        Assert.Empty(chatClient.Requests);
    }

    [Fact]
    public async Task AddAgentBlazor_TypedMiddleware_UsesScopedDependencies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddScoped<ScopedMiddlewareState>();
        services.AddAgentBlazor(options =>
        {
            options.UseChatClientRuntimeAdapter();
            options.UseMiddleware<ScopedResponseStampMiddleware>();
            options.ConfigureBuilder(builder => builder.AddAgent("chat-agent"));
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var scopeAccessor = provider.GetRequiredService<IAgentExecutionScopeAccessor>();

        await using var scope = provider.CreateAsyncScope();
        var middlewareState = scope.ServiceProvider.GetRequiredService<ScopedMiddlewareState>();

        AgentTurnResponse response;
        using (scopeAccessor.Push(scope.ServiceProvider))
        {
            response = await adapter.RunTurnAsync(new AgentTurnRequest(
                "hello",
                AgentName: "chat-agent",
                SessionId: "middleware-typed"));
        }

        Assert.Equal("response-1|scope:1", response.ResponseText);
        Assert.Equal(1, middlewareState.InvocationCount);
    }

    [Fact]
    public async Task AddAgentBlazor_InlineMiddleware_CanShortCircuitStreamingTurns()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazor(options =>
        {
            options.UseChatClientRuntimeAdapter();
            options.UseMiddleware((context, _, _) =>
            {
                context.Response = new AgentTurnResponse("chat-agent", "stream-short", [], []);
                return Task.CompletedTask;
            });
            options.ConfigureBuilder(builder => builder.AddAgent("chat-agent"));
        });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();
        var events = new List<AgentTurnStreamEvent>();

        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "hello",
                           AgentName: "chat-agent",
                           SessionId: "middleware-streaming-inline")))
        {
            events.Add(streamEvent);
        }

        Assert.Empty(chatClient.Requests);
        Assert.Collection(events,
            static streamEvent => Assert.Equal(AgentTurnStreamEventKind.RunStarted, streamEvent.Kind),
            static streamEvent => Assert.Equal(AgentTurnStreamEventKind.TextMessageStart, streamEvent.Kind),
            static streamEvent =>
            {
                Assert.Equal(AgentTurnStreamEventKind.TextMessageContent, streamEvent.Kind);
                Assert.Equal("stream-short", streamEvent.TextDelta);
            },
            static streamEvent => Assert.Equal(AgentTurnStreamEventKind.TextMessageEnd, streamEvent.Kind),
            static streamEvent =>
            {
                Assert.Equal(AgentTurnStreamEventKind.RunFinished, streamEvent.Kind);
                Assert.Equal("stream-short", streamEvent.Response?.ResponseText);
            });
    }

    private sealed class ScopedMiddlewareState
    {
        public int InvocationCount { get; set; }
    }

    private sealed class ScopedResponseStampMiddleware(ScopedMiddlewareState state) : IAgentTurnMiddleware
    {
        public async Task InvokeAsync(
            AgentTurnContext context,
            Func<CancellationToken, Task> next,
            CancellationToken ct = default)
        {
            state.InvocationCount++;
            await next(ct);

            if (context.Response is not null)
            {
                context.Response = context.Response with
                {
                    ResponseText = $"{context.Response.ResponseText}|scope:{state.InvocationCount}"
                };
            }
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = options;
            _ = cancellationToken;

            var capturedMessages = messages.ToArray();
            Requests.Add(capturedMessages);
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                $"response-{Requests.Count}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
