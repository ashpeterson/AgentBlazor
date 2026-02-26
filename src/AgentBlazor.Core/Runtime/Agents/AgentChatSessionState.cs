namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Provides a default scoped session id for chat components so multiple chat
/// surfaces can share one conversation unless explicitly overridden.
/// </summary>
public interface IAgentChatSessionState
{
    string SessionId { get; }
}

internal sealed class AgentChatSessionState : IAgentChatSessionState
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
}
