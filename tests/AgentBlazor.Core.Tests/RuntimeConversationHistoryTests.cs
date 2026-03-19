using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Conversation;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimeConversationHistoryTests
{
    [Fact]
    public void ToPlannerTurns_FlattensRecentConversationTurns()
    {
        var history = new ConversationHistory
        {
            SessionId = "session-1",
            Turns =
            [
                new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = "first user",
                    AgentResponse = "first agent"
                },
                new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = "second user",
                    AgentResponse = "second agent"
                }
            ]
        };

        var plannerTurns = RuntimeConversationHistory.ToPlannerTurns(history);

        Assert.Collection(
            plannerTurns,
            turn =>
            {
                Assert.Equal("user", turn.Role);
                Assert.Equal("first user", turn.Content);
            },
            turn =>
            {
                Assert.Equal("assistant", turn.Role);
                Assert.Equal("first agent", turn.Content);
            },
            turn =>
            {
                Assert.Equal("user", turn.Role);
                Assert.Equal("second user", turn.Content);
            },
            turn =>
            {
                Assert.Equal("assistant", turn.Role);
                Assert.Equal("second agent", turn.Content);
            });
    }

    [Fact]
    public void ToPlannerTurns_RespectsRecentTurnLimit()
    {
        var history = new ConversationHistory
        {
            SessionId = "session-1",
            Turns =
            [
                new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = "u1",
                    AgentResponse = "a1"
                },
                new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = "u2",
                    AgentResponse = "a2"
                },
                new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = "u3",
                    AgentResponse = "a3"
                }
            ]
        };

        var plannerTurns = RuntimeConversationHistory.ToPlannerTurns(history, maxTurns: 2);

        Assert.Equal(4, plannerTurns.Count);
        Assert.Equal("u2", plannerTurns[0].Content);
        Assert.Equal("a3", plannerTurns[^1].Content);
    }
}
