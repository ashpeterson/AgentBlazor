using System.ComponentModel;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Execution;
using Microsoft.Extensions.AI;

namespace AgentBlazor.Core.Runtime.Agents;

public sealed record AgentTurnResponse(
    string AgentName,
    string ResponseText,
    [property: EditorBrowsable(EditorBrowsableState.Never)]
    IReadOnlyList<PlannedComponentAction> PlannedActions,
    [property: EditorBrowsable(EditorBrowsableState.Never)]
    IReadOnlyList<ComponentActionExecutionResult> ExecutionResults)
{
    public bool RequiresClarification { get; init; }
    public string? ClarificationQuestion { get; init; }
    public bool RequiresApproval { get; init; }
    public IReadOnlyList<PendingApproval> PendingApprovals { get; init; } = [];
    public AgentUiDocument? GeneratedUi { get; init; }
    public AgentExecutionPlan? ExecutionPlan { get; init; }
    public UsageDetails? Usage { get; init; }
    public bool HasNormalizedExecutionPlan => ExecutionPlan?.Steps.Count > 0;
    public bool UsesLegacyCompatibilityPayload => !HasNormalizedExecutionPlan &&
        (PlannedActions.Count > 0 || ExecutionResults.Count > 0);
    public IReadOnlyList<PlannedComponentAction> LegacyPlannedActions =>
        HasNormalizedExecutionPlan ? [] : PlannedActions;
    public IReadOnlyList<ComponentActionExecutionResult> LegacyExecutionResults =>
        HasNormalizedExecutionPlan ? [] : ExecutionResults;
}

public sealed record PendingApproval(
    string ComponentId,
    string ActionId,
    string Description,
    IReadOnlyDictionary<string, object?> Parameters,
    AgentPolicyDecision? PolicyDecision = null,
    string? ApprovalId = null);
