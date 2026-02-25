using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Resolves action arguments against the target component's schema and semantics
/// so that LLM- or intent-produced values (e.g. "Priority", "High") are mapped
/// to the component's actual parameters (e.g. column "PriorityScore", value 70).
/// Applied at dequeue/apply time when the component has real state (columns, aliases, value mappings).
/// </summary>
public interface IComponentActionArgumentResolver
{
    /// <summary>
    /// Resolves arguments using the component's current state (e.g. columns, columnAliases, valueMappings).
    /// Returns a new dictionary with resolved values; does not mutate the input.
    /// </summary>
    IReadOnlyDictionary<string, object?> Resolve(
        string componentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        ComponentState componentState);
}
