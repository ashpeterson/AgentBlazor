using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace AgentBlazor.Components;

public class AgentNavMenu : MudNavMenu, IAgentControllable, IDisposable
{
    [Inject]
    private IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Inject]
    private IAgentDeferredActionEvents? DeferredActionEvents { get; set; }

    [Parameter]
    public string AgentId { get; set; } = string.Empty;

    public string ComponentType => "NavMenu";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;

    [AgentReadable("Current page URI")]
    public string CurrentUri => Navigation.Uri;

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
        ["uri"] = Navigation.Uri,
        ["dense"] = Dense
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

    [AgentAction("Navigate to an internal application route")]
    public Task<ActionResult> NavigateTo(
        [AgentParam("The route URI to navigate to (e.g. /demo/suppliers)", Required = true)] string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return Task.FromResult(ActionResult.NeedsClarification("Action 'navigate_to' requires a 'uri' parameter."));
        }

        Navigation.NavigateTo(uri, forceLoad: false);
        return Task.FromResult(ActionResult.Applied($"Navigated to {uri}."));
    }

    [AgentAction("Navigate to an external URL", RequiresApproval = true)]
    public Task<ActionResult> NavigateExternal(
        [AgentParam("The full external URL to navigate to", Required = true)] string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(ActionResult.NeedsClarification("Action 'navigate_external' requires a 'url' parameter."));
        }

        Navigation.NavigateTo(url, forceLoad: true);
        return Task.FromResult(ActionResult.Applied($"Navigated externally to {url}."));
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

        UserAttributes["data-ab-type"] = "navmenu";
        UserAttributes["data-ab-agentid"] = AgentId;
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
