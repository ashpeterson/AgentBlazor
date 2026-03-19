using AgentBlazor.Core.Runtime.Conversation;
using PlannerTurn = AgentBlazor.Core.Runtime.Planning.ConversationTurn;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeConversationHistory
{
    public static IReadOnlyList<PlannerTurn> ToPlannerTurns(
        ConversationHistory? history,
        int maxTurns = 10)
    {
        if (history is null || history.Turns.Count == 0)
        {
            return [];
        }

        var plannerTurns = new List<PlannerTurn>(history.Turns.Count * 2);
        foreach (var turn in history.Turns.TakeLast(maxTurns))
        {
            if (!string.IsNullOrWhiteSpace(turn.UserMessage))
            {
                plannerTurns.Add(new PlannerTurn
                {
                    Role = "user",
                    Content = turn.UserMessage
                });
            }

            if (!string.IsNullOrWhiteSpace(turn.AgentResponse))
            {
                plannerTurns.Add(new PlannerTurn
                {
                    Role = "assistant",
                    Content = turn.AgentResponse
                });
            }
        }

        return plannerTurns;
    }
}
