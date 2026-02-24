using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Agents;

public enum AgentTurnStreamEventKind
{
    RunStarted,
    TextDelta,
    PlannedAction,
    ExecutionResult,
    ClarificationRequired,
    ApprovalRequired,
    RunFinished,
    RunError
}

public sealed record AgentTurnStreamEvent
{
    public required AgentTurnStreamEventKind Kind { get; init; }
    public string? AgentName { get; init; }
    public string? TextDelta { get; init; }
    public PlannedComponentAction? PlannedAction { get; init; }
    public ComponentActionExecutionResult? ExecutionResult { get; init; }
    public string? ClarificationQuestion { get; init; }
    public IReadOnlyList<PendingApproval>? PendingApprovals { get; init; }
    public AgentTurnResponse? Response { get; init; }
    public string? ErrorMessage { get; init; }
}
