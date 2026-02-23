using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Preferences;
using Microsoft.Extensions.AI;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Plans and builds component actions from user intents and resolved actions.
/// </summary>
internal interface IActionPlanner
{
    /// <summary>
    /// Builds AI tools from available component capabilities.
    /// </summary>
    /// <param name="allowedComponents">Components and actions available to the agent.</param>
    /// <returns>List of AI tools for the LLM to use.</returns>
    IReadOnlyList<AITool> BuildTools(IReadOnlyList<ComponentCapability> allowedComponents);

    /// <summary>
    /// Plans a component action from a resolved intent.
    /// </summary>
    /// <param name="resolvedIntent">The resolved intent to plan.</param>
    /// <param name="userMessage">The original user message.</param>
    /// <param name="registeredComponents">Currently registered component instances.</param>
    /// <param name="preferences">Optional user preferences for argument inference.</param>
    /// <returns>The planned action ready for execution.</returns>
    PlannedComponentAction PlanAction(
        ResolvedIntent resolvedIntent,
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        UserPreferences? preferences = null);

    /// <summary>
    /// Builds enriched arguments for a component action.
    /// </summary>
    /// <param name="userMessage">The user message for context.</param>
    /// <param name="componentId">The target component ID.</param>
    /// <param name="actionId">The target action ID.</param>
    /// <param name="registeredComponents">Currently registered component instances.</param>
    /// <param name="seedArguments">Initial arguments to enrich.</param>
    /// <returns>Enriched arguments with inferred values and context.</returns>
    IReadOnlyDictionary<string, object?> BuildArgumentsWithContext(
        string userMessage,
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        IReadOnlyDictionary<string, object?>? seedArguments = null);

    /// <summary>
    /// Adds inferred arguments based on user message and component state.
    /// </summary>
    /// <param name="args">Arguments dictionary to modify.</param>
    /// <param name="componentId">The target component ID.</param>
    /// <param name="actionId">The target action ID.</param>
    /// <param name="userMessage">The user message for inference.</param>
    /// <param name="registeredComponents">Currently registered component instances.</param>
    void AddInferredArguments(
        IDictionary<string, object?> args,
        string componentId,
        string actionId,
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents);

    /// <summary>
    /// Resolves structured tool actions from LLM response text.
    /// </summary>
    /// <param name="text">The LLM response text to parse.</param>
    /// <param name="allowedComponents">Components and actions available.</param>
    /// <returns>List of resolved fallback actions.</returns>
    IReadOnlyList<FallbackAction> ResolveStructuredToolActions(
        string text,
        IReadOnlyList<ComponentCapability> allowedComponents);

    /// <summary>
    /// Builds the reason string for a tool invocation.
    /// </summary>
    /// <param name="userMessage">The user message.</param>
    /// <returns>A formatted reason string.</returns>
    string BuildToolInvocationReason(string userMessage);
}

/// <summary>
/// Represents a fallback action resolved from structured tool directives.
/// </summary>
internal readonly record struct FallbackAction(
    string ComponentId,
    string ActionId,
    bool RequiresApproval,
    IReadOnlyDictionary<string, object?>? Arguments = null);
