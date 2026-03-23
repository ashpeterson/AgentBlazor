namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Shared context keys used across runtime, executors, and component wrappers.
/// </summary>
public static class AgentRuntimeContextKeys
{
    public const string SessionId = "agentblazor.session_id";
    public const string RunId = "agentblazor.run_id";
    public const string UserId = "agentblazor.user_id";
    public const string AgentName = "agentblazor.agent_name";
    public const string AgentLock = "agentblazor.agent_lock";
    public const string AgentHandoffFrom = "agentblazor.agent_handoff_from";
    public const string AgentHandoffTo = "agentblazor.agent_handoff_to";
    public const string AgentHandoffAt = "agentblazor.agent_handoff_at";
    public const string CurrentRoute = "agentblazor.current_route";
    public const string ContextVersion = "agentblazor.context_version";
    public const string ProjectLegacyComponentToolAliases = "agentblazor.project_legacy_component_tool_aliases";
    public const string SharedStateSnapshot = "agentblazor.shared_state_snapshot";
    public const string SharedStateDelta = "agentblazor.shared_state_delta";
}
