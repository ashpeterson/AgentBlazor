using System.Collections.Concurrent;
using AgentBlazor.Core.Models;

namespace AgentBlazor.Components.Render;

/// <summary>
/// Scoped in-memory registry that maps (agentId, actionId) pairs to their render fragment sets.
/// Scoped per Blazor circuit so each page has its own set of registered render overrides.
/// </summary>
internal sealed class InMemoryAgentActionRenderRegistry : IAgentActionRenderRegistry
{
    private readonly ConcurrentDictionary<(string AgentId, string ActionId), ActionRenderFragments> _registry =
        new();

    public void Register(string agentId, string actionId, ActionRenderFragments fragments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(fragments);

        _registry[(Normalize(agentId), Normalize(actionId))] = fragments;
    }

    public void Unregister(string agentId, string actionId)
    {
        _registry.TryRemove((Normalize(agentId), Normalize(actionId)), out _);
    }

    public ActionRenderFragments? TryGet(string agentId, string actionId)
    {
        _registry.TryGetValue((Normalize(agentId), Normalize(actionId)), out var fragments);
        return fragments;
    }

    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();
}
