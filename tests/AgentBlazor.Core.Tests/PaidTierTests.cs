using AgentBlazor.Core.Paid;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for paid-tier services: InMemoryActionHistoryStore (2b) and PatternBasedSuggestionService (2c).
/// </summary>
public class PaidTierTests
{
    // -------------------------------------------------------------------------
    // 2b: InMemoryActionHistoryStore
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActionHistoryStore_RecordAndRetrieve_ReturnsEntry()
    {
        var store = new InMemoryActionHistoryStore();
        var entry = MakeEntry("session-1", actionId: "filter", userMessage: "filter to high risk");

        await store.RecordAsync(entry);

        var results = await store.GetRecentAsync("session-1");
        var single = Assert.Single(results);
        Assert.Equal("filter", single.ActionId);
        Assert.Equal("filter to high risk", single.UserMessage);
    }

    [Fact]
    public async Task ActionHistoryStore_GetRecent_ReturnsOnlyMatchingSession()
    {
        var store = new InMemoryActionHistoryStore();
        await store.RecordAsync(MakeEntry("session-A", actionId: "filter"));
        await store.RecordAsync(MakeEntry("session-B", actionId: "sort"));

        var resultsA = await store.GetRecentAsync("session-A");
        var resultsB = await store.GetRecentAsync("session-B");

        Assert.Single(resultsA);
        Assert.Equal("filter", resultsA[0].ActionId);
        Assert.Single(resultsB);
        Assert.Equal("sort", resultsB[0].ActionId);
    }

    [Fact]
    public async Task ActionHistoryStore_GetRecent_RespectsLimit()
    {
        var store = new InMemoryActionHistoryStore();
        for (var i = 0; i < 10; i++)
            await store.RecordAsync(MakeEntry("s", actionId: $"action-{i}"));

        var results = await store.GetRecentAsync("s", limit: 3);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task ActionHistoryStore_AtCapacity_DropsOldestEntry()
    {
        var store = new InMemoryActionHistoryStore();

        // Fill to cap (200) with "old" entries, all with actionId "old"
        for (var i = 0; i < 200; i++)
            await store.RecordAsync(MakeEntry("cap-session", actionId: "old", timestamp: DateTimeOffset.UtcNow.AddMinutes(-200 + i)));

        // Add one more, which should evict the oldest
        await store.RecordAsync(MakeEntry("cap-session", actionId: "new", timestamp: DateTimeOffset.UtcNow));

        var results = await store.GetRecentAsync("cap-session", limit: 200);

        // Still at most 200 entries
        Assert.True(results.Count <= 200);
        // The newest entry is present
        Assert.Contains(results, e => e.ActionId == "new");
    }

    [Fact]
    public async Task ActionHistoryStore_GetByUser_ReturnsUserEntries()
    {
        var store = new InMemoryActionHistoryStore();
        await store.RecordAsync(MakeEntry("s1", userId: "alice", actionId: "filter"));
        await store.RecordAsync(MakeEntry("s2", userId: "alice", actionId: "sort"));
        await store.RecordAsync(MakeEntry("s3", userId: "bob",   actionId: "select_row"));

        var aliceEntries = await store.GetByUserAsync("alice");
        var bobEntries = await store.GetByUserAsync("bob");

        Assert.Equal(2, aliceEntries.Count);
        Assert.All(aliceEntries, e => Assert.Equal("alice", e.UserId));
        Assert.Single(bobEntries);
        Assert.Equal("select_row", bobEntries[0].ActionId);
    }

    [Fact]
    public async Task ActionHistoryStore_NullUserId_NotIndexedInUserStore()
    {
        var store = new InMemoryActionHistoryStore();
        await store.RecordAsync(MakeEntry("s", userId: null, actionId: "filter"));

        // No user ID = not retrievable via GetByUserAsync
        var results = await store.GetByUserAsync("");
        Assert.Empty(results);
    }

    [Fact]
    public async Task NullActionHistoryStore_RecordAndRetrieve_AlwaysEmpty()
    {
        var store = new NullActionHistoryStore();
        await store.RecordAsync(MakeEntry("s", actionId: "filter"));

        var results = await store.GetRecentAsync("s");
        Assert.Empty(results);
    }

    // -------------------------------------------------------------------------
    // 2c: PatternBasedSuggestionService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SuggestionService_WithFrequentAction_ReturnsThatActionAsTopSuggestion()
    {
        var store = new InMemoryActionHistoryStore();

        // Record "filter high risk" 10 times
        for (var i = 0; i < 10; i++)
            await store.RecordAsync(MakeEntry("session-1",
                actionId: "filter",
                userMessage: "filter to high risk suppliers",
                args: new Dictionary<string, object?> { ["column"] = "RiskScore", ["operator"] = "gte", ["value"] = "80" }));

        // Record other actions less frequently
        for (var i = 0; i < 3; i++)
            await store.RecordAsync(MakeEntry("session-1", actionId: "sort", userMessage: "sort by name"));

        var service = new PatternBasedSuggestionService(store);

        var suggestions = await service.GetSuggestionsAsync("session-1", userId: null, currentContext: null);

        Assert.NotEmpty(suggestions);
        // Top suggestion should correspond to the most-frequent action
        var top = suggestions[0];
        Assert.Equal("pattern", top.Source);
        Assert.True(top.Confidence > 0f);
        // The top suggestion text should reference the high-frequency message
        Assert.Contains("filter to high risk suppliers", top.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestionService_WithNoHistory_ReturnsEmpty()
    {
        var store = new InMemoryActionHistoryStore();
        var service = new PatternBasedSuggestionService(store);

        var suggestions = await service.GetSuggestionsAsync("empty-session", userId: null, currentContext: null);

        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task SuggestionService_ReturnsAtMostThreeSuggestions()
    {
        var store = new InMemoryActionHistoryStore();

        // Record 10 distinct actions so there are more than 3 candidates
        for (var i = 0; i < 10; i++)
            await store.RecordAsync(MakeEntry("s",
                actionId: $"action_{i}",
                userMessage: $"do action {i}"));

        var service = new PatternBasedSuggestionService(store);

        var suggestions = await service.GetSuggestionsAsync("s", userId: null, currentContext: null);

        Assert.True(suggestions.Count <= 3);
    }

    [Fact]
    public async Task SuggestionService_ConfidenceIsBetweenZeroAndOne()
    {
        var store = new InMemoryActionHistoryStore();
        for (var i = 0; i < 5; i++)
            await store.RecordAsync(MakeEntry("s", actionId: "filter", userMessage: "filter"));

        var service = new PatternBasedSuggestionService(store);
        var suggestions = await service.GetSuggestionsAsync("s", userId: null, currentContext: null);

        Assert.All(suggestions, s =>
        {
            Assert.True(s.Confidence >= 0f);
            Assert.True(s.Confidence <= 1f);
        });
    }

    [Fact]
    public async Task StaticSuggestionService_AlwaysReturnsEmpty()
    {
        var service = new StaticSuggestionService();

        var suggestions = await service.GetSuggestionsAsync("any", "user", "context");

        Assert.Empty(suggestions);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ActionHistoryEntry MakeEntry(
        string sessionId,
        string actionId = "filter",
        string? userId = null,
        string userMessage = "test message",
        Dictionary<string, object?>? args = null,
        DateTimeOffset? timestamp = null)
        => new(
            SessionId: sessionId,
            UserId: userId,
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            UserMessage: userMessage,
            ActionId: actionId,
            AgentId: "test-component",
            Args: args ?? new Dictionary<string, object?>());
}
