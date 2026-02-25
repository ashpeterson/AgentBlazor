using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;

namespace AgentBlazor;

public abstract class AgentControllableComponentBase : ComponentBase, IAgentControllable, IDisposable
{
    [Inject]
    protected IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private IComponentRouteRegistry? ComponentRouteRegistry { get; set; }

    [Inject]
    private NavigationManager? Navigation { get; set; }

    [Inject]
    protected IComponentActionArgumentResolver? ActionArgumentResolver { get; set; }

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Inject]
    private IAgentDeferredActionEvents? DeferredActionEvents { get; set; }

    [Parameter, EditorRequired]
    public string AgentId { get; set; } = string.Empty;

    private ILogger? _logger;

    public abstract string ComponentType { get; }

    /// <summary>
    /// Component id used for route registration (e.g. AgentDataGrid). Used to know which route hosts this component when navigating.
    /// </summary>
    protected abstract string ComponentIdForRoute { get; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        ComponentRegistry.Register(this);

        if (ComponentRouteRegistry is not null && Navigation is not null)
        {
            var uri = Navigation.Uri;
            var path = string.IsNullOrEmpty(uri) ? "/" : new Uri(uri).AbsolutePath;
            ComponentRouteRegistry.Register(ComponentIdForRoute, path);
            NavigationIntentService.MarkNavigationCompleted(path);
        }
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
            _logger?.LogDebug("[AgentFlow] Component.ApplyIntents: {ComponentType}/{AgentId} has no pending actions.", ComponentType, AgentId);
            return;
        }

        var pending = NavigationIntentService.Dequeue(ComponentType, AgentId);
        _logger?.LogInformation(
            "[AgentFlow] Component.ApplyIntents: {ComponentType}/{AgentId} applying {Count} pending action(s): [{ActionIds}]",
            ComponentType, AgentId, pending.Count, string.Join(", ", pending.Select(static a => a.Name)));

        foreach (var action in pending)
        {
            var arguments = action.Parameters;
            if (ActionArgumentResolver is not null)
            {
                var state = GetCurrentState();
                arguments = ActionArgumentResolver.Resolve(ComponentType, action.Name, action.Parameters, state);
            }

            var resolvedAction = AgentAction.Create(action.Name, arguments);
            var sessionId = TryGetContextValue(resolvedAction.Parameters, AgentRuntimeContextKeys.SessionId);
            var runId = TryGetContextValue(resolvedAction.Parameters, AgentRuntimeContextKeys.RunId);
            var result = await ExecuteActionAsync(resolvedAction);
            _logger?.LogInformation(
                "[AgentFlow] Component.ApplyIntents: {ComponentType}/{AgentId} applied {ActionName} Succeeded={Succeeded} Message={Message}",
                ComponentType, AgentId, action.Name, result.Succeeded, result.Message);
            DeferredActionEvents?.Publish(new DeferredComponentActionEvent(
                ComponentType: ComponentType,
                AgentId: AgentId,
                ActionId: action.Name,
                Succeeded: result.Succeeded,
                Message: result.Message,
                OccurredAt: DateTimeOffset.UtcNow,
                SessionId: sessionId,
                RunId: runId));
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

        ComponentRouteRegistry?.Unregister(ComponentIdForRoute);
    }

    private static string? TryGetContextValue(
        IReadOnlyDictionary<string, object?>? parameters,
        string key)
    {
        if (parameters is null ||
            !parameters.TryGetValue(key, out var raw) ||
            raw is null)
        {
            return null;
        }

        var value = raw switch
        {
            string text => text,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } json =>
                json.GetString() ?? string.Empty,
            System.Text.Json.JsonElement json => json.ToString(),
            _ => raw.ToString() ?? string.Empty
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
