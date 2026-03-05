using AgentBlazor.Components.Chat;

namespace AgentBlazor.Components.Tests;

public class HandoffHistoryFormatterTests
{
    [Theory]
    [InlineData("/handoff-history", true, 10)]
    [InlineData("handoff history", true, 10)]
    [InlineData("/handoff-history 5", true, 5)]
    [InlineData("/handoff-history 99", true, 25)]
    [InlineData("/agents", false, 10)]
    public void TryParseHistoryCommand_ParsesExpectedValues(string input, bool expectedParsed, int expectedLimit)
    {
        var parsed = HandoffHistoryFormatter.TryParseHistoryCommand(input, out var limit);

        Assert.Equal(expectedParsed, parsed);
        Assert.Equal(expectedLimit, limit);
    }

    [Fact]
    public void BuildSummary_ReturnsNoHistoryMessage_WhenEmpty()
    {
        var summary = HandoffHistoryFormatter.BuildSummary([], 5);

        Assert.Equal("No handoff transitions recorded yet.", summary);
    }

    [Fact]
    public void BuildSummary_FormatsRecentTransitionsAndTopPaths()
    {
        IReadOnlyList<HandoffTransition> transitions =
        [
            new("Agent A", "Agent B", DateTimeOffset.Parse("2026-03-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            new("Agent B", "Agent C", DateTimeOffset.Parse("2026-03-05T10:01:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            new("Agent A", "Agent B", DateTimeOffset.Parse("2026-03-05T10:02:00Z", System.Globalization.CultureInfo.InvariantCulture))
        ];

        var summary = HandoffHistoryFormatter.BuildSummary(transitions, 2);

        Assert.Contains("Recent handoffs (2 of 3):", summary, StringComparison.Ordinal);
        Assert.Contains("Agent A -> Agent B", summary, StringComparison.Ordinal);
        Assert.Contains("Agent B -> Agent C", summary, StringComparison.Ordinal);
        Assert.Contains("Top paths: Agent A -> Agent B x2", summary, StringComparison.Ordinal);
    }
}

