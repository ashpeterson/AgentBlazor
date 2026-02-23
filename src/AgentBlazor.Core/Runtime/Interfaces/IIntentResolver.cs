using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Intent;
using AgentBlazor.Core.Runtime.Preferences;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Resolves user intents into actionable component operations by combining
/// intent classification, route resolution, and user preferences.
/// </summary>
internal interface IIntentResolver
{
    /// <summary>
    /// Resolves the user's intent into a concrete action to execute.
    /// </summary>
    /// <param name="userMessage">The user's natural language input.</param>
    /// <param name="allowedComponents">Components and actions available to the agent.</param>
    /// <param name="registeredComponents">Currently registered component instances.</param>
    /// <param name="history">Optional conversation history for context.</param>
    /// <param name="preferences">Optional user preferences for personalization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved intent with action details, or null if no intent could be resolved.</returns>
    Task<ResolvedIntent?> ResolveAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        ConversationHistory? history = null,
        UserPreferences? preferences = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to infer a navigation URI from the user message.
    /// </summary>
    /// <param name="userMessage">The user's natural language input.</param>
    /// <param name="uri">The inferred URI if successful.</param>
    /// <returns>True if a URI was inferred; otherwise, false.</returns>
    bool TryInferNavigationUri(string userMessage, out string uri);

    /// <summary>
    /// Attempts to extract route tokens from the user message for navigation.
    /// </summary>
    /// <param name="userMessage">The user's natural language input.</param>
    /// <param name="routeTokens">The extracted route tokens if successful.</param>
    /// <param name="hadEntityToken">True if an entity token was found and excluded.</param>
    /// <returns>True if route tokens were extracted; otherwise, false.</returns>
    bool TryExtractRouteTokens(string userMessage, out string[] routeTokens, out bool hadEntityToken);

    /// <summary>
    /// Attempts to extract an entity key token (like "SUP-123") from text.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="token">The extracted entity key if found.</param>
    /// <returns>True if an entity key was found; otherwise, false.</returns>
    bool TryExtractEntityKeyToken(string text, out string token);

    /// <summary>
    /// Checks if the text contains any of the specified keywords.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="keywords">Keywords to look for.</param>
    /// <returns>True if any keyword is found; otherwise, false.</returns>
    bool ContainsAny(string text, params string[] keywords);
}

/// <summary>
/// Represents a fully resolved intent ready for action planning.
/// </summary>
internal sealed record ResolvedIntent
{
    /// <summary>
    /// The underlying intent classification.
    /// </summary>
    public required IntentClassification Classification { get; init; }

    /// <summary>
    /// The resolved component ID to target.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// The resolved action ID to execute.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Arguments extracted or inferred for the action.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the action requires user approval before execution.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Additional follow-up actions that should be executed after this one.
    /// </summary>
    public IReadOnlyList<ResolvedIntent> FollowUpActions { get; init; } = [];

    /// <summary>
    /// Reason or explanation for why this resolution was chosen.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Creates a navigation intent.
    /// </summary>
    public static ResolvedIntent Navigate(string uri, IntentClassification classification) => new()
    {
        Classification = classification,
        ComponentId = AgentComponentCapabilityProfile.AgentNavMenuComponentId,
        ActionId = AgentComponentCapabilityProfile.NavigationNavigateToActionId,
        Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["uri"] = uri
        },
        Reason = $"Navigate to {uri}"
    };

    /// <summary>
    /// Creates a filter intent for a data grid.
    /// </summary>
    public static ResolvedIntent Filter(
        IntentClassification classification,
        string? column = null,
        string? @operator = null,
        object? value = null) => new()
    {
        Classification = classification,
        ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
        ActionId = AgentComponentCapabilityProfile.DataGridFilterActionId,
        Arguments = BuildArguments(("column", column), ("operator", @operator), ("value", value)),
        Reason = "Filter data grid"
    };

    /// <summary>
    /// Creates a sort intent for a data grid.
    /// </summary>
    public static ResolvedIntent Sort(
        IntentClassification classification,
        string? column = null,
        string? direction = null) => new()
    {
        Classification = classification,
        ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
        ActionId = AgentComponentCapabilityProfile.DataGridSortActionId,
        Arguments = BuildArguments(("column", column), ("direction", direction)),
        Reason = "Sort data grid"
    };

    private static Dictionary<string, object?> BuildArguments(params (string key, object? value)[] pairs)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            if (value is not null)
            {
                dict[key] = value;
            }
        }
        return dict;
    }
}
