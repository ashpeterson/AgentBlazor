namespace AgentBlazor.Options;

/// <summary>
/// Configuration options for conversation management.
/// </summary>
public sealed class ConversationOptions
{
    /// <summary>
    /// Maximum number of turns to keep per session.
    /// Older turns are removed when this limit is exceeded.
    /// Default: 50
    /// </summary>
    public int MaxTurnsPerSession { get; set; } = 50;

    /// <summary>
    /// How long to keep inactive sessions before they expire.
    /// Default: 24 hours
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum number of recent turns to include in LLM prompts.
    /// Default: 5
    /// </summary>
    public int MaxHistoryInPrompt { get; set; } = 5;

    /// <summary>
    /// Whether to include action execution results in conversation history.
    /// Default: true
    /// </summary>
    public bool IncludeActionResultsInHistory { get; set; } = true;

    /// <summary>
    /// Whether to automatically clean up expired sessions.
    /// Default: true
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// How often to run the cleanup process.
    /// Default: 1 hour
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of sessions to keep in memory.
    /// When exceeded, oldest sessions are evicted.
    /// Default: 10000
    /// </summary>
    public int MaxSessions { get; set; } = 10000;

    /// <summary>
    /// Whether to persist conversation history across server restarts.
    /// Only applies to persistent store implementations.
    /// Default: false (in-memory is ephemeral)
    /// </summary>
    public bool PersistAcrossRestarts { get; set; } = false;
}
