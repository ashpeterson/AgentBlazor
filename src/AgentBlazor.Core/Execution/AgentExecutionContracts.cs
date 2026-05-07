namespace AgentBlazor.Execution;

public enum AgentExecutionStepKind
{
    Unknown = 0,
    SemanticCapability = 1,
    UiAction = 2,
    ServiceTool = 3,
    GeneratedUiTool = 4
}

public enum AgentExecutionStepStatus
{
    Pending = 0,
    Completed = 1,
    ApprovalRequired = 2,
    NeedsClarification = 3,
    Blocked = 4,
    Failed = 5,
    Queued = 6
}

public sealed record AgentExecutionContext(
    string SessionId,
    string RunId,
    string? UserId = null,
    string? Route = null,
    string? ContextVersion = null,
    AgentContextFreshness Freshness = AgentContextFreshness.Unknown);

public sealed record AgentExecutionStep(
    string StepId,
    int Order,
    AgentExecutionStepKind Kind,
    string TargetId,
    string ActionId,
    AgentExecutionStepStatus Status,
    bool RequiresApproval,
    AgentPolicyDecision PolicyDecision,
    IReadOnlyDictionary<string, object?>? Arguments = null,
    string? Message = null,
    IReadOnlyDictionary<string, object?>? Outputs = null,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<string>? NextActions = null);

public sealed record AgentExecutionPlan(
    string AgentName,
    AgentExecutionContext Context,
    IReadOnlyList<AgentExecutionStep> Steps)
{
    public bool RequiresApproval => Steps.Any(static step => step.RequiresApproval);

    public bool HasFailures => Steps.Any(static step =>
        step.Status is AgentExecutionStepStatus.Failed or AgentExecutionStepStatus.Blocked);
}
