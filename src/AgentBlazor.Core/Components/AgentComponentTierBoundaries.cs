using AgentBlazor.Licensing;

namespace AgentBlazor.Components;

public static class AgentComponentTierBoundaries
{
    public const string DataGridBasicFeature = "agentblazor.components.datagrid.basic";
    public const string DataGridAdvancedFeature = "agentblazor.components.datagrid.advanced";
    public const string DialogFlowFeature = "agentblazor.components.dialog.flow";
    public const string FormAssistFeature = "agentblazor.components.form.assist";
    public const string FormSubmissionFeature = "agentblazor.components.form.submission";
    public const string NavigationInternalFeature = "agentblazor.components.navigation.internal";
    public const string NavigationExternalFeature = "agentblazor.components.navigation.external";
    public const string TabsFeature = "agentblazor.components.tabs.navigation";

    private static readonly IReadOnlyDictionary<string, (string FeatureKey, AgentBlazorTier RequiredTier)> ActionTiers =
        new Dictionary<string, (string FeatureKey, AgentBlazorTier RequiredTier)>(StringComparer.OrdinalIgnoreCase)
        {
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridFilterActionId)] =
                (DataGridBasicFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridSortActionId)] =
                (DataGridBasicFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridClearFiltersActionId)] =
                (DataGridBasicFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridSelectRowActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridGoToPageActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, AgentComponentV1CapabilityProfile.DataGridSetPageActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDialogComponentId, AgentComponentV1CapabilityProfile.DialogOpenActionId)] =
                (DialogFlowFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDialogComponentId, AgentComponentV1CapabilityProfile.DialogCloseActionId)] =
                (DialogFlowFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentDialogComponentId, AgentComponentV1CapabilityProfile.DialogConfirmActionId)] =
                (DialogFlowFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentFormComponentId, AgentComponentV1CapabilityProfile.FormSetFieldActionId)] =
                (FormAssistFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentFormComponentId, AgentComponentV1CapabilityProfile.FormValidateActionId)] =
                (FormAssistFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentFormComponentId, AgentComponentV1CapabilityProfile.FormResetActionId)] =
                (FormAssistFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentFormComponentId, AgentComponentV1CapabilityProfile.FormSubmitActionId)] =
                (FormSubmissionFeature, AgentBlazorTier.Premium),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, AgentComponentV1CapabilityProfile.NavigationNavigateToActionId)] =
                (NavigationInternalFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId)] =
                (NavigationExternalFeature, AgentBlazorTier.Premium),
            [ComponentActionPolicy.ToActionKey(AgentComponentV1CapabilityProfile.AgentTabsComponentId, AgentComponentV1CapabilityProfile.TabsSwitchTabActionId)] =
                (TabsFeature, AgentBlazorTier.Free)
        };

    public static IReadOnlyDictionary<string, AgentBlazorTier> GetActionTiers() =>
        ActionTiers.ToDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value.RequiredTier,
            StringComparer.OrdinalIgnoreCase);

    public static AgentBlazorTier GetRequiredTier(string componentId, string actionId)
    {
        var key = ComponentActionPolicy.ToActionKey(componentId, actionId);
        return ActionTiers.TryGetValue(key, out var entry)
            ? entry.RequiredTier
            : AgentBlazorTier.Free;
    }

    public static string GetFeatureKey(string componentId, string actionId)
    {
        var key = ComponentActionPolicy.ToActionKey(componentId, actionId);
        return ActionTiers.TryGetValue(key, out var entry)
            ? entry.FeatureKey
            : "agentblazor.core";
    }
}
