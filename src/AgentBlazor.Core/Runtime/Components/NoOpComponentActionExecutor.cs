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
            (AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridFilterActionId or
                AgentComponentCapabilityProfile.DataGridClearFiltersActionId or
                AgentComponentCapabilityProfile.DataGridSortActionId or
                AgentComponentCapabilityProfile.DataGridSelectRowActionId or
                AgentComponentCapabilityProfile.DataGridGoToPageActionId or
                AgentComponentCapabilityProfile.DataGridNavigateToRowActionId or
                AgentComponentCapabilityProfile.DataGridSetPageActionId) =>
                dataGridExecutor.ExecuteAsync(new DataGridActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentDialogComponentId,
                AgentComponentCapabilityProfile.DialogOpenActionId or
                AgentComponentCapabilityProfile.DialogCloseActionId or
                AgentComponentCapabilityProfile.DialogConfirmActionId) =>
                dialogExecutor.ExecuteAsync(new DialogActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentFormComponentId,
                AgentComponentCapabilityProfile.FormSetFieldActionId or
                AgentComponentCapabilityProfile.FormValidateActionId or
                AgentComponentCapabilityProfile.FormResetActionId or
                AgentComponentCapabilityProfile.FormSubmitActionId) =>
                formExecutor.ExecuteAsync(new FormActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentNavMenuComponentId,
                AgentComponentCapabilityProfile.NavigationNavigateToActionId or
                AgentComponentCapabilityProfile.NavigationNavigateExternalActionId) =>
                navigationExecutor.ExecuteAsync(new NavigationActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            (AgentComponentCapabilityProfile.AgentTabsComponentId,
                AgentComponentCapabilityProfile.TabsSwitchTabActionId) =>
                tabsExecutor.ExecuteAsync(new TabsActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            ("AgentChatWidget", "open_widget" or "close_widget") =>
                chatWidgetExecutor.ExecuteAsync(new ChatWidgetActionRequest(action.ActionId, normalizedArguments), cancellationToken),

            _ => Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Outcome: ActionOutcome.Failed,
                Message:
                $"No executor mapping is registered for '{action.ComponentId}.{action.ActionId}'. " +
                $"Register a custom {nameof(IComponentActionExecutor)} or use supported AgentBlazor component capability actions."))
        };
    }
}
