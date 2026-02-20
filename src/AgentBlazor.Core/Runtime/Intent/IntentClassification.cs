namespace AgentBlazor.Core.Runtime.Intent;

/// <summary>
/// Represents the result of classifying a user's intent from their message.
/// </summary>
public sealed record IntentClassification
{
    /// <summary>
    /// The primary intent identified (e.g., "navigate", "filter", "sort", "select", "submit").
    /// </summary>
    public required string PrimaryIntent { get; init; }

    /// <summary>
    /// The target component ID if identified (e.g., "AgentDataGrid", "AgentNavMenu").
    /// </summary>
    public string? TargetComponentId { get; init; }

    /// <summary>
    /// The target action ID if identified (e.g., "filter", "sort", "navigate_to").
    /// </summary>
    public string? TargetActionId { get; init; }

    /// <summary>
    /// Confidence score between 0.0 and 1.0 indicating how certain the classification is.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Entities extracted from the user message (e.g., column names, filter values, route paths).
    /// </summary>
    public IReadOnlyDictionary<string, object?> ExtractedEntities { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The classification method used ("keyword", "llm", "hybrid").
    /// </summary>
    public string ClassificationMethod { get; init; } = "keyword";

    /// <summary>
    /// Whether the classification requires clarification from the user.
    /// </summary>
    public bool RequiresClarification { get; init; }

    /// <summary>
    /// Suggested clarification question if <see cref="RequiresClarification"/> is true.
    /// </summary>
    public string? ClarificationPrompt { get; init; }

    /// <summary>
    /// Gets whether this classification is unknown/unidentified.
    /// </summary>
    public bool IsUnknown => string.Equals(PrimaryIntent, "unknown", StringComparison.OrdinalIgnoreCase) ||
                             Confidence < 0.01f;

    /// <summary>
    /// Creates an empty/unknown classification with zero confidence.
    /// </summary>
    public static IntentClassification Unknown(string? clarificationPrompt = null) => new()
    {
        PrimaryIntent = "unknown",
        Confidence = 0f,
        RequiresClarification = clarificationPrompt is not null,
        ClarificationPrompt = clarificationPrompt
    };

    /// <summary>
    /// Creates a navigation intent classification.
    /// </summary>
    public static IntentClassification Navigate(
        string? targetPath,
        float confidence,
        string method = "keyword") => new()
    {
        PrimaryIntent = "navigate",
        TargetComponentId = "AgentNavMenu",
        TargetActionId = "navigate_to",
        Confidence = confidence,
        ClassificationMethod = method,
        ExtractedEntities = targetPath is not null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["uri"] = targetPath }
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>
    /// Creates a filter intent classification.
    /// </summary>
    public static IntentClassification Filter(
        string? column,
        string? @operator,
        object? value,
        float confidence,
        string method = "keyword") => new()
    {
        PrimaryIntent = "filter",
        TargetComponentId = "AgentDataGrid",
        TargetActionId = "filter",
        Confidence = confidence,
        ClassificationMethod = method,
        ExtractedEntities = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = column,
            ["operator"] = @operator,
            ["value"] = value
        }
    };

    /// <summary>
    /// Creates a sort intent classification.
    /// </summary>
    public static IntentClassification Sort(
        string? column,
        string? direction,
        float confidence,
        string method = "keyword") => new()
    {
        PrimaryIntent = "sort",
        TargetComponentId = "AgentDataGrid",
        TargetActionId = "sort",
        Confidence = confidence,
        ClassificationMethod = method,
        ExtractedEntities = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = column,
            ["direction"] = direction
        }
    };
}
