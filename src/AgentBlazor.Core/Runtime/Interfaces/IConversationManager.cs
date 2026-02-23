using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Manages conversation state, history, and pending clarifications for agent sessions.
/// </summary>
internal interface IConversationManager
{
    /// <summary>
    /// Gets the conversation history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The conversation history, or null if none exists.</returns>
    Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a turn to the conversation history.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turn">The turn to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a conversation context string for LLM prompts.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="maxTurns">Maximum number of recent turns to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A formatted context string, or empty if no history.</returns>
    Task<string> BuildContextAsync(
        string sessionId,
        int maxTurns = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if there is a pending clarification for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="agentName">The agent name.</param>
    /// <returns>The pending clarification, or null if none.</returns>
    PendingClarification? GetPendingClarification(string sessionId, string agentName);

    /// <summary>
    /// Sets a pending clarification for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="agentName">The agent name.</param>
    /// <param name="clarification">The pending clarification.</param>
    void SetPendingClarification(string sessionId, string agentName, PendingClarification clarification);

    /// <summary>
    /// Clears any pending clarification for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="agentName">The agent name.</param>
    void ClearPendingClarification(string sessionId, string agentName);

    /// <summary>
    /// Tries to resolve pending arguments from a user reply.
    /// </summary>
    /// <param name="userReply">The user's reply message.</param>
    /// <param name="pending">The pending clarification.</param>
    /// <param name="resolvedArguments">The resolved arguments if successful.</param>
    /// <returns>True if arguments were resolved; otherwise, false.</returns>
    bool TryResolvePendingArguments(
        string userReply,
        PendingClarification pending,
        out IReadOnlyDictionary<string, object?> resolvedArguments);

    /// <summary>
    /// Builds a pending clarification from execution results.
    /// </summary>
    /// <param name="userMessage">The original user message.</param>
    /// <param name="plannedActions">The planned actions.</param>
    /// <param name="executionResults">The execution results.</param>
    /// <param name="registeredComponents">The registered components.</param>
    /// <param name="clarification">The built clarification if any is needed.</param>
    /// <returns>True if a clarification is needed; otherwise, false.</returns>
    bool TryBuildPendingClarification(
        string userMessage,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        out PendingClarification? clarification);

    /// <summary>
    /// Checks if a message is an affirmative reply (yes, yeah, etc.).
    /// </summary>
    bool IsAffirmativeReply(string message);

    /// <summary>
    /// Checks if a message is a negative reply (no, cancel, etc.).
    /// </summary>
    bool IsNegativeReply(string message);

    /// <summary>
    /// Clears the conversation history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a pending clarification waiting for user input.
/// </summary>
internal sealed record PendingClarification
{
    /// <summary>
    /// The component ID awaiting clarification.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// The action ID awaiting clarification.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// The missing parameter that needs to be provided.
    /// </summary>
    public required string MissingParameter { get; init; }

    /// <summary>
    /// The prompt to show the user asking for clarification.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// The base arguments already provided.
    /// </summary>
    public IReadOnlyDictionary<string, object?> BaseArguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Suggested argument values the user can confirm.
    /// </summary>
    public IReadOnlyDictionary<string, object?> SuggestedArguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When this clarification was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
