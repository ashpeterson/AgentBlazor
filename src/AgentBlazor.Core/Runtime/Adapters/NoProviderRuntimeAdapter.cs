using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Adapters;

internal sealed class NoProviderRuntimeAdapter : IAgentRuntimeAdapter
{
    public bool SupportsStreaming => false;

    public bool SupportsReconnect => false;

    public bool SupportsCancellation => false;

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(RuntimeEarlyExitResponses.BuildProviderMissingResponse(
            request.AgentName ?? "none"));
    }

    public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        throw new NotSupportedException("The configured runtime adapter does not support streaming.");
    }

    public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = runId;
        _ = cancellationToken;
        throw new NotSupportedException("The configured runtime adapter does not support reconnecting to streaming runs.");
    }

    public Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        _ = runId;
        _ = cancellationToken;
        throw new NotSupportedException("The configured runtime adapter does not support stopping streaming runs.");
    }
}
