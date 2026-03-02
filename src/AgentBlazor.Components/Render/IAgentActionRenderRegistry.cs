using AgentBlazor.Core.Models;
using Microsoft.AspNetCore.Components;

namespace AgentBlazor.Components.Render;

public interface IAgentActionRenderRegistry
{
    void Register(string agentId, string actionId, ActionRenderFragments fragments);
    void Unregister(string agentId, string actionId);
    ActionRenderFragments? TryGet(string agentId, string actionId);
}

public record ActionRenderFragments(
    RenderFragment<ActionRenderContext>? InProgress,
    RenderFragment<ActionRenderContext>? Executing,
    RenderFragment<ActionRenderContext>? Complete);
