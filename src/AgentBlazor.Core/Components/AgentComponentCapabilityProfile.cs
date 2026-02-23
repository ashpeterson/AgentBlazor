namespace AgentBlazor.Components;

/// <summary>
/// Canonical component and action identifiers and JSON schemas for agent-controllable components.
/// Single source of truth for the shipped component capability profile.
/// </summary>
public static class AgentComponentCapabilityProfile
{
    /// <summary>Identifier for this capability profile (e.g. for diagnostics).</summary>
    public const string ProfileId = "agentblazor.components";

    public const string AgentDataGridComponentId = "AgentDataGrid";
    public const string AgentDialogComponentId = "AgentDialog";
    public const string AgentFormComponentId = "AgentForm";
    public const string AgentNavMenuComponentId = "AgentNavMenu";
    public const string AgentTabsComponentId = "AgentTabs";

    public const string DataGridFilterActionId = "filter";
    public const string DataGridClearFiltersActionId = "clear_filters";
    public const string DataGridSortActionId = "sort";
    public const string DataGridNavigateToRowActionId = "navigate_to_row";
    public const string DataGridSelectRowActionId = "select_row";
    public const string DataGridGoToPageActionId = "go_to_page";
    public const string DataGridSetPageActionId = "set_page";

    public const string DialogOpenActionId = "open";
    public const string DialogCloseActionId = "close";
    public const string DialogConfirmActionId = "confirm";

    public const string FormSetFieldActionId = "set_field";
    public const string FormValidateActionId = "validate";
    public const string FormResetActionId = "reset";
    public const string FormSubmitActionId = "submit";

    public const string NavigationNavigateToActionId = "navigate_to";
    public const string NavigationNavigateExternalActionId = "navigate_external";

    public const string TabsSwitchTabActionId = "switch_tab";

    public static IReadOnlyList<string> ComponentIds { get; } =
    [
        AgentDataGridComponentId,
        AgentDialogComponentId,
        AgentFormComponentId,
        AgentNavMenuComponentId,
        AgentTabsComponentId
    ];

    public static void Apply(ComponentCapabilityCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddComponent(
            AgentDataGridComponentId,
            "AgentDataGrid interactions for filtering, sorting, row focus, and pagination.",
            new ComponentActionCapability(
                DataGridFilterActionId,
                "Apply a filter to an AgentDataGrid column.",
                RequiresApproval: false,
                InputSchema: DataGridFilterInputSchema),
            new ComponentActionCapability(
                DataGridClearFiltersActionId,
                "Clear AgentDataGrid filters.",
                RequiresApproval: false,
                InputSchema: DataGridClearFiltersInputSchema),
            new ComponentActionCapability(
                DataGridSortActionId,
                "Apply sorting to an AgentDataGrid column.",
                RequiresApproval: false,
                InputSchema: DataGridSortInputSchema),
            new ComponentActionCapability(
                DataGridNavigateToRowActionId,
                "Focus or navigate to a specific AgentDataGrid row.",
                RequiresApproval: false,
                InputSchema: DataGridNavigateToRowInputSchema),
            new ComponentActionCapability(
                DataGridSelectRowActionId,
                "Select a specific AgentDataGrid row.",
                RequiresApproval: false,
                InputSchema: DataGridNavigateToRowInputSchema),
            new ComponentActionCapability(
                DataGridGoToPageActionId,
                "Navigate to a specific AgentDataGrid page.",
                RequiresApproval: false,
                InputSchema: DataGridSetPageInputSchema),
            new ComponentActionCapability(
                DataGridSetPageActionId,
                "Set AgentDataGrid paging state.",
                RequiresApproval: false,
                InputSchema: DataGridSetPageInputSchema));

        builder.AddComponent(
            AgentDialogComponentId,
            "AgentDialog interactions for opening and closing dialogs.",
            new ComponentActionCapability(
                DialogOpenActionId,
                "Open an AgentDialog with optional parameters.",
                RequiresApproval: false,
                InputSchema: DialogOpenInputSchema),
            new ComponentActionCapability(
                DialogCloseActionId,
                "Close an AgentDialog with an optional reason.",
                RequiresApproval: false,
                InputSchema: DialogCloseInputSchema),
            new ComponentActionCapability(
                DialogConfirmActionId,
                "Confirm the dialog action.",
                RequiresApproval: true,
                InputSchema: DialogConfirmInputSchema));

        builder.AddComponent(
            AgentFormComponentId,
            "AgentForm interactions for field mutation, validation, and submission.",
            new ComponentActionCapability(
                FormSetFieldActionId,
                "Set a field value on an AgentForm.",
                RequiresApproval: false,
                InputSchema: FormSetFieldInputSchema),
            new ComponentActionCapability(
                FormValidateActionId,
                "Trigger validation on an AgentForm.",
                RequiresApproval: false,
                InputSchema: FormValidateInputSchema),
            new ComponentActionCapability(
                FormResetActionId,
                "Reset AgentForm fields to initial values.",
                RequiresApproval: false,
                InputSchema: FormResetInputSchema),
            new ComponentActionCapability(
                FormSubmitActionId,
                "Submit an AgentForm after validation.",
                RequiresApproval: true,
                InputSchema: FormSubmitInputSchema));

        builder.AddComponent(
            AgentNavMenuComponentId,
            "AgentNavMenu interactions for internal and external route changes.",
            new ComponentActionCapability(
                NavigationNavigateToActionId,
                "Navigate to an internal route.",
                RequiresApproval: false,
                InputSchema: NavigationNavigateToInputSchema),
            new ComponentActionCapability(
                NavigationNavigateExternalActionId,
                "Navigate to an external URI.",
                RequiresApproval: true,
                InputSchema: NavigationNavigateExternalInputSchema));

        builder.AddComponent(
            AgentTabsComponentId,
            "AgentTabs interactions for switching active tabs.",
            new ComponentActionCapability(
                TabsSwitchTabActionId,
                "Switch active tab by index.",
                RequiresApproval: false,
                InputSchema: TabsSwitchTabInputSchema));
    }

    /// <summary>
    /// Applies a minimal subset: same components but only actions that do not require approval
    /// (excludes DialogConfirm, FormSubmit, NavigationNavigateExternal).
    /// </summary>
    public static void ApplyMinimal(ComponentCapabilityCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddComponent(
            AgentDataGridComponentId,
            "AgentDataGrid interactions for filtering, sorting, row focus, and pagination.",
            new ComponentActionCapability(DataGridFilterActionId, "Apply a filter to an AgentDataGrid column.", RequiresApproval: false, InputSchema: DataGridFilterInputSchema),
            new ComponentActionCapability(DataGridSortActionId, "Apply sorting to an AgentDataGrid column.", RequiresApproval: false, InputSchema: DataGridSortInputSchema),
            new ComponentActionCapability(DataGridClearFiltersActionId, "Clear AgentDataGrid filters.", RequiresApproval: false, InputSchema: DataGridClearFiltersInputSchema),
            new ComponentActionCapability(DataGridNavigateToRowActionId, "Focus or navigate to a specific AgentDataGrid row.", RequiresApproval: false, InputSchema: DataGridNavigateToRowInputSchema),
            new ComponentActionCapability(DataGridSelectRowActionId, "Select a specific AgentDataGrid row.", RequiresApproval: false, InputSchema: DataGridNavigateToRowInputSchema),
            new ComponentActionCapability(DataGridGoToPageActionId, "Navigate to an AgentDataGrid page.", RequiresApproval: false, InputSchema: DataGridSetPageInputSchema),
            new ComponentActionCapability(DataGridSetPageActionId, "Set AgentDataGrid paging state.", RequiresApproval: false, InputSchema: DataGridSetPageInputSchema));

        builder.AddComponent(
            AgentDialogComponentId,
            "AgentDialog interactions for opening and closing dialogs.",
            new ComponentActionCapability(DialogOpenActionId, "Open an AgentDialog with optional parameters.", RequiresApproval: false, InputSchema: DialogOpenInputSchema),
            new ComponentActionCapability(DialogCloseActionId, "Close an AgentDialog with an optional reason.", RequiresApproval: false, InputSchema: DialogCloseInputSchema));

        builder.AddComponent(
            AgentFormComponentId,
            "AgentForm interactions for field mutation and validation.",
            new ComponentActionCapability(FormSetFieldActionId, "Set a field value on an AgentForm.", RequiresApproval: false, InputSchema: FormSetFieldInputSchema),
            new ComponentActionCapability(FormValidateActionId, "Trigger validation on an AgentForm.", RequiresApproval: false, InputSchema: FormValidateInputSchema),
            new ComponentActionCapability(FormResetActionId, "Reset AgentForm fields to initial values.", RequiresApproval: false, InputSchema: FormResetInputSchema));

        builder.AddComponent(
            AgentNavMenuComponentId,
            "AgentNavMenu interactions for internal route changes.",
            new ComponentActionCapability(NavigationNavigateToActionId, "Navigate to an internal route.", RequiresApproval: false, InputSchema: NavigationNavigateToInputSchema));

        builder.AddComponent(
            AgentTabsComponentId,
            "AgentTabs interactions for switching active tabs.",
            new ComponentActionCapability(TabsSwitchTabActionId, "Switch active tab by index.", RequiresApproval: false, InputSchema: TabsSwitchTabInputSchema));
    }

    public const string DataGridFilterInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "column": { "type": "string", "description": "Column/property name." },
            "operator": {
              "type": "string",
              "enum": ["eq", "neq", "gt", "gte", "lt", "lte", "contains", "startswith", "endswith", "in", "notin", "isnull", "notnull"],
              "description": "Filter operator. For threshold phrases (e.g. 'high risk', 'low risk') use gte or lte so the app can map semantic values; use eq only for exact match. Use the sort action for asc/desc ordering."
            },
            "value": {
              "description": "Filter value; use semantic terms (e.g. high, low) when the app maps them to numbers.",
              "type": ["string", "number", "boolean", "null"]
            }
          },
          "required": ["column", "operator"]
        }
        """;

    public const string DataGridClearFiltersInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "column": { "type": "string", "description": "Optional column name to clear; omitted clears all filters." }
          }
        }
        """;

    public const string DataGridSortInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "column": { "type": "string", "description": "Column/property name." },
            "direction": {
              "type": "string",
              "enum": ["asc", "desc"],
              "description": "Sort direction."
            }
          },
          "required": ["column", "direction"]
        }
        """;

    public const string DataGridNavigateToRowInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "rowKey": {
              "description": "Row key or identifier.",
              "type": ["string", "number"]
            }
          },
          "required": ["rowKey"]
        }
        """;

    public const string DataGridSetPageInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "pageIndex": { "type": "integer", "minimum": 0 },
            "pageSize": { "type": "integer", "minimum": 1 }
          },
          "required": ["pageIndex"]
        }
        """;

    public const string DialogOpenInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "dialogId": { "type": "string" },
            "title": { "type": "string" },
            "parameters": { "type": "object" }
          }
        }
        """;

    public const string DialogCloseInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "dialogId": { "type": "string" },
            "reason": { "type": "string" }
          }
        }
        """;

    public const string DialogConfirmInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "dialogId": { "type": "string" },
            "confirmation": { "type": "string" }
          }
        }
        """;

    public const string FormSetFieldInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "field": { "type": "string" },
            "value": {}
          },
          "required": ["field"]
        }
        """;

    public const string FormValidateInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "fields": {
              "type": "array",
              "items": { "type": "string" }
            }
          }
        }
        """;

    public const string FormSubmitInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "formId": { "type": "string" },
            "confirmationMessage": { "type": "string" }
          }
        }
        """;

    public const string FormResetInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string NavigationNavigateToInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "uri": { "type": "string" },
            "replaceHistory": { "type": "boolean" }
          },
          "required": ["uri"]
        }
        """;

    public const string NavigationNavigateExternalInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "uri": { "type": "string" },
            "openInNewTab": { "type": "boolean" }
          },
          "required": ["uri"]
        }
        """;

    public const string TabsSwitchTabInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "index": { "type": "integer", "minimum": 0 }
          },
          "required": ["index"]
        }
        """;
}
