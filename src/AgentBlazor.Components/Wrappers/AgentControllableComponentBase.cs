using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace AgentBlazor;

/// <summary>
/// Base class for Blazor components that can be controlled by the agent runtime.
///
/// Subclasses can use [AgentAction] / [AgentReadable] / [AgentParam] attributes for
/// zero-boilerplate registration, or override GetCapability() / GetCurrentState() /
/// ExecuteActionAsync() for full manual control.
/// </summary>
public abstract class AgentControllableComponentBase : ComponentBase, IAgentControllable, IDisposable
{
    [Inject]
    protected IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private NavigationManager? Navigation { get; set; }

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Inject]
    private IAgentDeferredActionEvents? DeferredActionEvents { get; set; }

    [Parameter, EditorRequired]
    public string AgentId { get; set; } = string.Empty;

    private ILogger? _logger;

    public abstract string ComponentType { get; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        ComponentRegistry.Register(this);

        if (Navigation is not null)
        {
            var uri = Navigation.Uri;
            var path = string.IsNullOrEmpty(uri) ? "/" : new Uri(uri).AbsolutePath;
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
            return;
        }

        var pending = NavigationIntentService.Dequeue(ComponentType, AgentId);
        _logger?.LogInformation(
            "[AgentFlow] Component.ApplyIntents: {ComponentType}/{AgentId} applying {Count} pending action(s): [{ActionIds}]",
            ComponentType, AgentId, pending.Count, string.Join(", ", pending.Select(static a => a.Name)));

        foreach (var action in pending)
        {
            var sessionId = TryGetContextValue(action.Parameters, AgentRuntimeContextKeys.SessionId);
            var runId = TryGetContextValue(action.Parameters, AgentRuntimeContextKeys.RunId);
            var result = await ExecuteActionAsync(action);
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
        }

        await RequestComponentRefreshAsync();
    }

    /// <summary>
    /// Returns the component's capability description.
    /// Default implementation uses [AgentAction]-decorated methods via reflection.
    /// Override to provide a fully custom capability descriptor.
    /// </summary>
    public virtual ComponentCapability GetCapability()
        => AgentActionDiscovery.BuildCapability(this);

    /// <summary>
    /// Returns the component's current readable state.
    /// Default implementation uses [AgentReadable]-decorated properties via reflection.
    /// Override to provide a fully custom state snapshot.
    /// </summary>
    public virtual ComponentState GetCurrentState()
        => AgentActionDiscovery.BuildState(this);

    /// <summary>
    /// Dispatches an agent action to the matching [AgentAction]-decorated method.
    /// Override to handle actions manually instead of via attribute discovery.
    /// </summary>
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
            // Unit tests may instantiate wrappers without a renderer — execute directly.
            return await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
        }
    }

    protected Task RequestComponentRefreshAsync()
    {
        try
        {
            return InvokeAsync(StateHasChanged);
        }
        catch (InvalidOperationException)
        {
            // Unit tests may instantiate wrappers without a renderer — ignore.
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
