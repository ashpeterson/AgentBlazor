using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Builds prompts, instructions, and responses for agent interactions.
/// </summary>
public interface IResponseBuilder
{
    /// <summary>
    /// Builds the user prompt with context and registered components.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="context">Optional context dictionary.</param>
    /// <param name="registeredComponents">Currently registered components.</param>
    /// <param name="conversationHistory">Optional conversation history for context.</param>
    /// <returns>The formatted prompt string.</returns>
    string BuildPrompt(
        string userMessage,
        IDictionary<string, string>? context,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        ConversationHistory? conversationHistory = null);

    /// <summary>
    /// Builds system instructions for the LLM.
    /// </summary>
    /// <param name="registration">The agent registration.</param>
    /// <param name="allowedComponents">Allowed component capabilities.</param>
    /// <param name="registeredComponents">Currently registered components.</param>
    /// <returns>The formatted instructions string.</returns>
    string BuildInstructions(
        AgentRegistration registration,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents);

    /// <summary>
    /// Builds a suggestion prompt for a missing parameter.
    /// </summary>
    /// <param name="userMessage">The user's original message.</param>
    /// <param name="componentId">The component ID.</param>
    /// <param name="actionId">The action ID.</param>
    /// <param name="missingParameter">The missing parameter name.</param>
    /// <param name="registeredComponents">Currently registered components.</param>
    /// <returns>A suggestion prompt for the user.</returns>
    string BuildMissingParameterSuggestion(
        string userMessage,
        string componentId,
        string actionId,
        string missingParameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents);

    /// <summary>
    /// Appends failure guidance to a response.
    /// </summary>
    /// <param name="responseText">The original response text.</param>
    /// <param name="userMessage">The user's original message.</param>
    /// <param name="executionResults">The execution results to analyze.</param>
    /// <param name="registeredComponents">Currently registered components.</param>
    /// <returns>The response with appended guidance if applicable.</returns>
    string AppendFailureGuidance(
        string responseText,
        string userMessage,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents);

    /// <summary>
    /// Formats the final agent response.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="responseText">The response text.</param>
    /// <param name="plannedActions">The planned actions.</param>
    /// <param name="executionResults">The execution results.</param>
    /// <returns>The formatted agent turn response.</returns>
    AgentTurnResponse FormatResponse(
        string agentName,
        string responseText,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults);
}
