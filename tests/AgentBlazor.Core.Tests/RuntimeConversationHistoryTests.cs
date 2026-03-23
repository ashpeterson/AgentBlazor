using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Conversation;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimeConversationHistoryTests
{
    [Fact]
    public void ToExecutionTurns_FlattensRecentConversationTurns()
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

        var executionTurns = RuntimeConversationHistory.ToExecutionTurns(history);

        Assert.Collection(
            executionTurns,
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
    public void ToExecutionTurns_RespectsRecentTurnLimit()
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

        var executionTurns = RuntimeConversationHistory.ToExecutionTurns(history, maxTurns: 2);

        Assert.Equal(4, executionTurns.Count);
        Assert.Equal("u2", executionTurns[0].Content);
        Assert.Equal("a3", executionTurns[^1].Content);
    }
}
