using System.ComponentModel;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Execution;

namespace AgentBlazor.Core.Runtime.Conversation;

/// <summary>
/// Represents a single turn in a conversation between user and agent.
/// </summary>
public sealed record ConversationTurn
{
    /// <summary>
    /// When this turn occurred.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// The user's message for this turn.
    /// </summary>
    public required string UserMessage { get; init; }

    /// <summary>
    /// The agent's response text.
    /// </summary>
    public required string AgentResponse { get; init; }

    /// <summary>
    /// Actions that were planned during this turn.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IReadOnlyList<PlannedComponentAction> PlannedActions { get; init; } = [];

    /// <summary>
    /// Results of executing the planned actions.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IReadOnlyList<ComponentActionExecutionResult> ExecutionResults { get; init; } = [];

    /// <summary>
    /// Normalized execution plan for this turn, when available.
    /// </summary>
    public AgentExecutionPlan? ExecutionPlan { get; init; }

    /// <summary>
    /// Whether this turn has normalized execution-step data.
    /// </summary>
    public bool HasNormalizedExecutionPlan => ExecutionPlan?.Steps.Count > 0;

    /// <summary>
    /// Whether this turn still depends on planner-era compatibility payloads.
    /// </summary>
    public bool UsesLegacyCompatibilityPayload => !HasNormalizedExecutionPlan &&
        (PlannedActions.Count > 0 || ExecutionResults.Count > 0);

    /// <summary>
    /// Legacy planned-action payload, only meaningful when <see cref="ExecutionPlan"/> is absent.
    /// </summary>
    public IReadOnlyList<PlannedComponentAction> LegacyPlannedActions =>
        HasNormalizedExecutionPlan ? [] : PlannedActions;

    /// <summary>
    /// Legacy execution-result payload, only meaningful when <see cref="ExecutionPlan"/> is absent.
    /// </summary>
    public IReadOnlyList<ComponentActionExecutionResult> LegacyExecutionResults =>
        HasNormalizedExecutionPlan ? [] : ExecutionResults;

    /// <summary>
    /// Optional generated UI document returned for this turn.
    /// </summary>
    public AgentUiDocument? GeneratedUi { get; init; }

    /// <summary>
    /// Whether any actions were successfully executed.
    /// </summary>
    public bool HasSuccessfulActions =>
        ExecutionPlan?.Steps.Any(static step => step.Status is AgentExecutionStepStatus.Completed) is true
            || LegacyExecutionResults.Any(static r => r.Succeeded);

    /// <summary>
    /// Creates a summary of this turn suitable for inclusion in prompts.
    /// </summary>
    public string Summarize()
    {
        if (ExecutionPlan?.Steps.Count > 0)
        {
            var completed = ExecutionPlan.Steps
                .Where(static step => step.Status is AgentExecutionStepStatus.Completed)
                .ToArray();

            if (completed.Length > 0)
            {
                var completedActions = string.Join(", ", completed
                    .Select(static step => $"{step.TargetId}.{step.ActionId}")
                    .Take(3));

                return $"Executed {completed.Length} step(s): {completedActions}";
            }
        }

        if (LegacyExecutionResults.Count == 0)
        {
            return AgentResponse.Length > 100
                ? $"{AgentResponse[..100]}..."
                : AgentResponse;
        }

        var successful = LegacyExecutionResults.Count(static r => r.Succeeded);
        var actions = string.Join(", ", LegacyExecutionResults
            .Where(static r => r.Succeeded)
            .Select(static r => $"{r.ComponentId}.{r.ActionId}")
            .Take(3));

        return $"Executed {successful} action(s): {actions}";
    }
}

/// <summary>
/// Represents the conversation history for a session.
/// </summary>
public sealed record ConversationHistory
{
    /// <summary>
    /// The session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// All turns in this conversation, ordered chronologically.
    /// </summary>
    public IReadOnlyList<ConversationTurn> Turns { get; init; } = [];

    /// <summary>
    /// When this conversation was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the last activity occurred.
    /// </summary>
    public DateTime LastActivityAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional user identifier associated with this session.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Creates an empty history for a new session.
    /// </summary>
    public static ConversationHistory Create(string sessionId, string? userId = null) => new()
    {
        SessionId = sessionId,
        UserId = userId,
        Turns = [],
        CreatedAt = DateTime.UtcNow,
        LastActivityAt = DateTime.UtcNow
    };

    /// <summary>
    /// Returns a new history with the turn appended.
    /// </summary>
    public ConversationHistory WithTurn(ConversationTurn turn) => this with
    {
        Turns = [..Turns, turn],
        LastActivityAt = DateTime.UtcNow
    };

    /// <summary>
    /// Returns the most recent turns up to the specified count.
    /// </summary>
    public IReadOnlyList<ConversationTurn> GetRecentTurns(int count) =>
        Turns.TakeLast(count).ToArray();

    /// <summary>
    /// Gets the last user message, if any.
    /// </summary>
    public string? LastUserMessage => Turns.LastOrDefault()?.UserMessage;

    /// <summary>
    /// Gets the last successfully executed action, if any.
    /// </summary>
    public (string ComponentId, string ActionId)? LastSuccessfulAction
    {
        get
        {
            var lastSuccessful = Turns
                .SelectMany(static t =>
                    t.ExecutionPlan?.Steps
                        .Where(static step => step.Status is AgentExecutionStepStatus.Completed)
                        .Select(static step => (step.TargetId, step.ActionId))
                    ?? Enumerable.Empty<(string TargetId, string ActionId)>())
                .LastOrDefault();

            if (lastSuccessful != default)
            {
                return lastSuccessful;
            }

            var lastSuccessfulResult = Turns
                .SelectMany(static t => t.LegacyExecutionResults)
                .LastOrDefault(static r => r.Succeeded);

            return lastSuccessfulResult is not null
                ? (lastSuccessfulResult.ComponentId, lastSuccessfulResult.ActionId)
                : null;
        }
    }
}
