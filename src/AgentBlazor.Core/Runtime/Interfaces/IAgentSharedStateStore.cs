using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Storage for agent/ui shared state keyed by agent, session, and run.
/// </summary>
public interface IAgentSharedStateStore
{
    AgentSharedStateSnapshot GetSnapshot(
        string agentName,
        string sessionId,
        string? runId = null);

    AgentSharedStateSnapshot SaveSnapshot(
        string agentName,
        string sessionId,
        string runId,
        IReadOnlyDictionary<string, string> values,
        DateTimeOffset? updatedAt = null);

    AgentSharedStateDelta ApplyDelta(
        string agentName,
        string sessionId,
        string runId,
        IReadOnlyDictionary<string, string?> changes,
        DateTimeOffset? updatedAt = null);

    void AssociateMessageWithRun(
        string agentName,
        string sessionId,
        string messageId,
        string runId);

    string? GetRunIdForMessage(
        string agentName,
        string sessionId,
        string messageId);

    IReadOnlyList<string> GetRunIdsForSession(
        string agentName,
        string sessionId);
}
