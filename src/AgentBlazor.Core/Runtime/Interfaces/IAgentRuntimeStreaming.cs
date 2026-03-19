using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Legacy streaming runtime seam retained for compatibility while AgentBlazor transitions
/// to adapter-first runtime integration. New UI/hosting consumers should prefer
/// <see cref="IAgentRuntimeAdapter"/>.
/// </summary>
public interface IAgentRuntimeStreaming
{
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
