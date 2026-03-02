namespace AgentBlazor.Core.Runtime.Middleware;

public interface IAgentTurnMiddleware
{
    Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct = default);
}
