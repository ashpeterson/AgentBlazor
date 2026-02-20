using AgentBlazor.Core.Runtime.Components;

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
    public IReadOnlyList<PlannedComponentAction> PlannedActions { get; init; } = [];

    /// <summary>
    /// Results of executing the planned actions.
    /// </summary>
    public IReadOnlyList<ComponentActionExecutionResult> ExecutionResults { get; init; } = [];

    /// <summary>
    /// Whether any actions were successfully executed.
    /// </summary>
    public bool HasSuccessfulActions =>
        ExecutionResults.Any(static r => r.Succeeded);

    /// <summary>
    /// Creates a summary of this turn suitable for inclusion in prompts.
    /// </summary>
    public string Summarize()
    {
        if (ExecutionResults.Count == 0)
        {
            return AgentResponse.Length > 100
                ? $"{AgentResponse[..100]}..."
                : AgentResponse;
        }

        var successful = ExecutionResults.Count(static r => r.Succeeded);
        var actions = string.Join(", ", ExecutionResults
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
                .SelectMany(static t => t.ExecutionResults)
                .LastOrDefault(static r => r.Succeeded);

            return lastSuccessful is not null
                ? (lastSuccessful.ComponentId, lastSuccessful.ActionId)
                : null;
        }
    }
}
