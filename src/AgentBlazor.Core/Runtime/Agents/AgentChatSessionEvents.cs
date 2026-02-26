namespace AgentBlazor.Core.Runtime.Agents;

public sealed record AgentChatSessionUpdate(
    string SessionId,
    string SourceId);

public interface IAgentChatSessionEvents
{
    event Action<AgentChatSessionUpdate>? SessionUpdated;

    void NotifySessionUpdated(string sessionId, string sourceId);
}

internal sealed class AgentChatSessionEvents : IAgentChatSessionEvents
{
    public event Action<AgentChatSessionUpdate>? SessionUpdated;

    public void NotifySessionUpdated(string sessionId, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        SessionUpdated?.Invoke(new AgentChatSessionUpdate(sessionId, sourceId));
    }
}
