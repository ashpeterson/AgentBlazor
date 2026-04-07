using System.Collections.Concurrent;

namespace AgentBlazor.Core.Runtime.Agents;

public sealed record AgentChatActiveRun(
    string ConversationSessionId,
    string RunId,
    string? AgentName,
    string? UserMessage,
    DateTimeOffset StartedAt);

public interface IAgentChatActiveRunStore
{
    bool TryGet(string conversationSessionId, out AgentChatActiveRun activeRun);

    void Track(AgentChatActiveRun activeRun);

    bool Clear(string conversationSessionId, string? runId = null);
}

internal sealed class AgentChatActiveRunStore : IAgentChatActiveRunStore
{
    private readonly ConcurrentDictionary<string, AgentChatActiveRun> _activeRuns =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string conversationSessionId, out AgentChatActiveRun activeRun)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationSessionId);
        return _activeRuns.TryGetValue(conversationSessionId, out activeRun!);
    }

    public void Track(AgentChatActiveRun activeRun)
    {
        ArgumentNullException.ThrowIfNull(activeRun);
        _activeRuns[activeRun.ConversationSessionId] = activeRun;
    }

    public bool Clear(string conversationSessionId, string? runId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationSessionId);

        if (!_activeRuns.TryGetValue(conversationSessionId, out var existing))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(runId) &&
            !string.Equals(existing.RunId, runId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _activeRuns.TryRemove(conversationSessionId, out _);
    }
}
