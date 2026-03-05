using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Agents;

public enum AgentTurnStreamEventKind
{
    RunStarted,
    StepStarted,
    ToolCallStart,
    ToolCallArgs,
    ToolCallEnd,
    ToolCallResult,
    StepFinished,
    TextMessageStart,
    TextMessageContent,
    TextMessageEnd,
    ClarificationRequired,
    ApprovalRequired,
    StateSnapshot,
    StateDelta,
    RunFinished,
    RunError,
    ReasoningStart,
    ReasoningContent,
    ReasoningEnd
}

public sealed record AgentTurnStreamEvent
{
    public required AgentTurnStreamEventKind Kind { get; init; }
    public string RunId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool IsReplay { get; init; }
    public string? AgentName { get; init; }
    public string? TextDelta { get; init; }
    public int? StepIndex { get; init; }
    public bool? StepSucceeded { get; init; }
    public PlannedComponentAction? PlannedAction { get; init; }
    public ComponentActionExecutionResult? ExecutionResult { get; init; }
    public IReadOnlyDictionary<string, object?>? ToolArguments { get; init; }
    public string? ClarificationQuestion { get; init; }
    public IReadOnlyList<PendingApproval>? PendingApprovals { get; init; }
    public IReadOnlyDictionary<string, string>? SharedStateSnapshot { get; init; }
    public IReadOnlyDictionary<string, string?>? SharedStateDelta { get; init; }
    public AgentTurnResponse? Response { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public string? ReasoningDelta { get; init; }
}
