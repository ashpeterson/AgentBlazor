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
    public const string SelectFeature = "agentblazor.components.select.basic";
    public const string AutocompleteFeature = "agentblazor.components.autocomplete.basic";
    public const string DatePickerFeature = "agentblazor.components.datepicker.basic";
    public const string DateRangePickerFeature = "agentblazor.components.daterangepicker.basic";
    public const string TreeViewFeature = "agentblazor.components.treeview.basic";
    public const string StepperFeature = "agentblazor.components.stepper.basic";
    public const string CommandBarFeature = "agentblazor.components.commandbar.basic";
    public const string FileUploadFeature = "agentblazor.components.fileupload.basic";

    private static readonly IReadOnlyDictionary<string, (string FeatureKey, AgentBlazorTier RequiredTier)> ActionTiers =
        new Dictionary<string, (string FeatureKey, AgentBlazorTier RequiredTier)>(StringComparer.OrdinalIgnoreCase)
        {
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridFilterActionId)] =
                (DataGridBasicFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridSortActionId)] =
                (DataGridBasicFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridClearFiltersActionId)] =
                (DataGridBasicFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridNavigateToRowActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridSelectRowActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridGoToPageActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDataGridComponentId, AgentComponentCapabilityProfile.DataGridSetPageActionId)] =
                (DataGridAdvancedFeature, AgentBlazorTier.Paid),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDialogComponentId, AgentComponentCapabilityProfile.DialogOpenActionId)] =
                (DialogFlowFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDialogComponentId, AgentComponentCapabilityProfile.DialogCloseActionId)] =
                (DialogFlowFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDialogComponentId, AgentComponentCapabilityProfile.DialogConfirmActionId)] =
                (DialogFlowFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFormComponentId, AgentComponentCapabilityProfile.FormSetFieldActionId)] =
                (FormAssistFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFormComponentId, AgentComponentCapabilityProfile.FormValidateActionId)] =
                (FormAssistFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFormComponentId, AgentComponentCapabilityProfile.FormResetActionId)] =
                (FormAssistFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFormComponentId, AgentComponentCapabilityProfile.FormSubmitActionId)] =
                (FormSubmissionFeature, AgentBlazorTier.Premium),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentNavMenuComponentId, AgentComponentCapabilityProfile.NavigationNavigateToActionId)] =
                (NavigationInternalFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentNavMenuComponentId, AgentComponentCapabilityProfile.NavigationNavigateExternalActionId)] =
                (NavigationExternalFeature, AgentBlazorTier.Premium),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentTabsComponentId, AgentComponentCapabilityProfile.TabsSwitchTabActionId)] =
                (TabsFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentSelectComponentId, AgentComponentCapabilityProfile.SelectOpenActionId)] =
                (SelectFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentSelectComponentId, AgentComponentCapabilityProfile.SelectCloseActionId)] =
                (SelectFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentSelectComponentId, AgentComponentCapabilityProfile.SelectSetValueActionId)] =
                (SelectFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentSelectComponentId, AgentComponentCapabilityProfile.SelectClearActionId)] =
                (SelectFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentAutocompleteComponentId, AgentComponentCapabilityProfile.AutocompleteSetQueryActionId)] =
                (AutocompleteFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentAutocompleteComponentId, AgentComponentCapabilityProfile.AutocompleteSelectOptionActionId)] =
                (AutocompleteFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentAutocompleteComponentId, AgentComponentCapabilityProfile.AutocompleteClearActionId)] =
                (AutocompleteFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDatePickerComponentId, AgentComponentCapabilityProfile.DatePickerSetDateActionId)] =
                (DatePickerFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDatePickerComponentId, AgentComponentCapabilityProfile.DatePickerClearActionId)] =
                (DatePickerFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDateRangePickerComponentId, AgentComponentCapabilityProfile.DateRangePickerSetRangeActionId)] =
                (DateRangePickerFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentDateRangePickerComponentId, AgentComponentCapabilityProfile.DateRangePickerClearActionId)] =
                (DateRangePickerFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentTreeViewComponentId, AgentComponentCapabilityProfile.TreeViewExpandActionId)] =
                (TreeViewFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentTreeViewComponentId, AgentComponentCapabilityProfile.TreeViewCollapseActionId)] =
                (TreeViewFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentTreeViewComponentId, AgentComponentCapabilityProfile.TreeViewSelectNodeActionId)] =
                (TreeViewFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentStepperComponentId, AgentComponentCapabilityProfile.StepperGoToStepActionId)] =
                (StepperFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentStepperComponentId, AgentComponentCapabilityProfile.StepperNextActionId)] =
                (StepperFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentStepperComponentId, AgentComponentCapabilityProfile.StepperPreviousActionId)] =
                (StepperFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentCommandBarComponentId, AgentComponentCapabilityProfile.CommandBarInvokeCommandActionId)] =
                (CommandBarFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentCommandBarComponentId, AgentComponentCapabilityProfile.CommandBarListCommandsActionId)] =
                (CommandBarFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFileUploadComponentId, AgentComponentCapabilityProfile.FileUploadAttachActionId)] =
                (FileUploadFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFileUploadComponentId, AgentComponentCapabilityProfile.FileUploadRemoveActionId)] =
                (FileUploadFeature, AgentBlazorTier.Free),
            [ComponentActionPolicy.ToActionKey(AgentComponentCapabilityProfile.AgentFileUploadComponentId, AgentComponentCapabilityProfile.FileUploadListFilesActionId)] =
                (FileUploadFeature, AgentBlazorTier.Free)
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
