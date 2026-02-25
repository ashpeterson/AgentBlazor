using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Agents;

public sealed record DataGridActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record DialogActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record FormActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record NavigationActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public sealed record TabsActionRequest(
    string ActionId,
    IReadOnlyDictionary<string, object?>? Arguments = null);

public interface IDataGridActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        DataGridActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IDialogActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        DialogActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IFormActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        FormActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface INavigationActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        NavigationActionRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITabsActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        TabsActionRequest request,
        CancellationToken cancellationToken = default);
}

internal static class RegisteredComponentActionExecutorBridge
{
    public static IReadOnlyDictionary<string, object?> NormalizeArguments(
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments)
        => ComponentActionArgumentNormalizer.Normalize(componentId, actionId, arguments);

    public static string? TryGetAgentId(IReadOnlyDictionary<string, object?>? arguments)
        => TryGetString(arguments, "agentId") ??
           TryGetString(arguments, "target");

    public static PendingActionOptions BuildPendingOptions(
        IComponentRouteRegistry componentRouteRegistry,
        string componentId)
    {
        ArgumentNullException.ThrowIfNull(componentRouteRegistry);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        if (componentRouteRegistry.TryGetRoute(componentId, out var route) && !string.IsNullOrWhiteSpace(route))
        {
            return new PendingActionOptions
            {
                TimeToLive = TimeSpan.FromMinutes(10),
                Dependency = PendingActionDependency.NavigationCompletion,
                RequiredRoutePath = route
            };
        }

        return new PendingActionOptions
        {
            TimeToLive = TimeSpan.FromMinutes(5),
            Dependency = PendingActionDependency.ComponentRegistration
        };
    }

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

        var normalizedArguments = NormalizeArguments(componentId, actionId, arguments);
        var target = ResolveTarget(componentRegistry, expectedComponentType, actionId, normalizedArguments);
        if (target is null)
        {
            return (false, new ComponentActionExecutionResult(
                ComponentId: componentId,
                ActionId: actionId,
                Outcome: ActionOutcome.Failed,
                Message: $"No registered {expectedComponentType} component is available for action '{actionId}'."));
        }

        var action = AgentAction.Create(actionId, normalizedArguments);
        var execution = await target.ExecuteActionAsync(action, cancellationToken);
        return (true, new ComponentActionExecutionResult(
            ComponentId: componentId,
            ActionId: actionId,
            Outcome: execution.Outcome,
            Message: execution.Message));
    }

    private static IAgentControllable? ResolveTarget(
        IAgentComponentRegistry componentRegistry,
        string expectedComponentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments)
    {
        var agentId = TryGetAgentId(arguments);
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            if (componentRegistry.TryGet(agentId, out var targeted) &&
                string.Equals(targeted.ComponentType, expectedComponentType, StringComparison.OrdinalIgnoreCase))
            {
                return targeted;
            }

            var typedCandidates = componentRegistry.GetAll()
                .Where(component => string.Equals(component.ComponentType, expectedComponentType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(component => component.AgentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (typedCandidates.Length > 0)
            {
                var resolution = DeterministicEntityResolver.Resolve(
                    agentId,
                    typedCandidates.Select(static candidate => candidate.AgentId),
                    minConfidence: 0.7,
                    ambiguityMargin: 0.05);
                if (resolution.Status is EntityResolutionStatus.Resolved && !string.IsNullOrWhiteSpace(resolution.Value))
                {
                    return typedCandidates.FirstOrDefault(candidate =>
                        string.Equals(candidate.AgentId, resolution.Value, StringComparison.OrdinalIgnoreCase));
                }
            }

            // If a specific target is requested, do not execute on a different instance.
            return null;
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
            if (capability.Actions.Any(action => string.Equals(action.ActionId, actionId, StringComparison.OrdinalIgnoreCase)))
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

internal sealed class NoOpDataGridActionExecutor(
    IAgentComponentRegistry componentRegistry,
    IAgentNavigationIntentService navigationIntentService,
    IComponentRouteRegistry componentRouteRegistry) : IDataGridActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        DataGridActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedArguments = RegisteredComponentActionExecutorBridge.NormalizeArguments(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            request.ActionId,
            request.Arguments);
        var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "DataGrid",
            componentId: AgentComponentCapabilityProfile.AgentDataGridComponentId,
            actionId: request.ActionId,
            arguments: normalizedArguments,
            cancellationToken);
        if (handled)
        {
            return result;
        }

        navigationIntentService.Enqueue(
            "DataGrid",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(normalizedArguments),
            AgentAction.Create(request.ActionId, normalizedArguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions(
                componentRouteRegistry,
                AgentComponentCapabilityProfile.AgentDataGridComponentId));
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentDataGrid action '{request.ActionId}' until a matching DataGrid component is registered.");
    }
}

internal sealed class NoOpDialogActionExecutor(
    IAgentComponentRegistry componentRegistry,
    IAgentNavigationIntentService navigationIntentService,
    IComponentRouteRegistry componentRouteRegistry) : IDialogActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        DialogActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedArguments = RegisteredComponentActionExecutorBridge.NormalizeArguments(
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            request.ActionId,
            request.Arguments);
        var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "Dialog",
            componentId: AgentComponentCapabilityProfile.AgentDialogComponentId,
            actionId: request.ActionId,
            arguments: normalizedArguments,
            cancellationToken);
        if (handled)
        {
            return result;
        }

        navigationIntentService.Enqueue(
            "Dialog",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(normalizedArguments),
            AgentAction.Create(request.ActionId, normalizedArguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions(
                componentRouteRegistry,
                AgentComponentCapabilityProfile.AgentDialogComponentId));
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentDialog action '{request.ActionId}' until a matching Dialog component is registered.");
    }
}

internal sealed class NoOpFormActionExecutor(
    IAgentComponentRegistry componentRegistry,
    IAgentNavigationIntentService navigationIntentService,
    IComponentRouteRegistry componentRouteRegistry) : IFormActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        FormActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedArguments = RegisteredComponentActionExecutorBridge.NormalizeArguments(
            AgentComponentCapabilityProfile.AgentFormComponentId,
            request.ActionId,
            request.Arguments);
        var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "Form",
            componentId: AgentComponentCapabilityProfile.AgentFormComponentId,
            actionId: request.ActionId,
            arguments: normalizedArguments,
            cancellationToken);
        if (handled)
        {
            return result;
        }

        navigationIntentService.Enqueue(
            "Form",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(normalizedArguments),
            AgentAction.Create(request.ActionId, normalizedArguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions(
                componentRouteRegistry,
                AgentComponentCapabilityProfile.AgentFormComponentId));
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentForm action '{request.ActionId}'. It will apply automatically when the form mounts (for example after opening the dialog).");
    }
}

internal sealed class NoOpNavigationActionExecutor(
    IAgentComponentRegistry componentRegistry,
    IAgentNavigationIntentService navigationIntentService,
    IComponentRouteRegistry componentRouteRegistry) : INavigationActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        NavigationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedArguments = RegisteredComponentActionExecutorBridge.NormalizeArguments(
            AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            request.ActionId,
            request.Arguments);
        var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "NavMenu",
            componentId: AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            actionId: request.ActionId,
            arguments: normalizedArguments,
            cancellationToken);
        if (handled)
        {
            return result;
        }

        navigationIntentService.Enqueue(
            "NavMenu",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(normalizedArguments),
            AgentAction.Create(request.ActionId, normalizedArguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions(
                componentRouteRegistry,
                AgentComponentCapabilityProfile.AgentNavMenuComponentId));
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentNavMenu action '{request.ActionId}' until a matching NavMenu component is registered.");
    }
}

internal sealed class NoOpTabsActionExecutor(
    IAgentComponentRegistry componentRegistry,
    IAgentNavigationIntentService navigationIntentService,
    IComponentRouteRegistry componentRouteRegistry) : ITabsActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        TabsActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedArguments = RegisteredComponentActionExecutorBridge.NormalizeArguments(
            AgentComponentCapabilityProfile.AgentTabsComponentId,
            request.ActionId,
            request.Arguments);
        var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "Tabs",
            componentId: AgentComponentCapabilityProfile.AgentTabsComponentId,
            actionId: request.ActionId,
            arguments: normalizedArguments,
            cancellationToken);
        if (handled)
        {
            return result;
        }

        navigationIntentService.Enqueue(
            "Tabs",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(normalizedArguments),
            AgentAction.Create(request.ActionId, normalizedArguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions(
                componentRouteRegistry,
                AgentComponentCapabilityProfile.AgentTabsComponentId));
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentTabsComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentTabs action '{request.ActionId}' until a matching Tabs component is registered.");
    }
}
