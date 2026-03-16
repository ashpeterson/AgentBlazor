using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace AgentBlazor.Components;

public class AgentDialog : MudDialog, IAgentControllable, IDisposable
{
    [Inject]
    private IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private NavigationManager? Navigation { get; set; }

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Inject]
    private IAgentDeferredActionEvents? DeferredActionEvents { get; set; }

    [Parameter]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Callback invoked when the agent calls the Confirm action.
    /// Wire this up to handle confirmation logic (for example, form submission or delete confirmation).
    /// </summary>
    [Parameter]
    public Func<Task<ActionResult>>? OnConfirm { get; set; }

    public string ComponentType => "Dialog";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;

    [AgentReadable("Whether the dialog is currently visible")]
    public bool IsVisible => Visible;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        _runtimeSupport ??= CreateRuntimeSupport();
        _runtimeSupport.OnInitialized();
        EnsureAgentUserAttributes();
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _runtimeSupport ??= CreateRuntimeSupport();
        await _runtimeSupport.OnInitializedAsync();
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    public void Dispose()
    {
        _runtimeSupport?.Dispose();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual ComponentState GetCurrentState() => new()
    {
        ["visible"] = Visible
    };

    public virtual async Task<ActionResult> ExecuteActionAsync(
        AgentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ActionResult result = ActionResult.Applied($"Executed '{action.Name}'.");
            await InvokeAsync(async () =>
            {
                result = await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
            });
            return result;
        }
        catch (InvalidOperationException)
        {
            return await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
        }
    }

    [AgentAction("Open this dialog. Call this before setting any form fields inside if the dialog is closed.")]
    public async Task Open()
    {
        await SetVisibleSafelyAsync(true);
    }

    [AgentAction("Close this dialog")]
    public async Task Close()
    {
        await SetVisibleSafelyAsync(false);
    }

    [AgentAction("Confirm the dialog action", RequiresApproval = true)]
    public async Task<ActionResult> Confirm()
    {
        if (OnConfirm is null)
        {
            return ActionResult.NeedsClarification("No confirm handler configured for this dialog. Set the OnConfirm parameter.");
        }

        try
        {
            ActionResult result = ActionResult.Failure("Confirm handler did not return a result.");
            await InvokeAsync(async () => result = await OnConfirm());
            return result;
        }
        catch (InvalidOperationException)
        {
            return await OnConfirm();
        }
    }

    private AgentControllableComponentRuntimeSupport CreateRuntimeSupport()
    {
        return new AgentControllableComponentRuntimeSupport(
            componentType: GetType(),
            component: this,
            componentRegistry: ComponentRegistry,
            navigationIntentService: NavigationIntentService,
            navigation: Navigation,
            logger: _logger,
            deferredActionEvents: DeferredActionEvents,
            getComponentType: () => ComponentType,
            getAgentId: () => AgentId,
            setAgentId: value => AgentId = value,
            executeActionAsync: action => ExecuteActionAsync(action),
            requestComponentRefreshAsync: RequestComponentRefreshAsync);
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= new Dictionary<string, object?>();
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "dialog";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private async Task SetVisibleSafelyAsync(bool visible)
    {
        try
        {
            await InvokeAsync(async () =>
            {
                Visible = visible;
                await VisibleChanged.InvokeAsync(visible);
                StateHasChanged();
            });
        }
        catch (InvalidOperationException)
        {
            Visible = visible;
            await VisibleChanged.InvokeAsync(visible);
        }
    }

    private Task RequestComponentRefreshAsync()
    {
        try
        {
            return InvokeAsync(StateHasChanged);
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }
}
