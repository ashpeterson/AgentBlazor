using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Intent;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Classifies user intent from natural language messages to determine
/// which component actions should be invoked.
/// </summary>
public interface IIntentClassifier
{
    /// <summary>
    /// Classifies the user's intent from their message.
    /// </summary>
    /// <param name="userMessage">The user's natural language input.</param>
    /// <param name="availableActions">The component actions available to the agent.</param>
    /// <param name="history">Optional conversation history for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The classified intent with confidence and extracted entities.</returns>
    Task<IntentClassification> ClassifyAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions,
        ConversationHistory? history = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies multiple potential intents from a complex user message.
    /// </summary>
    /// <param name="userMessage">The user's natural language input.</param>
    /// <param name="availableActions">The component actions available to the agent.</param>
    /// <param name="history">Optional conversation history for context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All classified intents ordered by confidence.</returns>
    Task<IReadOnlyList<IntentClassification>> ClassifyMultipleAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions,
        ConversationHistory? history = null,
        CancellationToken cancellationToken = default);
}
