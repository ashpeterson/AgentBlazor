using AgentBlazor.Core.Runtime.Preferences;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Service for tracking and retrieving user preferences based on past interactions.
/// </summary>
public interface IUserPreferenceService
{
    /// <summary>
    /// Records an action that was performed, updating preference tracking.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="componentId">The component that performed the action.</param>
    /// <param name="actionId">The action that was performed.</param>
    /// <param name="arguments">Arguments passed to the action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordActionAsync(
        string sessionId,
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the aggregated preferences for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user preferences for the session.</returns>
    Task<UserPreferences> GetPreferencesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets preferences for a user across all their sessions.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated preferences across all sessions.</returns>
    Task<UserPreferences> GetUserPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Associates a session with a user ID for cross-session preference tracking.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AssociateUserAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears preferences for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearSessionPreferencesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets suggested values for a parameter based on past usage.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="maxSuggestions">Maximum number of suggestions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Suggested values ordered by frequency.</returns>
    Task<IReadOnlyList<string>> GetSuggestedValuesAsync(
        string sessionId,
        string parameterName,
        int maxSuggestions = 5,
        CancellationToken cancellationToken = default);
}
