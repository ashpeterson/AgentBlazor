using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

public interface IAgentRuntime
{
    Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);
}
