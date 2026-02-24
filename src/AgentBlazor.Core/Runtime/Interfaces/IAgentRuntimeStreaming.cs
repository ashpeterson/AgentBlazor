using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

public interface IAgentRuntimeStreaming
{
    IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);
}
