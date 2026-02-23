using AgentBlazor.Core.Runtime.Intent;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace AgentBlazor.Core.Tests;

public class IntentClassificationTests
{
    [Fact]
    public async Task ClassifyAsync_NavigationIntent_ReturnsNavigateWithHighConfidence()
    {
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            "go to suppliers page",
            [],
            history: null);

        Assert.Equal("navigate", result.PrimaryIntent);
        Assert.True(result.Confidence > 0.5f);
    }

    [Fact]
    public async Task ClassifyAsync_FilterIntent_ReturnsFilterClassification()
    {
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            "filter by risk level high",
            [],
            history: null);

        Assert.Equal("filter", result.PrimaryIntent);
        Assert.True(result.Confidence > 0.5f);
    }

    [Fact]
    public async Task ClassifyAsync_SortIntent_ReturnsSortClassification()
    {
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            "sort by name ascending",
            [],
            history: null);

        Assert.Equal("sort", result.PrimaryIntent);
        Assert.True(result.Confidence > 0.5f);
    }

    [Fact]
    public async Task ClassifyAsync_UnknownIntent_ReturnsLowConfidence()
    {
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(
            "what is the weather today",
            [],
            history: null);

        Assert.True(result.Confidence < 0.5f || result.IsUnknown);
    }

    [Fact]
    public void IntentClassification_Navigate_CreatesCorrectRecord()
    {
        var intent = IntentClassification.Navigate("/suppliers", 0.95f);

        Assert.Equal("navigate", intent.PrimaryIntent);
        Assert.Equal("AgentNavMenu", intent.TargetComponentId);
        Assert.Equal("navigate_to", intent.TargetActionId);
        Assert.Equal(0.95f, intent.Confidence);
        Assert.True(intent.ExtractedEntities.ContainsKey("uri"));
        Assert.Equal("/suppliers", intent.ExtractedEntities["uri"]);
    }

    [Fact]
    public void IntentClassification_Filter_CreatesCorrectRecord()
    {
        var intent = IntentClassification.Filter("RiskLevel", "eq", "High", 0.9f);

        Assert.Equal("filter", intent.PrimaryIntent);
        Assert.Equal("AgentDataGrid", intent.TargetComponentId);
        Assert.Equal("filter", intent.TargetActionId);
        Assert.Equal("RiskLevel", intent.ExtractedEntities["column"]);
        Assert.Equal("eq", intent.ExtractedEntities["operator"]);
        Assert.Equal("High", intent.ExtractedEntities["value"]);
    }

    [Fact]
    public void IntentClassification_Sort_CreatesCorrectRecord()
    {
        var intent = IntentClassification.Sort("Name", "asc", 0.85f);

        Assert.Equal("sort", intent.PrimaryIntent);
        Assert.Equal("AgentDataGrid", intent.TargetComponentId);
        Assert.Equal("sort", intent.TargetActionId);
        Assert.Equal("Name", intent.ExtractedEntities["column"]);
        Assert.Equal("asc", intent.ExtractedEntities["direction"]);
    }

    [Fact]
    public void IntentClassification_Unknown_IsUnknownReturnsTrue()
    {
        var intent = IntentClassification.Unknown("Please clarify your request.");

        Assert.True(intent.IsUnknown);
        Assert.True(intent.RequiresClarification);
        Assert.Equal("Please clarify your request.", intent.ClarificationPrompt);
    }

    [Theory]
    [InlineData("navigate to home", "navigate")]
    [InlineData("go to the suppliers", "navigate")]
    [InlineData("show me suppliers", "navigate")]
    [InlineData("filter where status is active", "filter")]
    [InlineData("only show active ones", "filter")]
    [InlineData("sort ascending", "sort")]
    [InlineData("order by date", "sort")]
    public async Task ClassifyAsync_VariousKeywords_ReturnsExpectedIntent(string message, string expectedIntent)
    {
        var classifier = CreateClassifier();

        var result = await classifier.ClassifyAsync(message, [], history: null);

        Assert.Equal(expectedIntent, result.PrimaryIntent);
    }

    private static KeywordIntentClassifier CreateClassifier()
    {
        var options = MsOptions.Create(new IntentClassificationOptions
        {
            MinConfidenceThreshold = 0.6f,
            UseLlmFallback = false
        });
        return new KeywordIntentClassifier(options, chatClient: null);
    }
}
