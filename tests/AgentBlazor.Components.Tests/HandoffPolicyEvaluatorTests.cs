using AgentBlazor.Components.Chat;

namespace AgentBlazor.Components.Tests;

public class HandoffPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_WithoutPolicy_AllowsHandoff()
    {
        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "A",
            toAgent: "B",
            handoffPolicy: null,
            history: [],
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.True(result.Allowed);
        Assert.Null(result.ViolationMessage);
    }

    [Fact]
    public void Evaluate_WhenTargetNotAllowed_BlocksHandoff()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dojo"] = ["Workflow"]
            };

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "Dojo",
            toAgent: "Supplier",
            handoffPolicy: policy,
            history: [],
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.False(result.Allowed);
        Assert.Contains("can hand off only", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenSessionLimitReached_BlocksHandoff()
    {
        IReadOnlyList<HandoffTransition> history =
        [
            new("A", "B", DateTimeOffset.UtcNow.AddMinutes(-3)),
            new("B", "C", DateTimeOffset.UtcNow.AddMinutes(-2))
        ];

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "C",
            toAgent: "A",
            handoffPolicy: null,
            history: history,
            maxHandoffsPerSession: 2,
            maxHandoffsPerPair: null,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.False(result.Allowed);
        Assert.Contains("session handoff limit", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenImmediateReturnEnabled_BlocksReverseHandoff()
    {
        IReadOnlyList<HandoffTransition> history =
        [
            new("Dojo", "Workflow", DateTimeOffset.UtcNow.AddMinutes(-1))
        ];

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "Workflow",
            toAgent: "Dojo",
            handoffPolicy: null,
            history: history,
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.False(result.Allowed);
        Assert.Contains("immediate return", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenPairLimitReached_BlocksHandoff()
    {
        IReadOnlyList<HandoffTransition> history =
        [
            new("Dojo", "Workflow", DateTimeOffset.UtcNow.AddMinutes(-4)),
            new("Workflow", "Supplier", DateTimeOffset.UtcNow.AddMinutes(-3)),
            new("Dojo", "Workflow", DateTimeOffset.UtcNow.AddMinutes(-2))
        ];

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "Dojo",
            toAgent: "Workflow",
            handoffPolicy: null,
            history: history,
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: 2,
            blockImmediateReturn: false,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.False(result.Allowed);
        Assert.Contains("pair limit", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WithWildcardRule_AllowsConfiguredTarget()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["*"] = ["Workflow Agent"]
            };

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "Dojo Agent",
            toAgent: "Workflow Agent",
            handoffPolicy: policy,
            history: [],
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Evaluate_WithDenyRule_BlocksTargetEvenWhenWildcardAllows()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> policy =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dojo Agent"] = ["*", "!Supplier Agent"]
            };

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "Dojo Agent",
            toAgent: "Supplier Agent",
            handoffPolicy: policy,
            history: [],
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: true,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: null,
            maxPairHandoffsPerWindow: null);

        Assert.False(result.Allowed);
        Assert.Contains("blocked", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenWindowSessionLimitReached_BlocksHandoff()
    {
        var now = DateTimeOffset.Parse("2026-03-05T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        IReadOnlyList<HandoffTransition> history =
        [
            new("A", "B", now.AddMinutes(-1)),
            new("B", "C", now.AddMinutes(-2)),
            new("C", "A", now.AddMinutes(-3))
        ];

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "A",
            toAgent: "B",
            handoffPolicy: null,
            history: history,
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: false,
            maxHandoffsPerWindow: 3,
            handoffWindowMinutes: 5,
            maxPairHandoffsPerWindow: null,
            nowUtc: now);

        Assert.False(result.Allowed);
        Assert.Contains("window", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenPairWindowLimitReached_BlocksPairHandoff()
    {
        var now = DateTimeOffset.Parse("2026-03-05T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        IReadOnlyList<HandoffTransition> history =
        [
            new("Dojo", "Workflow", now.AddMinutes(-1)),
            new("Dojo", "Workflow", now.AddMinutes(-2)),
            new("Workflow", "Supplier", now.AddMinutes(-3))
        ];

        var result = HandoffPolicyEvaluator.Evaluate(
            fromAgent: "Dojo",
            toAgent: "Workflow",
            handoffPolicy: null,
            history: history,
            maxHandoffsPerSession: null,
            maxHandoffsPerPair: null,
            blockImmediateReturn: false,
            maxHandoffsPerWindow: null,
            handoffWindowMinutes: 10,
            maxPairHandoffsPerWindow: 2,
            nowUtc: now);

        Assert.False(result.Allowed);
        Assert.Contains("exceeded", result.ViolationMessage, StringComparison.OrdinalIgnoreCase);
    }
}
