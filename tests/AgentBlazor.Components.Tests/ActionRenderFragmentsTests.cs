using AgentBlazor.Components.Render;
using AgentBlazor.Core.Models;
using Microsoft.AspNetCore.Components;

namespace AgentBlazor.Components.Tests;

public sealed class ActionRenderFragmentsTests
{
    [Fact]
    public void Resolve_ReturnsMatchingFragment_ForEachLifecycleStatus()
    {
        RenderFragment<ActionRenderContext> inProgress = _ => _ => { };
        RenderFragment<ActionRenderContext> executing = _ => _ => { };
        RenderFragment<ActionRenderContext> complete = _ => _ => { };
        RenderFragment<ActionRenderContext> failed = _ => _ => { };

        var fragments = new ActionRenderFragments(inProgress, executing, complete, failed);

        Assert.Same(inProgress, fragments.Resolve(ActionStatus.InProgress));
        Assert.Same(executing, fragments.Resolve(ActionStatus.Executing));
        Assert.Same(complete, fragments.Resolve(ActionStatus.Complete));
        Assert.Same(failed, fragments.Resolve(ActionStatus.Failed));
    }

    [Fact]
    public void Resolve_Failed_FallsBackToComplete_WhenFailedFragmentMissing()
    {
        RenderFragment<ActionRenderContext> complete = _ => _ => { };

        var fragments = new ActionRenderFragments(
            InProgress: null,
            Executing: null,
            Complete: complete,
            Failed: null);

        Assert.Same(complete, fragments.Resolve(ActionStatus.Failed));
    }
}
