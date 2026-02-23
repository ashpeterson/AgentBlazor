namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// A complete trace of a prompt's lifecycle through the AgentBlazor pipeline.
/// This is the primary record for observability and debugging.
/// </summary>
internal sealed record PromptTrace
{
    /// <summary>
    /// Unique identifier for this trace.
    /// </summary>
    public required string TraceId { get; init; }

    /// <summary>
    /// Entry point data (session, user, message, context).
    /// </summary>
    public required PromptTraceEntry Entry { get; init; }

    /// <summary>
    /// Intent classification results, if classification occurred.
    /// </summary>
    public PromptTraceClassification? Classification { get; init; }

    /// <summary>
    /// Intent resolution results, if resolution occurred.
    /// </summary>
    public PromptTraceResolution? Resolution { get; init; }

    /// <summary>
    /// Action planning results, if planning occurred.
    /// </summary>
    public PromptTracePlanning? Planning { get; init; }

    /// <summary>
    /// Action execution results, if execution occurred.
    /// </summary>
    public PromptTraceExecution? Execution { get; init; }

    /// <summary>
    /// Final response data and outcome.
    /// </summary>
    public PromptTraceResponse? Response { get; init; }

    /// <summary>
    /// Whether this trace represents a completed prompt lifecycle.
    /// </summary>
    public bool IsComplete => Response is not null;

    /// <summary>
    /// Total duration from entry to response.
    /// </summary>
    public TimeSpan TotalDuration => Response?.TotalDuration ?? TimeSpan.Zero;

    /// <summary>
    /// Gets a short summary of the trace for logging.
    /// </summary>
    public string Summary
    {
        get
        {
            var intent = Classification?.PrimaryIntent ?? "unknown";
            var outcome = Response?.Outcome.ToString() ?? "incomplete";
            var duration = TotalDuration.TotalMilliseconds;
            return $"[{TraceId}] {intent} -> {outcome} ({duration:F1}ms)";
        }
    }

    /// <summary>
    /// Creates a minimal failed trace for error scenarios.
    /// </summary>
    public static PromptTrace CreateFailed(
        string traceId,
        PromptTraceEntry entry,
        PromptTraceOutcome outcome,
        string errorMessage,
        TimeSpan duration) => new()
    {
        TraceId = traceId,
        Entry = entry,
        Response = new PromptTraceResponse
        {
            ResponseText = errorMessage,
            Outcome = outcome,
            TotalDuration = duration,
            ErrorMessage = errorMessage
        }
    };

    /// <summary>
    /// Creates a trace for when no agent was registered.
    /// </summary>
    public static PromptTrace CreateNoAgent(
        string traceId,
        PromptTraceEntry entry,
        TimeSpan duration) => CreateFailed(
            traceId,
            entry,
            PromptTraceOutcome.NoAgentRegistered,
            "No agent registered for this request",
            duration);

    /// <summary>
    /// Creates a trace for when no provider was configured.
    /// </summary>
    public static PromptTrace CreateNoProvider(
        string traceId,
        PromptTraceEntry entry,
        TimeSpan duration) => CreateFailed(
            traceId,
            entry,
            PromptTraceOutcome.ProviderMissing,
            "No LLM provider configured",
            duration);
}
