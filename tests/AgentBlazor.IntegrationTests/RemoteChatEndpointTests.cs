using System.Text.Json;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public sealed class RemoteChatEndpointTests
{
    [Fact]
    public async Task RunAsync_ForwardsRequestToRuntimeAdapter()
    {
        var adapter = new RecordingRuntimeAdapter();
        var result = await AgentBlazorRemoteChatEndpoint.RunAsync(
            new AgentBlazorRemoteChatRequest(
                "summarize customer risk",
                "assistant",
                "wasm-session",
                "user-1",
                new Dictionary<string, string> { ["route"] = "/orders" }),
            adapter);

        var json = await ExecuteResultAsync(result);

        Assert.Equal("summarize customer risk", adapter.LastRequest?.UserMessage);
        Assert.Equal("assistant", adapter.LastRequest?.AgentName);
        Assert.Equal("wasm-session", adapter.LastRequest?.SessionId);
        Assert.Equal("user-1", adapter.LastRequest?.UserId);
        Assert.Equal("/orders", adapter.LastRequest?.Context?["route"]);
        Assert.Contains("\"agentName\":\"assistant\"", json, StringComparison.Ordinal);
        Assert.Contains("\"responseText\":\"Remote runtime response.\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RejectsEmptyPrompt()
    {
        var adapter = new RecordingRuntimeAdapter();
        var result = await AgentBlazorRemoteChatEndpoint.RunAsync(
            new AgentBlazorRemoteChatRequest(" ", null, null, null, null),
            adapter);

        var context = await ExecuteContextAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(adapter.LastRequest);
    }

    private static async Task<string> ExecuteResultAsync(IResult result)
    {
        var context = await ExecuteContextAsync(result);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private static async Task<DefaultHttpContext> ExecuteContextAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        return context;
    }

    private sealed class RecordingRuntimeAdapter : IAgentRuntimeAdapter
    {
        public bool SupportsStreaming => false;
        public bool SupportsReconnect => false;
        public bool SupportsCancellation => false;
        public AgentTurnRequest? LastRequest { get; private set; }

        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(new AgentTurnResponse("assistant", "Remote runtime response.", [], []));
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
            AgentTurnRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            return Task.FromResult(false);
        }
    }
}
