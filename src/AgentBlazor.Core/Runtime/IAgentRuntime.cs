namespace AgentBlazor.Runtime;

public interface IAgentRuntime
{
    Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);
}
