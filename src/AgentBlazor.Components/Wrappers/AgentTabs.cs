using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using RuntimeComponentState = AgentBlazor.Core.Runtime.Components.ComponentState;

namespace AgentBlazor.Components;

public class AgentTabs : MudTabs, IAgentControllable, IAsyncDisposable
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

    public string ComponentType => "Tabs";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private bool _hasRendered;

    [AgentReadable("Currently active tab index (0-based)")]
    public int CurrentTabIndex => ActivePanelIndex;

    [AgentReadable("Available tab labels")]
    public string[] AvailableTabs => Panels
        .Select(panel => panel.Text)
        .Where(static text => !string.IsNullOrWhiteSpace(text))
        .Select(static text => text!)
        .ToArray();

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

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            _hasRendered = true;
        }
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState() => new()
    {
        ["activePanelIndex"] = ActivePanelIndex,
        ["availableTabs"] = AvailableTabs
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

    [AgentAction("Switch to a specific tab by index (0-based)", ActionId = "switch_tab")]
    public async Task<ActionResult> SwitchTab(
        [AgentParam("Tab index, starting from 0", Required = true)] int index)
    {
        if (index < 0)
        {
            return ActionResult.Failure("Tab index must be 0 or greater.");
        }

        if (Panels.Count > 0 && index >= Panels.Count)
        {
            return ActionResult.NeedsClarification(
                $"Tab index '{index}' is out of range. Available tab count is {Panels.Count}.");
        }

        await SwitchTabSafelyAsync(index);
        return ActionResult.Applied($"Switched to tab {index}.");
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
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "tabs";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private async Task SwitchTabSafelyAsync(int index)
    {
        if (Panels.Count == 0)
        {
            ActivePanelIndex = index;
            await ActivePanelIndexChanged.InvokeAsync(index);
            return;
        }

        try
        {
            await ActivatePanelAsync(index);
        }
        catch (InvalidOperationException)
        {
            ActivePanelIndex = index;
            await ActivePanelIndexChanged.InvokeAsync(index);
        }
        catch (Exception) when (!_hasRendered)
        {
            ActivePanelIndex = index;
            await ActivePanelIndexChanged.InvokeAsync(index);
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

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _runtimeSupport?.Dispose();
        await base.DisposeAsync();
    }
}
