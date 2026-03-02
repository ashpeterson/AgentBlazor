using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Interfaces;

public sealed record AgentRuntimeTurnStartedEvent(
    string SessionId,
    string? RunId,
    string AgentName,
    string UserMessage,
    DateTimeOffset OccurredAt);

public sealed record AgentRuntimeTurnFinishedEvent(
    string SessionId,
    string? RunId,
    string AgentName,
    string UserMessage,
    AgentTurnResponse Response,
    DateTimeOffset OccurredAt);

public sealed record AgentRuntimeToolExecutionStartedEvent(
    string SessionId,
    string? RunId,
    string AgentName,
    int StepIndex,
    PlannedComponentAction Action,
    DateTimeOffset OccurredAt);

public sealed record AgentRuntimeToolExecutionFinishedEvent(
    string SessionId,
    string? RunId,
    string AgentName,
    int StepIndex,
    ComponentActionExecutionResult Result,
    DateTimeOffset OccurredAt);

public sealed record AgentRuntimeErrorEvent(
    string SessionId,
    string? RunId,
    string AgentName,
    string UserMessage,
    string ErrorMessage,
    DateTimeOffset OccurredAt);

/// <summary>
/// Optional subscriber hook for runtime lifecycle notifications.
/// Register one or more implementations to observe turn/tool/error events.
/// </summary>
public interface IAgentRuntimeEventSubscriber
{
    ValueTask OnTurnStartedAsync(
        AgentRuntimeTurnStartedEvent runtimeEvent,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    ValueTask OnTurnFinishedAsync(
        AgentRuntimeTurnFinishedEvent runtimeEvent,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    ValueTask OnToolExecutionStartedAsync(
        AgentRuntimeToolExecutionStartedEvent runtimeEvent,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    ValueTask OnToolExecutionFinishedAsync(
        AgentRuntimeToolExecutionFinishedEvent runtimeEvent,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    ValueTask OnErrorAsync(
        AgentRuntimeErrorEvent runtimeEvent,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
