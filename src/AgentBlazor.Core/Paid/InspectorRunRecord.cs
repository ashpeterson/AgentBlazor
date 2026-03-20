using AgentBlazor.Execution;

namespace AgentBlazor.Core.Paid;

public sealed record InspectorRunRecord(
    string RunId,
    string SessionId,
    string AgentName,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? SystemPrompt,
    string? RawPlanResponse,
    IReadOnlyList<InspectorEvent> Events,
    bool Succeeded,
    string? ErrorMessage,
    AgentExecutionPlan? ExecutionPlan = null);

public sealed record InspectorEvent(
    DateTimeOffset Timestamp,
    string Kind,
    string? ComponentId,
    string? ActionId,
    string? Detail);
