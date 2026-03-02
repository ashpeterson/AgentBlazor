using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Middleware;

/// <summary>
/// Builds and executes a middleware pipeline around AgentRuntime turns.
/// Middlewares are registered at startup and execute in registration order (FIFO outer→inner).
/// </summary>
public sealed class AgentMiddlewarePipeline
{
    private readonly IReadOnlyList<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>> _middlewares;

    public AgentMiddlewarePipeline(
        IReadOnlyList<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>>? middlewares = null)
    {
        _middlewares = middlewares ?? [];
    }

    public bool HasMiddlewares => _middlewares.Count > 0;

    /// <summary>
    /// Runs the middleware pipeline wrapping <paramref name="core"/>.
    /// If a middleware short-circuits by setting <see cref="AgentTurnContext.Response"/>,
    /// the core function is skipped.
    /// </summary>
    public async Task<AgentTurnResponse> RunAsync(
        AgentTurnRequest request,
        Func<AgentTurnRequest, CancellationToken, Task<AgentTurnResponse>> core,
        CancellationToken cancellationToken = default)
    {
        var context = new AgentTurnContext(request);

        if (_middlewares.Count == 0)
        {
            return await core(request, cancellationToken);
        }

        // Build chain from outermost to innermost (FIFO)
        Func<CancellationToken, Task> pipeline = async ct =>
        {
            if (!context.IsShortCircuited)
                context.Response = await core(context.Request, ct);
        };

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var current = _middlewares[i];
            var next = pipeline;
            pipeline = ct => current(context, next, ct);
        }

        await pipeline(cancellationToken);

        return context.Response
            ?? throw new InvalidOperationException("Middleware pipeline completed without setting a response.");
    }
}
