using AgentBlazor.Components;

namespace AgentBlazor.Runtime;

internal sealed class NoOpComponentActionExecutor(
    IDataGridActionExecutor dataGridExecutor,
    IDialogActionExecutor dialogExecutor,
    IFormActionExecutor formExecutor,
    INavigationActionExecutor navigationExecutor,
    ITabsActionExecutor tabsExecutor,
    IChatWidgetActionExecutor chatWidgetExecutor) : IComponentActionExecutor
{
    public Task<ComponentActionExecutionResult> ExecuteAsync(
        PlannedComponentAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var normalizedArguments = ComponentActionArgumentNormalizer.Normalize(
            action.ComponentId,
            action.ActionId,
            action.Arguments,
            action.Reason);

        return (action.ComponentId, action.ActionId) switch
        {
            (AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridFilterActionId or
                AgentComponentV1CapabilityProfile.DataGridClearFiltersActionId or
                AgentComponentV1CapabilityProfile.DataGridSortActionId or
                AgentComponentV1CapabilityProfile.DataGridSelectRowActionId or
                AgentComponentV1CapabilityProfile.DataGridGoToPageActionId or
                AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId or
                AgentComponentV1CapabilityProfile.DataGridSetPageActionId) =>
                dataGridExecutor.ExecuteAsync(new DataGridActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentV1CapabilityProfile.AgentDialogComponentId,
                AgentComponentV1CapabilityProfile.DialogOpenActionId or
                AgentComponentV1CapabilityProfile.DialogCloseActionId or
                AgentComponentV1CapabilityProfile.DialogConfirmActionId) =>
                dialogExecutor.ExecuteAsync(new DialogActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentV1CapabilityProfile.AgentFormComponentId,
                AgentComponentV1CapabilityProfile.FormSetFieldActionId or
                AgentComponentV1CapabilityProfile.FormValidateActionId or
                AgentComponentV1CapabilityProfile.FormResetActionId or
                AgentComponentV1CapabilityProfile.FormSubmitActionId) =>
                formExecutor.ExecuteAsync(new FormActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
                AgentComponentV1CapabilityProfile.NavigationNavigateToActionId or
                AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId) =>
                navigationExecutor.ExecuteAsync(new NavigationActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentV1CapabilityProfile.AgentTabsComponentId,
                AgentComponentV1CapabilityProfile.TabsSwitchTabActionId) =>
                tabsExecutor.ExecuteAsync(new TabsActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            ("AgentChatWidget", "open_widget" or "close_widget") =>
                chatWidgetExecutor.ExecuteAsync(new ChatWidgetActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            _ => Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: false,
                Message:
                $"No executor mapping is registered for '{action.ComponentId}.{action.ActionId}'. " +
                $"Register a custom {nameof(IComponentActionExecutor)} or use supported AgentBlazor component capability actions."))
        };
    }
}
