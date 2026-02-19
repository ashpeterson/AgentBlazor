namespace AgentBlazor.Components;

public static class AgentCapabilityPresets
{
    public static void Apply(
        ComponentCapabilityCatalogBuilder builder,
        AgentCapabilityPreset preset)
    {
        ArgumentNullException.ThrowIfNull(builder);

        switch (preset)
        {
            case AgentCapabilityPreset.V1Minimal:
                ApplyV1Minimal(builder);
                break;
            case AgentCapabilityPreset.V1Full:
                AgentComponentV1CapabilityProfile.Apply(builder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported AgentBlazor capability preset.");
        }
    }

    private static void ApplyV1Minimal(ComponentCapabilityCatalogBuilder builder)
    {
        builder.AddComponent(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            "AgentDataGrid interactions for filtering, sorting, row focus, and pagination.",
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridFilterActionId,
                "Apply a filter to an AgentDataGrid column.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridFilterInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridSortActionId,
                "Apply sorting to an AgentDataGrid column.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridSortInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridClearFiltersActionId,
                "Clear AgentDataGrid filters.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridClearFiltersInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId,
                "Focus or navigate to a specific AgentDataGrid row.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridNavigateToRowInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridSelectRowActionId,
                "Select a specific AgentDataGrid row.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridNavigateToRowInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridGoToPageActionId,
                "Navigate to a specific AgentDataGrid page.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridSetPageInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DataGridSetPageActionId,
                "Set AgentDataGrid paging state.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DataGridSetPageInputSchema));

        builder.AddComponent(
            AgentComponentV1CapabilityProfile.AgentDialogComponentId,
            "AgentDialog interactions for opening and closing dialogs.",
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DialogOpenActionId,
                "Open an AgentDialog with optional parameters.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DialogOpenInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.DialogCloseActionId,
                "Close an AgentDialog with an optional reason.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.DialogCloseInputSchema));

        builder.AddComponent(
            AgentComponentV1CapabilityProfile.AgentFormComponentId,
            "AgentForm interactions for field mutation, validation, and submission.",
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.FormSetFieldActionId,
                "Set a field value on an AgentForm.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.FormSetFieldInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.FormValidateActionId,
                "Trigger validation on an AgentForm.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.FormValidateInputSchema),
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.FormResetActionId,
                "Reset AgentForm fields to initial values.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.FormResetInputSchema));

        builder.AddComponent(
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            "AgentNavMenu interactions for internal and external route changes.",
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
                "Navigate to an internal route.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.NavigationNavigateToInputSchema));

        builder.AddComponent(
            AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            "AgentTabs interactions for switching active tabs.",
            new ComponentActionCapability(
                AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
                "Switch active tab by index.",
                RequiresApproval: false,
                InputSchema: AgentComponentV1CapabilityProfile.TabsSwitchTabInputSchema));
    }
}
