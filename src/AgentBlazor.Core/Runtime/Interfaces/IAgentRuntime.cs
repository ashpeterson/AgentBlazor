using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Legacy runtime seam retained for compatibility while AgentBlazor transitions
/// to adapter-first runtime integration. New UI/hosting consumers should prefer
/// <see cref="IAgentRuntimeAdapter"/>.
/// </summary>
public interface IAgentRuntime
{
    Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default);
}
