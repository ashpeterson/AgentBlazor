namespace AgentBlazor.Options;

/// <summary>
/// Configuration options for prompt tracing and observability.
/// </summary>
public sealed class PromptTracingOptions
{
    /// <summary>
    /// Whether prompt tracing is enabled. When disabled, tracing has zero overhead.
    /// Default: false (opt-in)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum number of traces to retain in memory.
    /// Default: 1000
    /// </summary>
    public int MaxTraceCount { get; set; } = 1000;

    /// <summary>
    /// How long to retain traces before automatic cleanup.
    /// Default: 1 hour
    /// </summary>
    public TimeSpan TraceRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether to capture full request/response content (may be large).
    /// Default: true
    /// </summary>
    public bool CaptureFullContent { get; set; } = true;

    /// <summary>
    /// Whether to capture extracted entities from classification.
    /// Default: true
    /// </summary>
    public bool CaptureExtractedEntities { get; set; } = true;

    /// <summary>
    /// Whether to capture action arguments (may contain sensitive data).
    /// Default: true
    /// </summary>
    public bool CaptureActionArguments { get; set; } = true;

    /// <summary>
    /// Maximum content length to capture (truncates longer content).
    /// Default: 10000 characters
    /// </summary>
    public int MaxContentLength { get; set; } = 10000;

    /// <summary>
    /// Whether to enable automatic cleanup of expired traces.
    /// Default: true
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// Interval for automatic cleanup of expired traces.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}
