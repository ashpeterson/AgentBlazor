using AgentBlazor.Components.Inspector;
using AgentBlazor.Core.Paid;

namespace AgentBlazor.Components.Tests;

public class InspectorEventLensTests
{
    [Theory]
    [InlineData("PlanningStarted", "planning")]
    [InlineData("ValidationFailed", "validation")]
    [InlineData("ExecutionFinished", "execution")]
    [InlineData("StateDelta", "state")]
    [InlineData("AgentHandoff", "handoff")]
    [InlineData("TextMessageContent", "stream")]
    [InlineData("RunFinished", "run")]
    [InlineData("UnknownKind", "other")]
    public void ClassifyPhase_ReturnsExpectedValue(string kind, string expected)
    {
        var phase = InspectorEventLens.ClassifyPhase(kind);

        Assert.Equal(expected, phase);
    }

    [Fact]
    public void IsStreamKind_ReturnsTrue_ForStreamEvent()
    {
        Assert.True(InspectorEventLens.IsStreamKind("ToolCallResult"));
        Assert.False(InspectorEventLens.IsStreamKind("StateDelta"));
    }

    [Fact]
    public void ExtractJsonTopLevelKeys_ReturnsKeys_ForJsonObject()
    {
        var keys = InspectorEventLens.ExtractJsonTopLevelKeys("{\"alpha\":1,\"beta\":\"x\",\"gamma\":true}");

        Assert.Equal(["alpha", "beta", "gamma"], keys);
    }

    [Fact]
    public void ExtractJsonTopLevelKeys_ReturnsEmpty_ForNonObjectJson()
    {
        var keys = InspectorEventLens.ExtractJsonTopLevelKeys("[1,2,3]");

        Assert.Empty(keys);
    }

    [Fact]
    public void ExtractJsonTopLevelEntries_ReturnsKeyValuePreviews()
    {
        var entries = InspectorEventLens.ExtractJsonTopLevelEntries("{\"name\":\"A very long value that should truncate\",\"count\":3,\"meta\":{\"a\":1}}", maxEntries: 3, maxValueLength: 16);

        Assert.Equal(3, entries.Count);
        Assert.Equal("name", entries[0].Key);
        Assert.EndsWith("...", entries[0].ValuePreview, StringComparison.Ordinal);
        Assert.Equal("count", entries[1].Key);
        Assert.Equal("3", entries[1].ValuePreview);
        Assert.Equal("meta", entries[2].Key);
        Assert.Equal("{...}", entries[2].ValuePreview);
    }

    [Fact]
    public void ExtractJsonTopLevelEntries_ReturnsEmpty_ForInvalidJson()
    {
        var entries = InspectorEventLens.ExtractJsonTopLevelEntries("{invalid");

        Assert.Empty(entries);
    }

    [Fact]
    public void ExtractJsonLeafPaths_ReturnsNestedPathEntries()
    {
        var entries = InspectorEventLens.ExtractJsonLeafPaths(
            "{\"state\":{\"form\":{\"title\":\"Test\",\"minutes\":15}},\"items\":[{\"id\":1},{\"id\":2}]}",
            maxEntries: 8,
            maxDepth: 5);

        Assert.Contains(entries, static entry => entry.Key == "$.state.form.title" && entry.ValuePreview == "Test");
        Assert.Contains(entries, static entry => entry.Key == "$.state.form.minutes" && entry.ValuePreview == "15");
        Assert.Contains(entries, static entry => entry.Key == "$.items[0].id" && entry.ValuePreview == "1");
        Assert.Contains(entries, static entry => entry.Key == "$.items[1].id" && entry.ValuePreview == "2");
    }

    [Fact]
    public void ExtractJsonLeafPaths_AppliesDepthAndArrayTruncation()
    {
        var entries = InspectorEventLens.ExtractJsonLeafPaths(
            "{\"root\":{\"level1\":{\"level2\":{\"level3\":{\"level4\":\"x\"}}}},\"arr\":[1,2,3,4,5]}",
            maxEntries: 10,
            maxDepth: 2,
            maxArrayItemsPerNode: 2);

        Assert.Contains(entries, static entry => entry.Key == "$.root.level1.level2" && entry.ValuePreview == "(max-depth)");
        Assert.Contains(entries, static entry => entry.Key == "$.arr[0]" && entry.ValuePreview == "1");
        Assert.Contains(entries, static entry => entry.Key == "$.arr[1]" && entry.ValuePreview == "2");
        Assert.Contains(entries, static entry => entry.Key == "$.arr[...]" && entry.ValuePreview == "(truncated)");
    }

    [Fact]
    public void GroupByPhase_GroupsAndSortsByPhaseOrder()
    {
        IReadOnlyList<InspectorEvent> events =
        [
            new(DateTimeOffset.UtcNow, "ExecutionStarted", null, null, null),
            new(DateTimeOffset.UtcNow, "PlanningStarted", null, null, null),
            new(DateTimeOffset.UtcNow, "TextMessageContent", null, null, null),
            new(DateTimeOffset.UtcNow, "ValidationPassed", null, null, null)
        ];

        var groups = InspectorEventLens.GroupByPhase(events);

        Assert.Equal(["planning", "validation", "execution", "stream"], groups.Select(static g => g.Phase).ToArray());
        Assert.Equal("Planning", groups[0].Label);
        Assert.Single(groups[0].Events);
    }
}
