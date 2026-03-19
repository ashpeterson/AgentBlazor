namespace AgentBlazor.Execution;

public enum AgentRiskClass
{
    Unknown = 0,
    ReadOnly = 1,
    LowRiskMutation = 2,
    SignificantMutation = 3,
    SensitiveMutation = 4,
    RestrictedAction = 5
}

public enum AgentApprovalMode
{
    None = 0,
    InlineConfirm = 1,
    ExplicitPlanApproval = 2,
    StepApproval = 3,
    PolicyDenied = 4
}

public enum AgentContextFreshness
{
    Unknown = 0,
    Current = 1,
    Changed = 2,
    Stale = 3
}

public sealed record AgentPolicyDecision(
    bool Allowed,
    AgentRiskClass RiskClass,
    AgentApprovalMode ApprovalMode,
    string? Reason = null);
