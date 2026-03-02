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
    private enum TargetResolutionStatus
    {
        Resolved,
        NotAvailable,
        Ambiguous
    }

    private sealed record TargetResolution(
        TargetResolutionStatus Status,
        IAgentControllable? Target = null,
        string? Message = null);

    public static string? TryGetAgentId(IReadOnlyDictionary<string, object?>? arguments)
        => TryGetString(arguments, "agentId") ??
           TryGetString(arguments, "target");

    public static string? TryGetSessionId(IReadOnlyDictionary<string, object?>? arguments)
        => TryGetString(arguments, AgentRuntimeContextKeys.SessionId);

    public static PendingActionOptions BuildPendingOptions()
    {
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

        var resolution = ResolveTarget(componentRegistry, expectedComponentType, actionId, arguments);
        if (resolution.Status is TargetResolutionStatus.Ambiguous)
        {
            return (true, new ComponentActionExecutionResult(
                ComponentId: componentId,
                ActionId: actionId,
                Outcome: ActionOutcome.NeedsClarification,
                Message: resolution.Message ??
                         $"Action '{actionId}' requires an explicit 'agentId' because multiple {expectedComponentType} components are available."));
        }

        if (resolution.Status is not TargetResolutionStatus.Resolved || resolution.Target is null)
        {
            return (false, new ComponentActionExecutionResult(
                ComponentId: componentId,
                ActionId: actionId,
                Outcome: ActionOutcome.Failed,
                Message: $"No registered {expectedComponentType} component is available for action '{actionId}'."));
        }

        var action = AgentAction.Create(actionId, arguments);
        var execution = await resolution.Target.ExecuteActionAsync(action, cancellationToken);
        return (true, new ComponentActionExecutionResult(
            ComponentId: componentId,
            ActionId: actionId,
            Outcome: execution.Outcome,
            Message: execution.Message));
    }

    private static TargetResolution ResolveTarget(
        IAgentComponentRegistry componentRegistry,
        string expectedComponentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments)
    {
        var typedCandidates = componentRegistry.GetAll()
            .Where(component => string.Equals(component.ComponentType, expectedComponentType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(component => component.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var agentId = TryGetAgentId(arguments);
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            if (componentRegistry.TryGet(agentId, out var targeted) &&
                string.Equals(targeted.ComponentType, expectedComponentType, StringComparison.OrdinalIgnoreCase))
            {
                return new TargetResolution(TargetResolutionStatus.Resolved, targeted);
            }

            if (typedCandidates.Length > 0)
            {
                var resolved = typedCandidates.FirstOrDefault(candidate =>
                    candidate.AgentId.Contains(agentId, StringComparison.OrdinalIgnoreCase));
                if (resolved is not null)
                {
                    return new TargetResolution(TargetResolutionStatus.Resolved, resolved);
                }
            }

            // If a specific target is requested, do not execute on a different instance.
            return new TargetResolution(TargetResolutionStatus.NotAvailable);
        }

        if (typedCandidates.Length == 0)
        {
            return new TargetResolution(TargetResolutionStatus.NotAvailable);
        }

        var capableCandidates = typedCandidates
            .Where(candidate => candidate.GetCapability().Actions.Any(action =>
                string.Equals(action.ActionId, actionId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (capableCandidates.Length == 1)
        {
            return new TargetResolution(TargetResolutionStatus.Resolved, capableCandidates[0]);
        }

        if (capableCandidates.Length > 1)
        {
            var ids = string.Join(", ", capableCandidates.Select(static candidate => candidate.AgentId));
            return new TargetResolution(
                TargetResolutionStatus.Ambiguous,
                Message: $"Action '{actionId}' matches multiple {expectedComponentType} components ({ids}). Specify 'agentId' to target one.");
        }

        if (typedCandidates.Length == 1)
        {
            return new TargetResolution(TargetResolutionStatus.Resolved, typedCandidates[0]);
        }

        var allIds = string.Join(", ", typedCandidates.Select(static candidate => candidate.AgentId));
        return new TargetResolution(
            TargetResolutionStatus.Ambiguous,
            Message: $"Multiple {expectedComponentType} components are available ({allIds}). Specify 'agentId' to target one.");
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
    AgentComponentRegistryHub componentRegistryHub,
    IAgentNavigationIntentService navigationIntentService) : IDataGridActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        DataGridActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sessionId = RegisteredComponentActionExecutorBridge.TryGetSessionId(request.Arguments);
        if (sessionId is not null && componentRegistryHub.TryGet(sessionId, out var registry))
        {
            var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
                registry,
                expectedComponentType: "DataGrid",
                componentId: AgentComponentCapabilityProfile.AgentDataGridComponentId,
                actionId: request.ActionId,
                arguments: request.Arguments,
                cancellationToken);
            if (handled) return result;
        }

        navigationIntentService.Enqueue(
            "DataGrid",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(request.Arguments),
            AgentAction.Create(request.ActionId, request.Arguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions());
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentDataGrid action '{request.ActionId}' until a matching DataGrid component is registered.");
    }
}

internal sealed class NoOpDialogActionExecutor(
    AgentComponentRegistryHub componentRegistryHub,
    IAgentNavigationIntentService navigationIntentService) : IDialogActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        DialogActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sessionId = RegisteredComponentActionExecutorBridge.TryGetSessionId(request.Arguments);
        if (sessionId is not null && componentRegistryHub.TryGet(sessionId, out var registry))
        {
            var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
                registry,
                expectedComponentType: "Dialog",
                componentId: AgentComponentCapabilityProfile.AgentDialogComponentId,
                actionId: request.ActionId,
                arguments: request.Arguments,
                cancellationToken);
            if (handled) return result;
        }

        navigationIntentService.Enqueue(
            "Dialog",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(request.Arguments),
            AgentAction.Create(request.ActionId, request.Arguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions());
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentDialog action '{request.ActionId}' until a matching Dialog component is registered.");
    }
}

internal sealed class NoOpFormActionExecutor(
    AgentComponentRegistryHub componentRegistryHub,
    IAgentNavigationIntentService navigationIntentService) : IFormActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        FormActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sessionId = RegisteredComponentActionExecutorBridge.TryGetSessionId(request.Arguments);
        if (sessionId is not null && componentRegistryHub.TryGet(sessionId, out var registry))
        {
            var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
                registry,
                expectedComponentType: "Form",
                componentId: AgentComponentCapabilityProfile.AgentFormComponentId,
                actionId: request.ActionId,
                arguments: request.Arguments,
                cancellationToken);
            if (handled) return result;
        }

        navigationIntentService.Enqueue(
            "Form",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(request.Arguments),
            AgentAction.Create(request.ActionId, request.Arguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions());
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentForm action '{request.ActionId}'. It will apply automatically when the form mounts (for example after opening the dialog).");
    }
}

internal sealed class NoOpNavigationActionExecutor(
    AgentComponentRegistryHub componentRegistryHub,
    IAgentNavigationIntentService navigationIntentService) : INavigationActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        NavigationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sessionId = RegisteredComponentActionExecutorBridge.TryGetSessionId(request.Arguments);
        if (sessionId is not null && componentRegistryHub.TryGet(sessionId, out var registry))
        {
            var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
                registry,
                expectedComponentType: "NavMenu",
                componentId: AgentComponentCapabilityProfile.AgentNavMenuComponentId,
                actionId: request.ActionId,
                arguments: request.Arguments,
                cancellationToken);
            if (handled) return result;
        }

        navigationIntentService.Enqueue(
            "NavMenu",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(request.Arguments),
            AgentAction.Create(request.ActionId, request.Arguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions());
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentNavMenu action '{request.ActionId}' until a matching NavMenu component is registered.");
    }
}

internal sealed class NoOpTabsActionExecutor(
    AgentComponentRegistryHub componentRegistryHub,
    IAgentNavigationIntentService navigationIntentService) : ITabsActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        TabsActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sessionId = RegisteredComponentActionExecutorBridge.TryGetSessionId(request.Arguments);
        if (sessionId is not null && componentRegistryHub.TryGet(sessionId, out var registry))
        {
            var (handled, result) = await RegisteredComponentActionExecutorBridge.TryExecuteAsync(
                registry,
                expectedComponentType: "Tabs",
                componentId: AgentComponentCapabilityProfile.AgentTabsComponentId,
                actionId: request.ActionId,
                arguments: request.Arguments,
                cancellationToken);
            if (handled) return result;
        }

        navigationIntentService.Enqueue(
            "Tabs",
            RegisteredComponentActionExecutorBridge.TryGetAgentId(request.Arguments),
            AgentAction.Create(request.ActionId, request.Arguments),
            RegisteredComponentActionExecutorBridge.BuildPendingOptions());
        return new ComponentActionExecutionResult(
            ComponentId: AgentComponentCapabilityProfile.AgentTabsComponentId,
            ActionId: request.ActionId,
            Outcome: ActionOutcome.Queued,
            Message: $"Queued AgentTabs action '{request.ActionId}' until a matching Tabs component is registered.");
    }
}
