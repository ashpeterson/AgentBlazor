using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Middleware;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for Part 3: Middleware Pipeline.
/// </summary>
public class MiddlewarePipelineTests
{
    private static AgentTurnResponse MakeResponse(string text) =>
        new AgentTurnResponse("TestAgent", text, [], []);

    private static AgentTurnRequest MakeRequest() =>
        new AgentTurnRequest("test message", "TestAgent", SessionId: "session-1");

    [Fact]
    public async Task EmptyPipeline_CallsCoreDirectly()
    {
        var pipeline = new AgentMiddlewarePipeline();
        var called = false;

        var result = await pipeline.RunAsync(
            MakeRequest(),
            (req, ct) =>
            {
                called = true;
                return Task.FromResult(MakeResponse("done"));
            });

        Assert.True(called);
        Assert.Equal("done", result.ResponseText);
    }

    [Fact]
    public async Task SingleMiddleware_RunsBeforeAndAfterCore()
    {
        var log = new List<string>();

        var middlewares = new List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>
        {
            async (ctx, next, ct) =>
            {
                log.Add("before");
                await next(ct);
                log.Add("after");
            }
        };

        var pipeline = new AgentMiddlewarePipeline(middlewares);

        await pipeline.RunAsync(
            MakeRequest(),
            (_, _) =>
            {
                log.Add("core");
                return Task.FromResult(MakeResponse("x"));
            });

        Assert.Equal(["before", "core", "after"], log);
    }

    [Fact]
    public async Task MultipleMiddlewares_ExecuteInFifoOrder()
    {
        var log = new List<string>();

        var middlewares = new List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>
        {
            async (ctx, next, ct) => { log.Add("m1-before"); await next(ct); log.Add("m1-after"); },
            async (ctx, next, ct) => { log.Add("m2-before"); await next(ct); log.Add("m2-after"); }
        };

        var pipeline = new AgentMiddlewarePipeline(middlewares);

        await pipeline.RunAsync(
            MakeRequest(),
            (_, _) => { log.Add("core"); return Task.FromResult(MakeResponse("y")); });

        Assert.Equal(["m1-before", "m2-before", "core", "m2-after", "m1-after"], log);
    }

    [Fact]
    public async Task ShortCircuit_SkipsCore()
    {
        var coreCalled = false;

        var middlewares = new List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>
        {
            (ctx, next, ct) =>
            {
                ctx.Response = MakeResponse("short-circuited");
                return Task.CompletedTask;
            }
        };

        var pipeline = new AgentMiddlewarePipeline(middlewares);

        var result = await pipeline.RunAsync(
            MakeRequest(),
            (_, _) => { coreCalled = true; return Task.FromResult(MakeResponse("core")); });

        Assert.False(coreCalled);
        Assert.Equal("short-circuited", result.ResponseText);
    }

    [Fact]
    public async Task MiddlewareItems_AreAccessibleAcrossMiddlewares()
    {
        var middlewares = new List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>
        {
            async (ctx, next, ct) =>
            {
                ctx.Items["flag"] = "[tested]";
                await next(ct);
            }
        };

        var pipeline = new AgentMiddlewarePipeline(middlewares);

        await pipeline.RunAsync(
            MakeRequest(),
            (req, ct) =>
            {
                return Task.FromResult(MakeResponse("done"));
            });

        // Verify via second middleware seeing the item
        var flagSeen = false;
        var middlewares2 = new List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>
        {
            async (ctx, next, ct) => { ctx.Items["x"] = 1; await next(ct); },
            async (ctx, next, ct) =>
            {
                flagSeen = ctx.Items.ContainsKey("x");
                await next(ct);
            }
        };

        var pipeline2 = new AgentMiddlewarePipeline(middlewares2);
        await pipeline2.RunAsync(MakeRequest(), (_, _) => Task.FromResult(MakeResponse("ok")));

        Assert.True(flagSeen);
    }

    [Fact]
    public async Task Exception_InMiddleware_Propagates()
    {
        var middlewares = new List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>
        {
            (ctx, next, ct) => throw new InvalidOperationException("middleware exploded")
        };

        var pipeline = new AgentMiddlewarePipeline(middlewares);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.RunAsync(MakeRequest(), (_, _) => Task.FromResult(MakeResponse("x"))));
    }
}
