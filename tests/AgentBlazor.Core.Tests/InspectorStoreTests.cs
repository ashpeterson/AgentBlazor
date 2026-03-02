using AgentBlazor.Core.Paid;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for Part 5: Inspector Store.
/// </summary>
public class InspectorStoreTests
{
    private static InspectorRunRecord MakeRun(string sessionId, string runId = "run-1", bool succeeded = true) =>
        new InspectorRunRecord(
            RunId: runId,
            SessionId: sessionId,
            AgentName: "TestAgent",
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: DateTimeOffset.UtcNow.AddSeconds(1),
            SystemPrompt: "# ROLE\nYou are a test agent.",
            RawPlanResponse: "{}",
            Events: [],
            Succeeded: succeeded,
            ErrorMessage: null);

    [Fact]
    public void NullStore_GetRecentRuns_ReturnsEmpty()
    {
        var store = new NullAgentInspectorStore();

        var runs = store.GetRecentRuns("any-session");

        Assert.Empty(runs);
    }

    [Fact]
    public void NullStore_RecordRun_DoesNotThrow()
    {
        var store = new NullAgentInspectorStore();

        var exception = Record.Exception(() => store.RecordRun(MakeRun("session-1")));

        Assert.Null(exception);
    }

    [Fact]
    public void InMemoryStore_RecordsAndRetrievesBySession()
    {
        var store = new InMemoryAgentInspectorStore();
        var run = MakeRun("session-abc");

        store.RecordRun(run);
        var runs = store.GetRecentRuns("session-abc");

        Assert.Single(runs);
        Assert.Equal("run-1", runs[0].RunId);
        Assert.Equal("# ROLE\nYou are a test agent.", runs[0].SystemPrompt);
    }

    [Fact]
    public void InMemoryStore_DoesNotMixSessions()
    {
        var store = new InMemoryAgentInspectorStore();
        store.RecordRun(MakeRun("session-a", "run-a"));
        store.RecordRun(MakeRun("session-b", "run-b"));

        var runsA = store.GetRecentRuns("session-a");
        var runsB = store.GetRecentRuns("session-b");

        Assert.Single(runsA);
        Assert.Equal("run-a", runsA[0].RunId);
        Assert.Single(runsB);
        Assert.Equal("run-b", runsB[0].RunId);
    }

    [Fact]
    public void InMemoryStore_EvictsOldestWhenCapExceeded()
    {
        var store = new InMemoryAgentInspectorStore();

        // Cap is 50 — add 52 runs
        for (var i = 1; i <= 52; i++)
            store.RecordRun(MakeRun("session-cap", $"run-{i}"));

        var runs = store.GetRecentRuns("session-cap", limit: 100);

        // Should have at most 50
        Assert.True(runs.Count <= 50);
    }

    [Fact]
    public void InMemoryStore_GetRecentRuns_RespectsLimit()
    {
        var store = new InMemoryAgentInspectorStore();
        for (var i = 1; i <= 10; i++)
            store.RecordRun(MakeRun("session-limit", $"run-{i}"));

        var runs = store.GetRecentRuns("session-limit", limit: 3);

        Assert.Equal(3, runs.Count);
    }

    [Fact]
    public void InMemoryStore_GetRecentRuns_UnknownSession_ReturnsEmpty()
    {
        var store = new InMemoryAgentInspectorStore();

        var runs = store.GetRecentRuns("nonexistent-session");

        Assert.Empty(runs);
    }

    [Fact]
    public void InspectorRunRecord_StoresSystemPrompt()
    {
        var run = MakeRun("sess-1");

        Assert.Equal("# ROLE\nYou are a test agent.", run.SystemPrompt);
    }

    [Fact]
    public void InspectorEvent_CanBeConstructed()
    {
        var ev = new InspectorEvent(
            DateTimeOffset.UtcNow,
            "ToolCallResult",
            "AgentDataGrid",
            "filter",
            "Applied filter column=status");

        Assert.Equal("ToolCallResult", ev.Kind);
        Assert.Equal("AgentDataGrid", ev.ComponentId);
        Assert.Equal("filter", ev.ActionId);
    }
}
