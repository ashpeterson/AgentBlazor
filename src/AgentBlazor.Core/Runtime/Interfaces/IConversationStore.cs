using AgentBlazor.Core.Runtime.Conversation;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Storage interface for conversation history.
/// Implementations can be in-memory, Redis, SQL, etc.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Gets the conversation history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The conversation history, or null if not found.</returns>
    Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a turn to the conversation history.
    /// Creates a new history if one doesn't exist for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="turn">The conversation turn to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the conversation history for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active session IDs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of active session IDs.</returns>
    Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user ID associated with a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userId">The user identifier to associate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetUserIdAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all session IDs for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of session IDs for the user.</returns>
    Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
