using System.Reflection;
using System.Text;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace AgentBlazor;

internal sealed class AgentControllableComponentRuntimeSupport
{
    private readonly Type _componentType;
    private readonly IAgentControllable _component;
    private readonly IAgentComponentRegistry _componentRegistry;
    private readonly IAgentNavigationIntentService _navigationIntentService;
    private readonly NavigationManager? _navigation;
    private readonly ILogger? _logger;
    private readonly IAgentDeferredActionEvents? _deferredActionEvents;
    private readonly Func<string> _getComponentType;
    private readonly Func<string> _getAgentId;
    private readonly Action<string> _setAgentId;
    private readonly Func<AgentAction, Task<ActionResult>> _executeActionAsync;
    private readonly Func<Task> _requestComponentRefreshAsync;

    public AgentControllableComponentRuntimeSupport(
        Type componentType,
        IAgentControllable component,
        IAgentComponentRegistry componentRegistry,
        IAgentNavigationIntentService navigationIntentService,
        NavigationManager? navigation,
        ILogger? logger,
        IAgentDeferredActionEvents? deferredActionEvents,
        Func<string> getComponentType,
        Func<string> getAgentId,
        Action<string> setAgentId,
        Func<AgentAction, Task<ActionResult>> executeActionAsync,
        Func<Task> requestComponentRefreshAsync)
    {
        _componentType = componentType;
        _component = component;
        _componentRegistry = componentRegistry;
        _navigationIntentService = navigationIntentService;
        _navigation = navigation;
        _logger = logger;
        _deferredActionEvents = deferredActionEvents;
        _getComponentType = getComponentType;
        _getAgentId = getAgentId;
        _setAgentId = setAgentId;
        _executeActionAsync = executeActionAsync;
        _requestComponentRefreshAsync = requestComponentRefreshAsync;
    }

    public void OnInitialized()
    {
        if (string.IsNullOrWhiteSpace(_getAgentId()))
        {
            _setAgentId(ResolveDefaultAgentId(_componentType, _getComponentType()));
        }

        _componentRegistry.Register(_component);

        if (_navigation is not null)
        {
            var uri = _navigation.Uri;
            var path = string.IsNullOrEmpty(uri) ? "/" : new Uri(uri).AbsolutePath;
            _navigationIntentService.MarkNavigationCompleted(path);
        }
    }

    public async Task OnInitializedAsync()
    {
        if (!_navigationIntentService.HasPending(_getComponentType(), _getAgentId()))
        {
            return;
        }

        var pending = _navigationIntentService.Dequeue(_getComponentType(), _getAgentId());
        _logger?.LogInformation(
            "[AgentFlow] Component.ApplyIntents: {ComponentType}/{AgentId} applying {Count} pending action(s): [{ActionIds}]",
            _getComponentType(), _getAgentId(), pending.Count, string.Join(", ", pending.Select(static a => a.Name)));

        foreach (var action in pending)
        {
            var sessionId = TryGetContextValue(action.Parameters, AgentRuntimeContextKeys.SessionId);
            var runId = TryGetContextValue(action.Parameters, AgentRuntimeContextKeys.RunId);
            var result = await _executeActionAsync(action);
            _logger?.LogInformation(
                "[AgentFlow] Component.ApplyIntents: {ComponentType}/{AgentId} applied {ActionName} Succeeded={Succeeded} Message={Message}",
                _getComponentType(), _getAgentId(), action.Name, result.Succeeded, result.Message);
            _deferredActionEvents?.Publish(new DeferredComponentActionEvent(
                ComponentType: _getComponentType(),
                AgentId: _getAgentId(),
                ActionId: action.Name,
                Succeeded: result.Succeeded,
                Message: result.Message,
                OccurredAt: DateTimeOffset.UtcNow,
                SessionId: sessionId,
                RunId: runId));
        }

        await _requestComponentRefreshAsync();
    }

    public void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(_getAgentId()))
        {
            _ = _componentRegistry.Unregister(_getAgentId());
        }
    }

    public static string ResolveDefaultComponentType(Type componentType)
    {
        var componentAttribute = componentType.GetCustomAttribute<AgentComponentAttribute>(inherit: true);
        if (!string.IsNullOrWhiteSpace(componentAttribute?.ComponentType))
        {
            return componentAttribute.ComponentType.Trim();
        }

        var typeName = componentType.Name;
        var genericSeparator = typeName.IndexOf('`');
        return genericSeparator > 0 ? typeName[..genericSeparator] : typeName;
    }

    public static string ResolveDefaultAgentId(Type componentType, string componentTypeName)
    {
        var componentAttribute = componentType.GetCustomAttribute<AgentComponentAttribute>(inherit: true);
        if (!string.IsNullOrWhiteSpace(componentAttribute?.AgentId))
        {
            return componentAttribute.AgentId.Trim();
        }

        var prefix = !string.IsNullOrWhiteSpace(componentAttribute?.AgentIdPrefix)
            ? componentAttribute.AgentIdPrefix
            : componentTypeName;

        var normalizedPrefix = ToKebabCase(prefix ?? string.Empty);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{normalizedPrefix}-{suffix}";
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "component";
        }

        var builder = new StringBuilder(value.Length + 8);
        var previousWasSeparator = false;

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (!char.IsLetterOrDigit(current))
            {
                if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    previousWasSeparator = true;
                }
                continue;
            }

            if (char.IsUpper(current))
            {
                var hasPrevious = builder.Length > 0;
                var previousIsLowerOrDigit =
                    i > 0 &&
                    (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));

                if (hasPrevious && !previousWasSeparator && previousIsLowerOrDigit)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(current));
                previousWasSeparator = false;
                continue;
            }

            builder.Append(char.ToLowerInvariant(current));
            previousWasSeparator = false;
        }

        var kebab = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(kebab) ? "component" : kebab;
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
