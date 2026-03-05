using AgentBlazor.Components.Chat;

namespace AgentBlazor.Components.Tests;

public class HandoffPolicyFormatterTests
{
    [Theory]
    [InlineData("/handoff-policy", true)]
    [InlineData("handoff policy", true)]
    [InlineData("/handoff-history", false)]
    public void TryParsePolicyCommand_ParsesExpectedValues(string input, bool expected)
    {
        var parsed = HandoffPolicyFormatter.TryParsePolicyCommand(input);

        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void BuildSummary_FormatsLimitsAndRules()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dojo Agent"] = ["Workflow Agent", "!Supplier Agent"],
                ["*"] = ["Workflow Agent"]
            };
        IReadOnlyList<HandoffTransition> history =
        [
            new("Dojo Agent", "Workflow Agent", DateTimeOffset.Parse("2026-03-05T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture)),
            new("Workflow Agent", "Supplier Agent", DateTimeOffset.Parse("2026-03-05T10:01:00Z", System.Globalization.CultureInfo.InvariantCulture))
        ];

        var summary = HandoffPolicyFormatter.BuildSummary(
            policy,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Workflow Agent"] = ["Supplier Agent"]
            },
            defaultRequireHandoffApproval: false,
            history,
            maxHandoffsPerSession: 10,
            maxHandoffsPerPair: 4,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: 3,
            handoffWindowMinutes: 15,
            maxPairHandoffsPerWindow: 2,
            nowUtc: DateTimeOffset.Parse("2026-03-05T10:05:00Z", System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains("Handoff policy summary:", summary, StringComparison.Ordinal);
        Assert.Contains("Session limit: 10", summary, StringComparison.Ordinal);
        Assert.Contains("Pair lifetime limit: 4", summary, StringComparison.Ordinal);
        Assert.Contains("Session window limit: 3", summary, StringComparison.Ordinal);
        Assert.Contains("Pair window limit: 2", summary, StringComparison.Ordinal);
        Assert.Contains("Default handoff approval required: no", summary, StringComparison.Ordinal);
        Assert.Contains("Workflow Agent -> Supplier Agent", summary, StringComparison.Ordinal);
        Assert.Contains("Dojo Agent -> Workflow Agent, !Supplier Agent", summary, StringComparison.Ordinal);
    }
}
