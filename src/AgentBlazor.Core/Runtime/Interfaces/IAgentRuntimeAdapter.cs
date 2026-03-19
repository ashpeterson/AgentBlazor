using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

public interface IAgentRuntimeAdapter
{
    bool SupportsStreaming { get; }

    bool SupportsReconnect { get; }

    bool SupportsCancellation { get; }

    Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        CancellationToken cancellationToken = default);

    Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
