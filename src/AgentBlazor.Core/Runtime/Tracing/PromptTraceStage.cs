using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// Outcome of the prompt processing.
/// </summary>
internal enum PromptTraceOutcome
{
    /// <summary>All actions succeeded.</summary>
    Succeeded,
    /// <summary>Some actions succeeded, some failed.</summary>
    PartialSuccess,
    /// <summary>All actions failed.</summary>
    Failed,
    /// <summary>No agent was registered for the request.</summary>
    NoAgentRegistered,
    /// <summary>No LLM provider was configured.</summary>
    ProviderMissing,
    /// <summary>No allowed actions for the agent.</summary>
    NoAllowedActions,
    /// <summary>Request was canceled.</summary>
    Canceled,
    /// <summary>An unexpected error occurred.</summary>
    Error
}

/// <summary>
/// Represents the entry point data for a prompt trace.
/// </summary>
internal sealed record PromptTraceEntry
{
    /// <summary>
    /// Unique identifier for this trace.
    /// </summary>
    public required string TraceId { get; init; }

    /// <summary>
    /// When the request was received.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Session identifier, if provided.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// User identifier, if provided.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// The user's original message.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// Additional context provided with the request.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }

    /// <summary>
    /// The resolved agent name.
    /// </summary>
    public string? AgentName { get; init; }
}

/// <summary>
/// Represents the intent classification stage of the trace.
/// </summary>
internal sealed record PromptTraceClassification
{
    /// <summary>
    /// Classification method used ("keyword", "llm", "hybrid").
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// The primary intent identified.
    /// </summary>
    public required string PrimaryIntent { get; init; }

    /// <summary>
    /// Confidence score between 0.0 and 1.0.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Target component ID if identified.
    /// </summary>
    public string? TargetComponentId { get; init; }

    /// <summary>
    /// Target action ID if identified.
    /// </summary>
    public string? TargetActionId { get; init; }

    /// <summary>
    /// Entities extracted from the user message.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExtractedEntities { get; init; }

    /// <summary>
    /// Time taken to classify the intent.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Whether clarification is needed from the user.
    /// </summary>
    public bool RequiresClarification { get; init; }

    /// <summary>
    /// Suggested clarification prompt if needed.
    /// </summary>
    public string? ClarificationPrompt { get; init; }
}

/// <summary>
/// Represents the intent resolution stage of the trace.
/// </summary>
internal sealed record PromptTraceResolution
{
    /// <summary>
    /// The resolved component ID to target.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// The resolved action ID to execute.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Arguments extracted or inferred for the action.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Arguments { get; init; }

    /// <summary>
    /// Reason or explanation for this resolution.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Whether the action requires user approval.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Time taken to resolve the intent.
    /// </summary>
    public required TimeSpan Duration { get; init; }
}

/// <summary>
/// Represents a single planned step in the planning stage.
/// </summary>
internal sealed record PromptTracePlannedAction
{
    /// <summary>
    /// The target component ID.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// The target action ID.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Reason for planning this step.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Arguments for the step.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Arguments { get; init; }
}

/// <summary>
/// Represents the planning stage of the trace.
/// </summary>
internal sealed record PromptTracePlanning
{
    /// <summary>
    /// All steps planned for execution.
    /// </summary>
    public required IReadOnlyList<PromptTracePlannedAction> PlannedActions { get; init; }

    /// <summary>
    /// Normalized workflow-step view over the planned actions.
    /// </summary>
    public IReadOnlyList<PromptTracePlannedAction> WorkflowSteps => PlannedActions;

    /// <summary>
    /// Time taken for planning.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of tools built for the LLM.
    /// </summary>
    public int ToolCount { get; init; }
}

/// <summary>
/// Represents a single action execution result.
/// </summary>
internal sealed record PromptTraceExecutionResult
{
    /// <summary>
    /// The component that executed.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// The action that was executed.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Whether the execution succeeded.
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>
    /// Canonical action outcome.
    /// </summary>
    public required ActionOutcome Outcome { get; init; }

    /// <summary>
    /// Result or error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Time taken to execute.
    /// </summary>
    public required TimeSpan Duration { get; init; }
}

/// <summary>
/// Represents the execution stage of the trace.
/// </summary>
internal sealed record PromptTraceExecution
{
    /// <summary>
    /// All execution results.
    /// </summary>
    public required IReadOnlyList<PromptTraceExecutionResult> Results { get; init; }

    /// <summary>
    /// Normalized workflow-step execution view over the recorded results.
    /// </summary>
    public IReadOnlyList<PromptTraceExecutionResult> ExecutionSteps => Results;

    /// <summary>
    /// Total time for all executions.
    /// </summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Number of successful executions.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of failed executions.
    /// </summary>
    public int FailureCount { get; init; }
}

/// <summary>
/// Represents the response building stage of the trace.
/// </summary>
internal sealed record PromptTraceResponse
{
    /// <summary>
    /// The final response text.
    /// </summary>
    public required string ResponseText { get; init; }

    /// <summary>
    /// Overall outcome of the prompt processing.
    /// </summary>
    public required PromptTraceOutcome Outcome { get; init; }

    /// <summary>
    /// Total duration from entry to response.
    /// </summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Error message if the outcome was Error.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Summary statistics for prompt traces.
/// </summary>
internal sealed record PromptTraceStatistics
{
    /// <summary>
    /// Total number of traces in the store.
    /// </summary>
    public required int TotalTraces { get; init; }

    /// <summary>
    /// Number of traces that succeeded.
    /// </summary>
    public required int SucceededCount { get; init; }

    /// <summary>
    /// Number of traces that failed.
    /// </summary>
    public required int FailedCount { get; init; }

    /// <summary>
    /// Number of traces with partial success.
    /// </summary>
    public required int PartialSuccessCount { get; init; }

    /// <summary>
    /// Average duration across all traces.
    /// </summary>
    public required TimeSpan AverageDuration { get; init; }

    /// <summary>
    /// Minimum duration.
    /// </summary>
    public required TimeSpan MinDuration { get; init; }

    /// <summary>
    /// Maximum duration.
    /// </summary>
    public required TimeSpan MaxDuration { get; init; }

    /// <summary>
    /// Count of traces by outcome.
    /// </summary>
    public required IReadOnlyDictionary<string, int> OutcomeCounts { get; init; }

    /// <summary>
    /// Count of traces by primary intent.
    /// </summary>
    public required IReadOnlyDictionary<string, int> IntentCounts { get; init; }

    /// <summary>
    /// Count of traces by target component.
    /// </summary>
    public required IReadOnlyDictionary<string, int> ComponentCounts { get; init; }

    /// <summary>
    /// Creates empty statistics.
    /// </summary>
    public static PromptTraceStatistics Empty => new()
    {
        TotalTraces = 0,
        SucceededCount = 0,
        FailedCount = 0,
        PartialSuccessCount = 0,
        AverageDuration = TimeSpan.Zero,
        MinDuration = TimeSpan.Zero,
        MaxDuration = TimeSpan.Zero,
        OutcomeCounts = new Dictionary<string, int>(),
        IntentCounts = new Dictionary<string, int>(),
        ComponentCounts = new Dictionary<string, int>()
    };
}
