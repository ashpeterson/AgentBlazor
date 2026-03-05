using AgentBlazor.Components.Inspector;
using AgentBlazor.Core.Paid;

namespace AgentBlazor.Components.Tests;

public class InspectorRunCorrelationLensTests
{
    [Fact]
    public void TryGetLastHandoff_ParsesArrowFormatWithTimestamp()
    {
        var run = MakeRun(
            runId: "run-1",
            agentName: "Agent A",
            startedAt: DateTimeOffset.Parse("2026-03-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            finishedAt: DateTimeOffset.Parse("2026-03-05T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture),
            events:
            [
                new InspectorEvent(
                    DateTimeOffset.Parse("2026-03-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                    "AgentHandoff",
                    null,
                    null,
                    "Agent A -> Agent B @ 2026-03-05T10:00:00Z")
            ]);

        var parsed = InspectorRunCorrelationLens.TryGetLastHandoff(run, out var fromAgent, out var toAgent);

        Assert.True(parsed);
        Assert.Equal("Agent A", fromAgent);
        Assert.Equal("Agent B", toAgent);
    }

    [Fact]
    public void TryParseHandoffDetail_ReturnsFalse_ForInvalidShape()
    {
        var parsed = InspectorRunCorrelationLens.TryParseHandoffDetail("handoff without arrow", out var fromAgent, out var toAgent);

        Assert.False(parsed);
        Assert.Equal(string.Empty, fromAgent);
        Assert.Equal(string.Empty, toAgent);
    }

    [Fact]
    public void BuildHandoffChainMap_LinksAdjacentRunsByHandoffTarget()
    {
        var runs = new[]
        {
            MakeRun(
                runId: "run-a",
                agentName: "Agent A",
                startedAt: DateTimeOffset.Parse("2026-03-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                finishedAt: DateTimeOffset.Parse("2026-03-05T10:00:03Z", System.Globalization.CultureInfo.InvariantCulture),
                events:
                [
                    new InspectorEvent(
                        DateTimeOffset.Parse("2026-03-05T10:00:02Z", System.Globalization.CultureInfo.InvariantCulture),
                        "AgentHandoff",
                        null,
                        null,
                        "Agent A -> Agent B")
                ]),
            MakeRun(
                runId: "run-b",
                agentName: "Agent B",
                startedAt: DateTimeOffset.Parse("2026-03-05T10:00:05Z", System.Globalization.CultureInfo.InvariantCulture),
                finishedAt: DateTimeOffset.Parse("2026-03-05T10:00:07Z", System.Globalization.CultureInfo.InvariantCulture),
                events: []),
            MakeRun(
                runId: "run-c",
                agentName: "Agent C",
                startedAt: DateTimeOffset.Parse("2026-03-05T10:10:00Z", System.Globalization.CultureInfo.InvariantCulture),
                finishedAt: DateTimeOffset.Parse("2026-03-05T10:10:02Z", System.Globalization.CultureInfo.InvariantCulture),
                events: [])
        };

        var chainMap = InspectorRunCorrelationLens.BuildHandoffChainMap(runs);

        Assert.Equal(chainMap["run-a"], chainMap["run-b"]);
        Assert.NotEqual(chainMap["run-b"], chainMap["run-c"]);
    }

    [Fact]
    public void BuildHandoffChainMap_DoesNotLinkWhenGapExceedsThreshold()
    {
        var runs = new[]
        {
            MakeRun(
                runId: "run-a",
                agentName: "Agent A",
                startedAt: DateTimeOffset.Parse("2026-03-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                finishedAt: DateTimeOffset.Parse("2026-03-05T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture),
                events:
                [
                    new InspectorEvent(
                        DateTimeOffset.Parse("2026-03-05T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture),
                        "AgentHandoff",
                        null,
                        null,
                        "Agent A -> Agent B")
                ]),
            MakeRun(
                runId: "run-b",
                agentName: "Agent B",
                startedAt: DateTimeOffset.Parse("2026-03-05T10:05:00Z", System.Globalization.CultureInfo.InvariantCulture),
                finishedAt: DateTimeOffset.Parse("2026-03-05T10:05:02Z", System.Globalization.CultureInfo.InvariantCulture),
                events: [])
        };

        var chainMap = InspectorRunCorrelationLens.BuildHandoffChainMap(runs, maxGap: TimeSpan.FromSeconds(30));

        Assert.NotEqual(chainMap["run-a"], chainMap["run-b"]);
    }

    private static InspectorRunRecord MakeRun(
        string runId,
        string agentName,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        IReadOnlyList<InspectorEvent> events)
    {
        return new InspectorRunRecord(
            RunId: runId,
            SessionId: "session-1",
            AgentName: agentName,
            StartedAt: startedAt,
            FinishedAt: finishedAt,
            SystemPrompt: null,
            RawPlanResponse: null,
            Events: events,
            Succeeded: true,
            ErrorMessage: null);
    }
}
