using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Components;

internal sealed class NoOpComponentActionExecutor(
    IDataGridActionExecutor dataGridExecutor,
    IDialogActionExecutor dialogExecutor,
    IFormActionExecutor formExecutor,
    INavigationActionExecutor navigationExecutor,
    ITabsActionExecutor tabsExecutor,
    IChatWidgetActionExecutor chatWidgetExecutor,
    AgentComponentRegistryHub componentRegistryHub) : IComponentActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        PlannedComponentAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return (action.ComponentId, action.ActionId) switch
        {
            (AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridFilterActionId or
                AgentComponentCapabilityProfile.DataGridClearFiltersActionId or
                AgentComponentCapabilityProfile.DataGridSortActionId or
                AgentComponentCapabilityProfile.DataGridSelectRowActionId or
                AgentComponentCapabilityProfile.DataGridGoToPageActionId or
                AgentComponentCapabilityProfile.DataGridNavigateToRowActionId or
                AgentComponentCapabilityProfile.DataGridSetPageActionId) =>
                await dataGridExecutor.ExecuteAsync(new DataGridActionRequest(action.ActionId, action.Arguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentDialogComponentId,
                AgentComponentCapabilityProfile.DialogOpenActionId or
                AgentComponentCapabilityProfile.DialogCloseActionId or
                AgentComponentCapabilityProfile.DialogConfirmActionId) =>
                await dialogExecutor.ExecuteAsync(new DialogActionRequest(action.ActionId, action.Arguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentFormComponentId,
                AgentComponentCapabilityProfile.FormSetFieldActionId or
                AgentComponentCapabilityProfile.FormValidateActionId or
                AgentComponentCapabilityProfile.FormResetActionId or
                AgentComponentCapabilityProfile.FormSubmitActionId) =>
                await formExecutor.ExecuteAsync(new FormActionRequest(action.ActionId, action.Arguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentNavMenuComponentId,
                AgentComponentCapabilityProfile.NavigationNavigateToActionId or
                AgentComponentCapabilityProfile.NavigationNavigateExternalActionId) =>
                await navigationExecutor.ExecuteAsync(new NavigationActionRequest(action.ActionId, action.Arguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentTabsComponentId,
                AgentComponentCapabilityProfile.TabsSwitchTabActionId) =>
                await tabsExecutor.ExecuteAsync(new TabsActionRequest(action.ActionId, action.Arguments), cancellationToken),

            ("AgentChatWidget", "open_widget" or "close_widget") =>
                await chatWidgetExecutor.ExecuteAsync(new ChatWidgetActionRequest(action.ActionId, action.Arguments), cancellationToken),

            // Fallback: try to execute on any registered custom component
            _ => await TryExecuteOnRegisteredComponentAsync(action, cancellationToken)
        };
    }

    /// <summary>
    /// Fallback executor for custom component types (like workflow agents).
    /// Looks up the component by type or agentId from the registry and executes the action directly.
    /// </summary>
    private async Task<ComponentActionExecutionResult> TryExecuteOnRegisteredComponentAsync(
        PlannedComponentAction action,
        CancellationToken cancellationToken)
    {
        var sessionId = RegisteredComponentActionExecutorBridge.TryGetSessionId(action.Arguments);
        if (sessionId is null || !componentRegistryHub.TryGet(sessionId, out var registry))
        {
            return new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Outcome: ActionOutcome.Failed,
                Message: $"No component registry found for session. Cannot execute '{action.ComponentId}.{action.ActionId}'.");
        }

        // Try to find a component by agentId first (from arguments)
        var agentId = RegisteredComponentActionExecutorBridge.TryGetAgentId(action.Arguments);

        if (!string.IsNullOrWhiteSpace(agentId) && registry.TryGet(agentId, out var targetedComponent))
        {
            return await ExecuteOnComponentAsync(targetedComponent, action, cancellationToken);
        }

        // Try to find by ComponentId matching AgentId directly (most common case)
        if (registry.TryGet(action.ComponentId, out var componentByAgentId))
        {
            return await ExecuteOnComponentAsync(componentByAgentId, action, cancellationToken);
        }

        // Try to find a component by ComponentType matching ComponentId
        var matchingComponents = registry.GetAll()
            .Where(c => string.Equals(c.ComponentType, action.ComponentId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingComponents.Length == 1)
        {
            return await ExecuteOnComponentAsync(matchingComponents[0], action, cancellationToken);
        }

        if (matchingComponents.Length > 1)
        {
            var ids = string.Join(", ", matchingComponents.Select(c => c.AgentId));
            return new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Outcome: ActionOutcome.NeedsClarification,
                Message: $"Multiple '{action.ComponentId}' components found ({ids}). Specify 'agentId' to target one.");
        }

        return new ComponentActionExecutionResult(
            action.ComponentId,
            action.ActionId,
            Outcome: ActionOutcome.Failed,
            Message: $"No registered component found for '{action.ComponentId}.{action.ActionId}'.");
    }

    private static async Task<ComponentActionExecutionResult> ExecuteOnComponentAsync(
        IAgentControllable component,
        PlannedComponentAction action,
        CancellationToken cancellationToken)
    {
        var agentAction = AgentAction.Create(action.ActionId, action.Arguments);
        var result = await component.ExecuteActionAsync(agentAction, cancellationToken);

        return new ComponentActionExecutionResult(
            action.ComponentId,
            action.ActionId,
            Outcome: result.Outcome,
            Message: result.Message);
    }
}
