using AgentBlazor.Runtime;

namespace AgentBlazor.Demo.Services;

internal static class RegisteredComponentExecutorBridge
{
    public static async Task<(bool Handled, ComponentActionExecutionResult Result)> TryExecuteAsync(
        IAgentComponentRegistry componentRegistry,
        string expectedComponentType,
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(componentRegistry);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedComponentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var normalizedArguments = ComponentActionArgumentNormalizer.Normalize(componentId, actionId, arguments);

        var target = ResolveTarget(componentRegistry, expectedComponentType, actionId, normalizedArguments);
        if (target is null)
        {
            return (false, new ComponentActionExecutionResult(
                ComponentId: componentId,
                ActionId: actionId,
                Succeeded: false,
                Message: $"No registered {expectedComponentType} component is available for action '{actionId}'."));
        }

        var action = AgentAction.Create(actionId, normalizedArguments);
        var execution = await target.ExecuteActionAsync(action, cancellationToken);

        return (true, new ComponentActionExecutionResult(
            ComponentId: componentId,
            ActionId: actionId,
            Succeeded: execution.Succeeded,
            Message: execution.Message));
    }

    private static IAgentControllable? ResolveTarget(
        IAgentComponentRegistry componentRegistry,
        string expectedComponentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments)
    {
        var agentId = TryGetString(arguments, "agentId");
        if (!string.IsNullOrWhiteSpace(agentId) &&
            componentRegistry.TryGet(agentId, out var targeted) &&
            string.Equals(targeted.ComponentType, expectedComponentType, StringComparison.OrdinalIgnoreCase))
        {
            return targeted;
        }

        var candidates = componentRegistry.GetAll()
            .Where(component => string.Equals(component.ComponentType, expectedComponentType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(component => component.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            var capability = candidate.GetCapability();
            if (capability.Actions.Any(action =>
                    string.Equals(action.ActionId, actionId, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?>? arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e => e.GetString(),
            System.Text.Json.JsonElement e => e.ToString(),
            _ => raw.ToString()
        };
    }
}
