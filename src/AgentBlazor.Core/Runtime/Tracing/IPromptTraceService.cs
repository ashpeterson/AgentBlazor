namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// Service for querying and managing prompt traces.
/// Provides a higher-level API over IPromptTraceStore with query support.
/// </summary>
internal interface IPromptTraceService
{
    /// <summary>
    /// Gets a trace by its ID.
    /// </summary>
    /// <param name="traceId">The trace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trace, or null if not found.</returns>
    Task<PromptTrace?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets traces matching the specified query.
    /// </summary>
    /// <param name="query">Query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching traces.</returns>
    Task<IReadOnlyList<PromptTrace>> GetTracesAsync(
        PromptTraceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets trace statistics.
    /// </summary>
    /// <param name="since">Start of time window (null for all time).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Statistics summary.</returns>
    Task<PromptTraceStatistics> GetStatisticsAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all traces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearTracesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current trace count.
    /// </summary>
    int TraceCount { get; }

    /// <summary>
    /// Generates a markdown report of recent traces.
    /// </summary>
    /// <param name="limit">Maximum number of traces to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Markdown formatted report.</returns>
    Task<string> GenerateReportAsync(int limit = 50, CancellationToken cancellationToken = default);
}
