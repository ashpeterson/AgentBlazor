using AgentBlazor.Core.Runtime.Conversation;
using ExecutionTurn = AgentBlazor.Core.Runtime.ExecutionPlans.ConversationTurn;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeConversationHistory
{
    public static IReadOnlyList<ExecutionTurn> ToExecutionTurns(
        ConversationHistory? history,
        int maxTurns = 10)
    {
        if (history is null || history.Turns.Count == 0)
        {
            return [];
        }

        var executionTurns = new List<ExecutionTurn>(history.Turns.Count * 2);
        foreach (var turn in history.Turns.TakeLast(maxTurns))
        {
            if (!string.IsNullOrWhiteSpace(turn.UserMessage))
            {
                executionTurns.Add(new ExecutionTurn
                {
                    Role = "user",
                    Content = turn.UserMessage
                });
            }

            if (!string.IsNullOrWhiteSpace(turn.AgentResponse))
            {
                executionTurns.Add(new ExecutionTurn
                {
                    Role = "assistant",
                    Content = turn.AgentResponse
                });
            }
        }

        return executionTurns;
    }
}
