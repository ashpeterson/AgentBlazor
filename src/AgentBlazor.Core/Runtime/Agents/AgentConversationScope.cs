namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Builds scoped conversation session keys for multi-agent chat isolation.
/// </summary>
public static class AgentConversationScope
{
    private const string Separator = "::agent::";

    public static string BuildSessionKey(
        string sessionId,
        string? agentName,
        bool isolateByAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!isolateByAgent || string.IsNullOrWhiteSpace(agentName))
        {
            return sessionId;
        }

        return $"{sessionId}{Separator}{NormalizeAgentName(agentName)}";
    }

    private static string NormalizeAgentName(string agentName)
    {
        var trimmed = agentName.Trim();
        return trimmed.Replace(Separator, "_", StringComparison.OrdinalIgnoreCase);
    }
}
