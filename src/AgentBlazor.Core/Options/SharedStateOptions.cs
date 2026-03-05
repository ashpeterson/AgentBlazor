namespace AgentBlazor.Options;

/// <summary>
/// Merge mode for concurrent shared-state updates.
/// </summary>
public enum SharedStateMergeMode
{
    /// <summary>
    /// Always apply the most recent write received by the store.
    /// </summary>
    LastWriteWins = 0,

    /// <summary>
    /// Ignore writes whose timestamp is older than the current stored version.
    /// </summary>
    RejectStaleWrites = 1
}

/// <summary>
/// Options for shared-state storage behavior.
/// </summary>
public sealed class SharedStateOptions
{
    /// <summary>
    /// Determines how concurrent updates are merged.
    /// </summary>
    public SharedStateMergeMode MergeMode { get; set; } = SharedStateMergeMode.LastWriteWins;
}
