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
    RenderFragment<ActionRenderContext>? Complete,
    RenderFragment<ActionRenderContext>? Failed)
{
    public RenderFragment<ActionRenderContext>? Resolve(ActionStatus status) =>
        status switch
        {
            ActionStatus.InProgress => InProgress,
            ActionStatus.Executing => Executing,
            ActionStatus.Complete => Complete,
            ActionStatus.Failed => Failed ?? Complete,
            _ => null
        };
}
