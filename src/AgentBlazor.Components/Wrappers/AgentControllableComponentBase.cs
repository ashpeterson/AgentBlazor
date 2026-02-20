using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace AgentBlazor;

public abstract class AgentControllableComponentBase : ComponentBase, IAgentControllable, IDisposable
{
    [Inject]
    protected IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Parameter, EditorRequired]
    public string AgentId { get; set; } = string.Empty;

    private ILogger? _logger;

    public abstract string ComponentType { get; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        ComponentRegistry.Register(this);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await ApplyNavigationIntentsAsync();
    }

    private async Task ApplyNavigationIntentsAsync()
    {
        if (!NavigationIntentService.HasPending(ComponentType, AgentId))
        {
            return;
        }

        var pending = NavigationIntentService.Dequeue(ComponentType, AgentId);
        foreach (var action in pending)
        {
            var result = await ExecuteActionAsync(action);
            if (!result.Succeeded)
            {
                _logger?.LogWarning(
                    "Pending action failed for {ComponentType}/{AgentId}: {ActionName} -> {Message}",
                    ComponentType,
                    AgentId,
                    action.Name,
                    result.Message);
            }
        }

        await RequestComponentRefreshAsync();
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
