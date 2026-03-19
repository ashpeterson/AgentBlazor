using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Adapters;

internal sealed class LegacyAgentRuntimeAdapter(IAgentRuntime runtime) : IAgentRuntimeAdapter
{
    private readonly IAgentRuntime _runtime = runtime;
    private readonly IAgentRuntimeStreaming? _streamingRuntime = runtime as IAgentRuntimeStreaming;

    public bool SupportsStreaming => _streamingRuntime is not null;

    public bool SupportsReconnect => _streamingRuntime is not null;

    public bool SupportsCancellation => _streamingRuntime is not null;

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default) =>
        _runtime.RunTurnAsync(request, cancellationToken);

    public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default) =>
        _streamingRuntime?.RunTurnStreamingAsync(request, cancellationToken)
        ?? throw new NotSupportedException("The configured agent runtime does not support streaming.");

    public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        CancellationToken cancellationToken = default) =>
        _streamingRuntime?.ConnectRunStreamAsync(runId, cancellationToken)
        ?? throw new NotSupportedException("The configured agent runtime does not support reconnecting to streaming runs.");

    public Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default) =>
        _streamingRuntime?.StopRunAsync(runId, cancellationToken)
        ?? throw new NotSupportedException("The configured agent runtime does not support stopping streaming runs.");
}
