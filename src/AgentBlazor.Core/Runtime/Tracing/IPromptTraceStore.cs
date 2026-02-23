namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// Storage interface for prompt traces.
/// Implementations can be in-memory, file-based, or external services.
/// </summary>
internal interface IPromptTraceStore
{
    /// <summary>
    /// Stores a completed trace.
    /// </summary>
    /// <param name="trace">The trace to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(PromptTrace trace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a trace by its ID.
    /// </summary>
    /// <param name="traceId">The trace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trace, or null if not found.</returns>
    Task<PromptTrace?> GetByIdAsync(string traceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves traces for a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="limit">Maximum number of traces to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Traces for the session, ordered by timestamp descending.</returns>
    Task<IReadOnlyList<PromptTrace>> GetBySessionAsync(
        string sessionId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves traces for a specific user across all sessions.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="limit">Maximum number of traces to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Traces for the user, ordered by timestamp descending.</returns>
    Task<IReadOnlyList<PromptTrace>> GetByUserAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves recent traces across all sessions.
    /// </summary>
    /// <param name="limit">Maximum number of traces to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent traces, ordered by timestamp descending.</returns>
    Task<IReadOnlyList<PromptTrace>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries traces by outcome.
    /// </summary>
    /// <param name="outcome">The outcome to filter by.</param>
    /// <param name="limit">Maximum number of traces to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Traces matching the outcome.</returns>
    Task<IReadOnlyList<PromptTrace>> GetByOutcomeAsync(
        PromptTraceOutcome outcome,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets summary statistics for traces.
    /// </summary>
    /// <param name="since">Start of time window (null for all time).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trace statistics.</returns>
    Task<PromptTraceStatistics> GetStatisticsAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all traces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current trace count.
    /// </summary>
    int Count { get; }
}

/// <summary>
/// Query parameters for trace retrieval.
/// </summary>
internal sealed record PromptTraceQuery
{
    /// <summary>
    /// Filter by session ID.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Filter by user ID.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Filter by outcome.
    /// </summary>
    public PromptTraceOutcome? Outcome { get; init; }

    /// <summary>
    /// Filter by primary intent.
    /// </summary>
    public string? PrimaryIntent { get; init; }

    /// <summary>
    /// Filter by start time (inclusive).
    /// </summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// Filter by end time (inclusive).
    /// </summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Number of results to skip.
    /// </summary>
    public int Offset { get; init; } = 0;
}
