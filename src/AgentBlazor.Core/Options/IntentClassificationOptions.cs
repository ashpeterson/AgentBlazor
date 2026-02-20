namespace AgentBlazor.Options;

/// <summary>
/// Configuration options for intent classification.
/// </summary>
public sealed class IntentClassificationOptions
{
    /// <summary>
    /// Minimum confidence threshold for keyword-based classification.
    /// When confidence falls below this threshold, LLM fallback is used (if enabled).
    /// Default: 0.6
    /// </summary>
    public float MinConfidenceThreshold { get; set; } = 0.6f;

    /// <summary>
    /// Whether to use LLM-based classification when keyword matching confidence is low.
    /// Default: true
    /// </summary>
    public bool UseLlmFallback { get; set; } = true;

    /// <summary>
    /// Whether to learn from user corrections and improve future classifications.
    /// Default: true
    /// </summary>
    public bool LearnFromCorrections { get; set; } = true;

    /// <summary>
    /// Maximum number of intents to return from multi-intent classification.
    /// Default: 5
    /// </summary>
    public int MaxIntentsToReturn { get; set; } = 5;

    /// <summary>
    /// Whether to include conversation history context when classifying intents.
    /// Default: true
    /// </summary>
    public bool UseConversationContext { get; set; } = true;

    /// <summary>
    /// Number of recent conversation turns to include in LLM context.
    /// Default: 3
    /// </summary>
    public int ConversationContextTurns { get; set; } = 3;

    /// <summary>
    /// Timeout for LLM fallback classification calls.
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan LlmTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to cache classification results for identical inputs.
    /// Default: true
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// How long to cache classification results.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
