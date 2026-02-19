using AgentBlazor.Components;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;

namespace AgentBlazor;

public abstract class AgentControllableComponentBase : ComponentBase, IAgentControllable, IDisposable
{
    [Inject]
    protected IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Parameter, EditorRequired]
    public string AgentId { get; set; } = string.Empty;

    public abstract string ComponentType { get; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ComponentRegistry.Register(this);
    }

    public abstract ComponentCapability GetCapability();

    public abstract ComponentState GetCurrentState();

    public abstract Task<ActionResult> ExecuteActionAsync(
        AgentAction action,
        CancellationToken cancellationToken = default);

    protected static AgentAction NormalizeAction(
        string componentId,
        AgentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var normalized = ComponentActionArgumentNormalizer.Normalize(
            componentId,
            action.Name,
            action.Parameters);
        return AgentAction.Create(action.Name, normalized);
    }

    protected Task RequestComponentRefreshAsync()
    {
        try
        {
            return InvokeAsync(StateHasChanged);
        }
        catch (InvalidOperationException)
        {
            // Unit tests instantiate wrappers without attaching them to a renderer.
            return Task.CompletedTask;
        }
    }

    public virtual void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(AgentId))
        {
            _ = ComponentRegistry.Unregister(AgentId);
        }
    }
}
