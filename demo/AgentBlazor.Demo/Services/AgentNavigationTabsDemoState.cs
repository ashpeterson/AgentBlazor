using AgentBlazor.Components;
using AgentBlazor.Runtime;

namespace AgentBlazor.Demo.Services;

public sealed class AgentNavigationTabsDemoState
{
    private readonly object _gate = new();
    private readonly Queue<string> _recentEvents = new();
    private int _activeTabIndex;
    private string _currentTarget = "/suppliers";

    public event Action? Changed;

    public AgentNavigationTabsDemoSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new AgentNavigationTabsDemoSnapshot(
                    ActiveTabIndex: _activeTabIndex,
                    CurrentTarget: _currentTarget,
                    RecentEvents: _recentEvents.ToArray());
            }
        }
    }

    public ComponentActionExecutionResult ApplyNavigationAction(
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        bool succeeded;
        string message;
        lock (_gate)
        {
            (succeeded, message) = actionId.Trim().ToLowerInvariant() switch
            {
                AgentComponentV1CapabilityProfile.NavigationNavigateToActionId =>
                    NavigateToUnsafe(ResolveTarget(arguments), external: false),
                AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId =>
                    NavigateToUnsafe(ResolveTarget(arguments), external: true),
                _ => (false, $"AgentNavMenu action '{actionId}' is not implemented by demo state.")
            };

            EnqueueEventUnsafe($"{DateTime.UtcNow:HH:mm:ss} - {message}");
        }

        Changed?.Invoke();
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            ActionId: actionId,
            Succeeded: succeeded,
            Message: message);
    }

    public ComponentActionExecutionResult ApplyTabsAction(
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        bool succeeded;
        string message;
        lock (_gate)
        {
            (succeeded, message) = actionId.Trim().ToLowerInvariant() switch
            {
                AgentComponentV1CapabilityProfile.TabsSwitchTabActionId => SwitchTabUnsafe(arguments),
                _ => (false, $"AgentTabs action '{actionId}' is not implemented by demo state.")
            };

            EnqueueEventUnsafe($"{DateTime.UtcNow:HH:mm:ss} - {message}");
        }

        Changed?.Invoke();
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            ActionId: actionId,
            Succeeded: succeeded,
            Message: message);
    }

    public void SetTabIndexFromUi(int index)
    {
        lock (_gate)
        {
            _activeTabIndex = Math.Max(0, index);
            EnqueueEventUnsafe($"{DateTime.UtcNow:HH:mm:ss} - UI switched to tab {_activeTabIndex}.");
        }

        Changed?.Invoke();
    }

    private (bool Succeeded, string Message) NavigateToUnsafe(string? target, bool external)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return (false, "Navigation action requires 'uri', 'url', or 'target' parameter.");
        }

        _currentTarget = target;
        return external
            ? (true, $"Requested external navigation to {target}.")
            : (true, $"Navigated to {target}.");
    }

    private (bool Succeeded, string Message) SwitchTabUnsafe(IReadOnlyDictionary<string, object?>? arguments)
    {
        var requested = TryGetInt(arguments, "index");
        if (requested is null)
        {
            return (false, "AgentTabs.switch_tab requires 'index' parameter.");
        }

        _activeTabIndex = Math.Max(0, requested.Value);
        return (true, $"Switched to tab index {_activeTabIndex}.");
    }

    private void EnqueueEventUnsafe(string message)
    {
        _recentEvents.Enqueue(message);
        while (_recentEvents.Count > 8)
        {
            _ = _recentEvents.Dequeue();
        }
    }

    private static string? ResolveTarget(IReadOnlyDictionary<string, object?>? arguments) =>
        TryGetString(arguments, "uri") ??
        TryGetString(arguments, "url") ??
        TryGetString(arguments, "target");

    private static string? TryGetString(IReadOnlyDictionary<string, object?>? arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e => e.GetString(),
            System.Text.Json.JsonElement e => e.ToString(),
            _ => value.ToString()
        };
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, object?>? arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } e when e.TryGetInt32(out var parsed) => parsed,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e when int.TryParse(e.GetString(), out var parsed) => parsed,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }
}

public sealed class DemoNavigationActionExecutor(
    IAgentComponentRegistry componentRegistry,
    AgentNavigationTabsDemoState state) : INavigationActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        NavigationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (handled, componentResult) = await RegisteredComponentExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "NavMenu",
            componentId: AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            actionId: request.ActionId,
            arguments: request.Arguments,
            cancellationToken);

        var stateResult = state.ApplyNavigationAction(request.ActionId, request.Arguments);
        return handled ? componentResult : stateResult;
    }
}

public sealed class DemoTabsActionExecutor(
    IAgentComponentRegistry componentRegistry,
    AgentNavigationTabsDemoState state) : ITabsActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        TabsActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (handled, componentResult) = await RegisteredComponentExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "Tabs",
            componentId: AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            actionId: request.ActionId,
            arguments: request.Arguments,
            cancellationToken);

        var stateResult = state.ApplyTabsAction(request.ActionId, request.Arguments);
        return handled ? componentResult : stateResult;
    }
}

public sealed record AgentNavigationTabsDemoSnapshot(
    int ActiveTabIndex,
    string CurrentTarget,
    IReadOnlyList<string> RecentEvents);
