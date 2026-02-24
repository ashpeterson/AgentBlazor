using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Agents;

public sealed record AgentTurnResponse(
    string AgentName,
    string ResponseText,
    IReadOnlyList<PlannedComponentAction> PlannedActions,
    IReadOnlyList<ComponentActionExecutionResult> ExecutionResults)
{
    public bool RequiresClarification { get; init; }
    public string? ClarificationQuestion { get; init; }
    public bool RequiresApproval { get; init; }
    public IReadOnlyList<PendingApproval> PendingApprovals { get; init; } = [];
}

public sealed record PendingApproval(
    string ComponentId,
    string ActionId,
    string Description,
    IReadOnlyDictionary<string, object?> Parameters);
