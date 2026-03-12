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
    public const string AgentSelectComponentId = "AgentSelect";
    public const string AgentAutocompleteComponentId = "AgentAutocomplete";
    public const string AgentDatePickerComponentId = "AgentDatePicker";
    public const string AgentDateRangePickerComponentId = "AgentDateRangePicker";
    public const string AgentTreeViewComponentId = "AgentTreeView";
    public const string AgentStepperComponentId = "AgentStepper";
    public const string AgentCommandBarComponentId = "AgentCommandBar";
    public const string AgentFileUploadComponentId = "AgentFileUpload";

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

    public const string SelectOpenActionId = "open";
    public const string SelectCloseActionId = "close";
    public const string SelectSetValueActionId = "set_value";
    public const string SelectClearActionId = "clear";

    public const string AutocompleteSetQueryActionId = "set_query";
    public const string AutocompleteSelectOptionActionId = "select_option";
    public const string AutocompleteClearActionId = "clear";

    public const string DatePickerSetDateActionId = "set_date";
    public const string DatePickerClearActionId = "clear";

    public const string DateRangePickerSetRangeActionId = "set_range";
    public const string DateRangePickerClearActionId = "clear";

    public const string TreeViewExpandActionId = "expand";
    public const string TreeViewCollapseActionId = "collapse";
    public const string TreeViewSelectNodeActionId = "select_node";

    public const string StepperGoToStepActionId = "go_to_step";
    public const string StepperNextActionId = "next";
    public const string StepperPreviousActionId = "previous";

    public const string CommandBarInvokeCommandActionId = "invoke_command";
    public const string CommandBarListCommandsActionId = "list_commands";

    public const string FileUploadAttachActionId = "attach";
    public const string FileUploadRemoveActionId = "remove";
    public const string FileUploadListFilesActionId = "list_files";

    public static IReadOnlyList<string> ComponentIds { get; } =
    [
        AgentDataGridComponentId,
        AgentDialogComponentId,
        AgentFormComponentId,
        AgentNavMenuComponentId,
        AgentTabsComponentId,
        AgentSelectComponentId,
        AgentAutocompleteComponentId,
        AgentDatePickerComponentId,
        AgentDateRangePickerComponentId,
        AgentTreeViewComponentId,
        AgentStepperComponentId,
        AgentCommandBarComponentId,
        AgentFileUploadComponentId
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

        builder.AddComponent(
            AgentSelectComponentId,
            "AgentSelect interactions for opening, closing, choosing, and clearing single values.",
            new ComponentActionCapability(
                SelectOpenActionId,
                "Open the select list.",
                RequiresApproval: false,
                InputSchema: SelectOpenInputSchema),
            new ComponentActionCapability(
                SelectCloseActionId,
                "Close the select list.",
                RequiresApproval: false,
                InputSchema: SelectCloseInputSchema),
            new ComponentActionCapability(
                SelectSetValueActionId,
                "Select one option value.",
                RequiresApproval: false,
                InputSchema: SelectSetValueInputSchema),
            new ComponentActionCapability(
                SelectClearActionId,
                "Clear the selected option.",
                RequiresApproval: false,
                InputSchema: SelectClearInputSchema));

        builder.AddComponent(
            AgentAutocompleteComponentId,
            "AgentAutocomplete interactions for query text, option selection, and clear.",
            new ComponentActionCapability(
                AutocompleteSetQueryActionId,
                "Set query text for autocomplete.",
                RequiresApproval: false,
                InputSchema: AutocompleteSetQueryInputSchema),
            new ComponentActionCapability(
                AutocompleteSelectOptionActionId,
                "Select a single suggested option value.",
                RequiresApproval: false,
                InputSchema: AutocompleteSelectOptionInputSchema),
            new ComponentActionCapability(
                AutocompleteClearActionId,
                "Clear autocomplete query and selected value.",
                RequiresApproval: false,
                InputSchema: AutocompleteClearInputSchema));

        builder.AddComponent(
            AgentDatePickerComponentId,
            "AgentDatePicker interactions for selecting and clearing one date.",
            new ComponentActionCapability(
                DatePickerSetDateActionId,
                "Set the selected date.",
                RequiresApproval: false,
                InputSchema: DatePickerSetDateInputSchema),
            new ComponentActionCapability(
                DatePickerClearActionId,
                "Clear the selected date.",
                RequiresApproval: false,
                InputSchema: DatePickerClearInputSchema));

        builder.AddComponent(
            AgentDateRangePickerComponentId,
            "AgentDateRangePicker interactions for setting and clearing a start/end range.",
            new ComponentActionCapability(
                DateRangePickerSetRangeActionId,
                "Set start and end dates for the range.",
                RequiresApproval: false,
                InputSchema: DateRangePickerSetRangeInputSchema),
            new ComponentActionCapability(
                DateRangePickerClearActionId,
                "Clear selected date range.",
                RequiresApproval: false,
                InputSchema: DateRangePickerClearInputSchema));

        builder.AddComponent(
            AgentTreeViewComponentId,
            "AgentTreeView interactions for expanding, collapsing, and selecting nodes.",
            new ComponentActionCapability(
                TreeViewExpandActionId,
                "Expand one tree node.",
                RequiresApproval: false,
                InputSchema: TreeViewExpandInputSchema),
            new ComponentActionCapability(
                TreeViewCollapseActionId,
                "Collapse one tree node.",
                RequiresApproval: false,
                InputSchema: TreeViewCollapseInputSchema),
            new ComponentActionCapability(
                TreeViewSelectNodeActionId,
                "Select one tree node.",
                RequiresApproval: false,
                InputSchema: TreeViewSelectNodeInputSchema));

        builder.AddComponent(
            AgentStepperComponentId,
            "AgentStepper interactions for workflow step navigation.",
            new ComponentActionCapability(
                StepperGoToStepActionId,
                "Go to an exact step index.",
                RequiresApproval: false,
                InputSchema: StepperGoToStepInputSchema),
            new ComponentActionCapability(
                StepperNextActionId,
                "Move to the next step.",
                RequiresApproval: false,
                InputSchema: StepperNextInputSchema),
            new ComponentActionCapability(
                StepperPreviousActionId,
                "Move to the previous step.",
                RequiresApproval: false,
                InputSchema: StepperPreviousInputSchema));

        builder.AddComponent(
            AgentCommandBarComponentId,
            "AgentCommandBar interactions for command discovery and invocation.",
            new ComponentActionCapability(
                CommandBarInvokeCommandActionId,
                "Invoke one command by id or name.",
                RequiresApproval: false,
                InputSchema: CommandBarInvokeCommandInputSchema),
            new ComponentActionCapability(
                CommandBarListCommandsActionId,
                "List available commands.",
                RequiresApproval: false,
                InputSchema: CommandBarListCommandsInputSchema));

        builder.AddComponent(
            AgentFileUploadComponentId,
            "AgentFileUpload interactions for attaching, removing, and listing file names.",
            new ComponentActionCapability(
                FileUploadAttachActionId,
                "Attach one file name to the current upload list.",
                RequiresApproval: false,
                InputSchema: FileUploadAttachInputSchema),
            new ComponentActionCapability(
                FileUploadRemoveActionId,
                "Remove one file name from the current upload list.",
                RequiresApproval: false,
                InputSchema: FileUploadRemoveInputSchema),
            new ComponentActionCapability(
                FileUploadListFilesActionId,
                "List currently attached files.",
                RequiresApproval: false,
                InputSchema: FileUploadListFilesInputSchema));
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

        builder.AddComponent(
            AgentSelectComponentId,
            "AgentSelect interactions for opening, closing, choosing, and clearing single values.",
            new ComponentActionCapability(SelectOpenActionId, "Open the select list.", RequiresApproval: false, InputSchema: SelectOpenInputSchema),
            new ComponentActionCapability(SelectCloseActionId, "Close the select list.", RequiresApproval: false, InputSchema: SelectCloseInputSchema),
            new ComponentActionCapability(SelectSetValueActionId, "Select one option value.", RequiresApproval: false, InputSchema: SelectSetValueInputSchema),
            new ComponentActionCapability(SelectClearActionId, "Clear the selected option.", RequiresApproval: false, InputSchema: SelectClearInputSchema));

        builder.AddComponent(
            AgentAutocompleteComponentId,
            "AgentAutocomplete interactions for query text, option selection, and clear.",
            new ComponentActionCapability(AutocompleteSetQueryActionId, "Set query text for autocomplete.", RequiresApproval: false, InputSchema: AutocompleteSetQueryInputSchema),
            new ComponentActionCapability(AutocompleteSelectOptionActionId, "Select a single suggested option value.", RequiresApproval: false, InputSchema: AutocompleteSelectOptionInputSchema),
            new ComponentActionCapability(AutocompleteClearActionId, "Clear autocomplete query and selected value.", RequiresApproval: false, InputSchema: AutocompleteClearInputSchema));

        builder.AddComponent(
            AgentDatePickerComponentId,
            "AgentDatePicker interactions for selecting and clearing one date.",
            new ComponentActionCapability(DatePickerSetDateActionId, "Set the selected date.", RequiresApproval: false, InputSchema: DatePickerSetDateInputSchema),
            new ComponentActionCapability(DatePickerClearActionId, "Clear the selected date.", RequiresApproval: false, InputSchema: DatePickerClearInputSchema));

        builder.AddComponent(
            AgentDateRangePickerComponentId,
            "AgentDateRangePicker interactions for setting and clearing a start/end range.",
            new ComponentActionCapability(DateRangePickerSetRangeActionId, "Set start and end dates for the range.", RequiresApproval: false, InputSchema: DateRangePickerSetRangeInputSchema),
            new ComponentActionCapability(DateRangePickerClearActionId, "Clear selected date range.", RequiresApproval: false, InputSchema: DateRangePickerClearInputSchema));

        builder.AddComponent(
            AgentTreeViewComponentId,
            "AgentTreeView interactions for expanding, collapsing, and selecting nodes.",
            new ComponentActionCapability(TreeViewExpandActionId, "Expand one tree node.", RequiresApproval: false, InputSchema: TreeViewExpandInputSchema),
            new ComponentActionCapability(TreeViewCollapseActionId, "Collapse one tree node.", RequiresApproval: false, InputSchema: TreeViewCollapseInputSchema),
            new ComponentActionCapability(TreeViewSelectNodeActionId, "Select one tree node.", RequiresApproval: false, InputSchema: TreeViewSelectNodeInputSchema));

        builder.AddComponent(
            AgentStepperComponentId,
            "AgentStepper interactions for workflow step navigation.",
            new ComponentActionCapability(StepperGoToStepActionId, "Go to an exact step index.", RequiresApproval: false, InputSchema: StepperGoToStepInputSchema),
            new ComponentActionCapability(StepperNextActionId, "Move to the next step.", RequiresApproval: false, InputSchema: StepperNextInputSchema),
            new ComponentActionCapability(StepperPreviousActionId, "Move to the previous step.", RequiresApproval: false, InputSchema: StepperPreviousInputSchema));

        builder.AddComponent(
            AgentCommandBarComponentId,
            "AgentCommandBar interactions for command discovery and invocation.",
            new ComponentActionCapability(CommandBarInvokeCommandActionId, "Invoke one command by id or name.", RequiresApproval: false, InputSchema: CommandBarInvokeCommandInputSchema),
            new ComponentActionCapability(CommandBarListCommandsActionId, "List available commands.", RequiresApproval: false, InputSchema: CommandBarListCommandsInputSchema));

        builder.AddComponent(
            AgentFileUploadComponentId,
            "AgentFileUpload interactions for attaching, removing, and listing file names.",
            new ComponentActionCapability(FileUploadAttachActionId, "Attach one file name to the current upload list.", RequiresApproval: false, InputSchema: FileUploadAttachInputSchema),
            new ComponentActionCapability(FileUploadRemoveActionId, "Remove one file name from the current upload list.", RequiresApproval: false, InputSchema: FileUploadRemoveInputSchema),
            new ComponentActionCapability(FileUploadListFilesActionId, "List currently attached files.", RequiresApproval: false, InputSchema: FileUploadListFilesInputSchema));
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
              "description": "Filter operator. For threshold phrases (e.g. 'high', 'low') use gte or lte so the app can map semantic values; use eq only for exact match. Use the sort action for asc/desc ordering."
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
            "page": { "type": "integer", "minimum": 1 },
            "pageIndex": { "type": "integer", "minimum": 0, "description": "Legacy zero-based page index." },
            "pageSize": { "type": "integer", "minimum": 1 }
          },
          "required": ["page"]
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

    public const string SelectOpenInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string SelectCloseInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string SelectSetValueInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "value": { "type": "string" }
          },
          "required": ["value"]
        }
        """;

    public const string SelectClearInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string AutocompleteSetQueryInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": { "type": "string" }
          },
          "required": ["query"]
        }
        """;

    public const string AutocompleteSelectOptionInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "value": { "type": "string" }
          },
          "required": ["value"]
        }
        """;

    public const string AutocompleteClearInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string DatePickerSetDateInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "date": { "type": "string", "description": "Date in yyyy-MM-dd or natural-language form." }
          },
          "required": ["date"]
        }
        """;

    public const string DatePickerClearInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string DateRangePickerSetRangeInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "startDate": { "type": "string", "description": "Range start date." },
            "endDate": { "type": "string", "description": "Range end date." }
          },
          "required": ["startDate", "endDate"]
        }
        """;

    public const string DateRangePickerClearInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string TreeViewExpandInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "nodeId": { "type": "string" }
          },
          "required": ["nodeId"]
        }
        """;

    public const string TreeViewCollapseInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "nodeId": { "type": "string" }
          },
          "required": ["nodeId"]
        }
        """;

    public const string TreeViewSelectNodeInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "nodeId": { "type": "string" }
          },
          "required": ["nodeId"]
        }
        """;

    public const string StepperGoToStepInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "index": { "type": "integer", "minimum": 0 }
          },
          "required": ["index"]
        }
        """;

    public const string StepperNextInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string StepperPreviousInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string CommandBarInvokeCommandInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "command": { "type": "string" }
          },
          "required": ["command"]
        }
        """;

    public const string CommandBarListCommandsInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public const string FileUploadAttachInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "fileName": { "type": "string" }
          },
          "required": ["fileName"]
        }
        """;

    public const string FileUploadRemoveInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "fileName": { "type": "string" }
          },
          "required": ["fileName"]
        }
        """;

    public const string FileUploadListFilesInputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;
}
