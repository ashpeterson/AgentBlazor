using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Options;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Tests;

public class ConversationStoreTests
{
    [Fact]
    public async Task AppendTurnAsync_CreatesSessiOnIfNotExists()
    {
        var store = CreateStore();
        var turn = CreateTurn("Hello", "Hi there!");

        await store.AppendTurnAsync("session-1", turn);

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.Single(history.Turns);
    }

    [Fact]
    public async Task AppendTurnAsync_AppendsToExistingSession()
    {
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("Hello", "Hi!"));
        await store.AppendTurnAsync("session-1", CreateTurn("How are you?", "I'm fine!"));
        await store.AppendTurnAsync("session-1", CreateTurn("Goodbye", "Bye!"));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.Equal(3, history.Turns.Count);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsNullForNonexistentSession()
    {
        var store = CreateStore();

        var history = await store.GetHistoryAsync("nonexistent");

        Assert.Null(history);
    }

    [Fact]
    public async Task ClearSessionAsync_RemovesSession()
    {
        var store = CreateStore();
        await store.AppendTurnAsync("session-1", CreateTurn("Hello", "Hi!"));

        await store.ClearSessionAsync("session-1");

        var history = await store.GetHistoryAsync("session-1");
        Assert.Null(history);
    }

    [Fact]
    public async Task AppendTurnAsync_TruncatesWhenMaxTurnsExceeded()
    {
        var options = Options.Create(new ConversationOptions
        {
            MaxTurnsPerSession = 3,
            SessionTimeout = TimeSpan.FromHours(1)
        });
        var store = new InMemoryConversationStore(options);

        await store.AppendTurnAsync("session-1", CreateTurn("1", "A"));
        await store.AppendTurnAsync("session-1", CreateTurn("2", "B"));
        await store.AppendTurnAsync("session-1", CreateTurn("3", "C"));
        await store.AppendTurnAsync("session-1", CreateTurn("4", "D"));
        await store.AppendTurnAsync("session-1", CreateTurn("5", "E"));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.Equal(3, history.Turns.Count);
        // Should keep the most recent turns
        Assert.Equal("3", history.Turns[0].UserMessage);
        Assert.Equal("4", history.Turns[1].UserMessage);
        Assert.Equal("5", history.Turns[2].UserMessage);
    }

    [Fact]
    public async Task ConversationHistory_TracksTimestamps()
    {
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("Hello", "Hi!"));
        await Task.Delay(10);
        await store.AppendTurnAsync("session-1", CreateTurn("Update", "Updated!"));

        var history = await store.GetHistoryAsync("session-1");
        Assert.NotNull(history);
        Assert.True(history.LastActivityAt >= history.CreatedAt);
    }

    [Fact]
    public async Task MultipleSessions_AreIsolated()
    {
        var store = CreateStore();

        await store.AppendTurnAsync("session-1", CreateTurn("Session 1", "Response 1"));
        await store.AppendTurnAsync("session-2", CreateTurn("Session 2", "Response 2"));

        var history1 = await store.GetHistoryAsync("session-1");
        var history2 = await store.GetHistoryAsync("session-2");

        Assert.NotNull(history1);
        Assert.NotNull(history2);
        Assert.NotEqual(history1.SessionId, history2.SessionId);
        Assert.Equal("Session 1", history1.Turns[0].UserMessage);
        Assert.Equal("Session 2", history2.Turns[0].UserMessage);
    }

    [Fact]
    public void ConversationTurn_StoresAllProperties()
    {
        var timestamp = DateTime.UtcNow;
        var plannedActions = new List<PlannedComponentAction>();
        var executionResults = new List<ComponentActionExecutionResult>();

        var turn = new ConversationTurn
        {
            Timestamp = timestamp,
            UserMessage = "Test message",
            AgentResponse = "Test response",
            PlannedActions = plannedActions,
            ExecutionResults = executionResults
        };

        Assert.Equal(timestamp, turn.Timestamp);
        Assert.Equal("Test message", turn.UserMessage);
        Assert.Equal("Test response", turn.AgentResponse);
        Assert.Same(plannedActions, turn.PlannedActions);
        Assert.Same(executionResults, turn.ExecutionResults);
    }

    private static InMemoryConversationStore CreateStore()
    {
        var options = Options.Create(new ConversationOptions
        {
            MaxTurnsPerSession = 50,
            SessionTimeout = TimeSpan.FromHours(24)
        });
        return new InMemoryConversationStore(options);
    }

    private static ConversationTurn CreateTurn(string userMessage, string agentResponse) => new()
    {
        Timestamp = DateTime.UtcNow,
        UserMessage = userMessage,
        AgentResponse = agentResponse,
        PlannedActions = [],
        ExecutionResults = []
    };
}
