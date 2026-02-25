namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Represents a request for an agent turn.
/// </summary>
/// <param name="UserMessage">The user's message.</param>
/// <param name="AgentName">Optional agent name to target.</param>
/// <param name="SessionId">Optional session identifier for conversation history.</param>
/// <param name="UserId">Optional user identifier for cross-session preferences.</param>
/// <param name="Context">Optional context dictionary.</param>
/// <param name="GeneratedUiAction">Optional typed generated-UI action invocation metadata.</param>
public sealed record AgentTurnRequest(
    string UserMessage,
    string? AgentName = null,
    string? SessionId = null,
    string? UserId = null,
    IDictionary<string, string>? Context = null,
    GeneratedUiActionInvocation? GeneratedUiAction = null)
{
    /// <summary>
    /// Gets the effective session ID, using the context value if SessionId is not set.
    /// </summary>
    public string GetEffectiveSessionId()
    {
        if (!string.IsNullOrWhiteSpace(SessionId))
        {
            return SessionId;
        }

        if (Context is not null &&
            Context.TryGetValue(AgentRuntimeContextKeys.SessionId, out var contextSessionId) &&
            !string.IsNullOrWhiteSpace(contextSessionId))
        {
            return contextSessionId;
        }

        return "global";
    }

    /// <summary>
    /// Gets the effective user ID from the context if not explicitly set.
    /// </summary>
    public string? GetEffectiveUserId()
    {
        if (!string.IsNullOrWhiteSpace(UserId))
        {
            return UserId;
        }

        if (Context is not null &&
            Context.TryGetValue(AgentRuntimeContextKeys.UserId, out var contextUserId) &&
            !string.IsNullOrWhiteSpace(contextUserId))
        {
            return contextUserId;
        }

        return null;
    }
}

/// <summary>
/// Typed generated-UI action invocation forwarded from UI components.
/// </summary>
public sealed record GeneratedUiActionInvocation(
    string BlockId,
    string ActionId,
    string? Prompt,
    IReadOnlyDictionary<string, object?> Payload);
