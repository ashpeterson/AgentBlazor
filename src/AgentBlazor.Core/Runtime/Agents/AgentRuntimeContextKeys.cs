namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Shared context keys used across runtime, executors, and component wrappers.
/// </summary>
public static class AgentRuntimeContextKeys
{
    public const string SessionId = "agentblazor.session_id";
    public const string RunId = "agentblazor.run_id";
    public const string UserId = "agentblazor.user_id";
}
