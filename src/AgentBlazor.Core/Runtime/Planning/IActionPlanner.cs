using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Plans actions from user prompts using structured output.
/// Returns ONLY structured plans - no execution, no heuristics.
/// </summary>
public interface IStructuredActionPlanner
{
    /// <summary>
    /// Whether a backing chat provider/client is configured.
    /// </summary>
    bool IsProviderConfigured { get; }

    /// <summary>
    /// Produces a structured action plan from the user's request.
    /// The LLM must return a JSON plan, not freeform text with tool calls.
    /// </summary>
    Task<ActionPlan> PlanAsync(
        ActionPlanRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for action planning.
/// </summary>
public sealed record ActionPlanRequest
{
    public required string UserMessage { get; init; }
    public required string SessionId { get; init; }
    public string? UserId { get; init; }
    public bool GenerateUi { get; init; }
    public GeneratedUiActionInvocation? GeneratedUiAction { get; init; }

    /// <summary>
    /// Available components and their actions.
    /// The planner can only reference these.
    /// </summary>
    public required IReadOnlyList<AvailableComponent> AvailableComponents { get; init; }

    /// <summary>
    /// Currently mounted component instances with their state.
    /// </summary>
    public IReadOnlyList<MountedComponentState> MountedComponents { get; init; } = [];

    /// <summary>
    /// Conversation context for multi-turn interactions.
    /// </summary>
    public IReadOnlyList<ConversationTurn> ConversationHistory { get; init; } = [];

    /// <summary>
    /// Available app routes (path, description, aliases) for intent→route navigation.
    /// Planner uses this to output navigate_to with the correct uri when the user is not on the target page.
    /// </summary>
    public IReadOnlyList<AvailableRoute> AvailableRoutes { get; init; } = [];

    /// <summary>
    /// Optional agent-level instructions to bias deterministic planning for a specific app/domain.
    /// </summary>
    public string? AgentInstructions { get; init; }

    /// <summary>
    /// The current route/URL the user is on. Helps the planner understand context
    /// and avoid unnecessary navigation when already on the correct page.
    /// </summary>
    public string? CurrentRoute { get; init; }
}

/// <summary>
/// A route the user can navigate to (from [Route] discovery).
/// </summary>
public sealed record AvailableRoute
{
    public required string Path { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Aliases { get; init; }
}

/// <summary>
/// A component available for planning.
/// </summary>
public sealed record AvailableComponent
{
    public required string ComponentId { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<AvailableAction> Actions { get; init; }
}

/// <summary>
/// An action available on a component.
/// </summary>
public sealed record AvailableAction
{
    public required string ActionId { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ActionParameter> Parameters { get; init; }
    public bool RequiresApproval { get; init; }
}

/// <summary>
/// A parameter for an action.
/// </summary>
public sealed record ActionParameter
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required bool Required { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? AllowedValues { get; init; }
}

/// <summary>
/// State of a mounted component instance.
/// </summary>
public sealed record MountedComponentState
{
    public required string AgentId { get; init; }
    public required string ComponentType { get; init; }
    public IReadOnlyDictionary<string, string> State { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// A turn in the conversation history.
/// </summary>
public sealed record ConversationTurn
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
