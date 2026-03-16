using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Runtime;
using AgentBlazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Reflection;

#pragma warning disable BL0005 // Setting parameters directly is intentional in wrapper logic unit tests.

namespace AgentBlazor.Components.Tests;

public class WrapperActionExecutionTests
{
    [Fact]
    public void AgentDataGrid_RowClassFunc_IsStronglyTypedParameter()
    {
        static string RowClass(SupplierRow row, int index)
        {
            _ = index;
            return row.RiskScore >= 70 ? "focused" : string.Empty;
        }

        var grid = new AgentDataGrid<SupplierRow>
        {
            AgentId = "supplier-grid",
            RowClassFunc = RowClass
        };

        Assert.NotNull(grid.RowClassFunc);
        Assert.Equal("focused", grid.RowClassFunc(new SupplierRow("S1", "EMEA", 80), 0));
    }

    [Fact]
    public async Task AgentDataGrid_ExecutesFilterSortPageAndNavigate_Actions()
    {
        var rows = new[]
        {
            new SupplierRow("S1", "EMEA", 30),
            new SupplierRow("S2", "APAC", 10),
            new SupplierRow("S3", "EMEA", 80)
        };

        var grid = new AgentDataGrid<SupplierRow>
        {
            AgentId = "supplier-grid",
            Items = rows,
            RowKeyProperty = nameof(SupplierRow.SupplierId)
        };

        string? lastSortColumn = null;
        string? lastSortDirection = null;
        string? lastFilterColumn = null;
        string? lastFilterOperator = null;
        object? lastFilterValue = null;
        int lastPageIndex = -1;
        int lastPageSize = 0;
        string? focusedRow = null;

        var callbacks = new EventCallbackFactory();
        grid.SortColumnChanged = callbacks.Create<string?>(this, value => lastSortColumn = value);
        grid.SortDirectionChanged = callbacks.Create<string>(this, value => lastSortDirection = value);
        grid.FilterColumnChanged = callbacks.Create<string?>(this, value => lastFilterColumn = value);
        grid.FilterOperatorChanged = callbacks.Create<string?>(this, value => lastFilterOperator = value);
        grid.FilterValueChanged = callbacks.Create<object?>(this, value => lastFilterValue = value);
        grid.CurrentPageIndexChanged = callbacks.Create<int>(this, value => lastPageIndex = value);
        grid.PageSizeChanged = callbacks.Create<int>(this, value => lastPageSize = value);
        grid.FocusedRowKeyChanged = callbacks.Create<string?>(this, value => focusedRow = value);

        var filter = await grid.ExecuteActionAsync(AgentAction.Create("filter", new Dictionary<string, object?>
        {
            ["column"] = "Region",
            ["operator"] = "eq",
            ["value"] = "EMEA"
        }));
        var sort = await grid.ExecuteActionAsync(AgentAction.Create("sort", new Dictionary<string, object?>
        {
            ["column"] = "RiskScore",
            ["direction"] = "desc"
        }));
        var page = await grid.ExecuteActionAsync(AgentAction.Create("go_to_page", new Dictionary<string, object?>
        {
            ["page"] = 2,
            ["pageSize"] = 2
        }));
        var navigate = await grid.ExecuteActionAsync(AgentAction.Create("select_row", new Dictionary<string, object?>
        {
            ["rowKey"] = "S3"
        }));
        var clear = await grid.ExecuteActionAsync(AgentAction.Create("clear_filters"));

        Assert.True(filter.Succeeded);
        Assert.True(sort.Succeeded);
        Assert.True(page.Succeeded);
        Assert.True(navigate.Succeeded);
        Assert.True(clear.Succeeded);
        Assert.Contains("page 2", page.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Null(grid.FilterColumn);
        Assert.Null(grid.FilterOperator);
        Assert.Null(grid.FilterValue);
        Assert.Equal("RiskScore", grid.SortColumn);
        Assert.Equal("desc", grid.SortDirection);
        Assert.Equal(2, grid.PageSize);
        Assert.Equal(0, grid.CurrentPageIndex); // S3 is first item after filter+sort.
        Assert.Equal("S3", grid.FocusedRowKey);

        Assert.Equal("RiskScore", lastSortColumn);
        Assert.Equal("desc", lastSortDirection);
        Assert.Null(lastFilterColumn);
        Assert.Null(lastFilterOperator);
        Assert.Null(lastFilterValue);
        Assert.Equal(0, lastPageIndex);
        Assert.Equal(2, lastPageSize);
        Assert.Equal("S3", focusedRow);

        var state = grid.GetCurrentState();
        Assert.Equal("RiskScore", state["sortColumn"]?.ToString());
        Assert.Equal("desc", state["sortDirection"]?.ToString());
        Assert.Equal(1, state["currentPage"]);
        Assert.Null(state["filterColumn"]);
        Assert.Null(state["filterOperator"]);
        Assert.Equal("S3", state["focusedRowKey"]?.ToString());
    }

    [Fact]
    public async Task AgentDataGrid_FilterWithOnlyIntent_FailsWithoutExplicitParameters()
    {
        var rows = new[]
        {
            new SupplierRow("S1", "EMEA", 30),
            new SupplierRow("S2", "APAC", 10),
            new SupplierRow("S3", "EMEA", 80)
        };

        var grid = new AgentDataGrid<SupplierRow>
        {
            AgentId = "supplier-grid",
            Items = rows
        };

        var highest = await grid.ExecuteActionAsync(AgentAction.Create("filter", new Dictionary<string, object?>
        {
            ["intent"] = "filter by highest risk supplier"
        }));
        var lowest = await grid.ExecuteActionAsync(AgentAction.Create("filter", new Dictionary<string, object?>
        {
            ["intent"] = "filter by lowest risk supplier"
        }));

        Assert.False(highest.Succeeded);
        Assert.False(lowest.Succeeded);
        Assert.Contains("Required parameter 'column' is missing", highest.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Required parameter 'column' is missing", lowest.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(grid.FilterColumn);
        Assert.Null(grid.FilterOperator);
        Assert.Null(grid.FilterValue);
    }

    [Fact]
    public async Task AgentDataGrid_FilterWithoutParameters_FailsWhenNoResolvableIntentOrArguments()
    {
        var rows = new[]
        {
            new RegionOnlyRow("S1", "EMEA"),
            new RegionOnlyRow("S2", "APAC")
        };

        var grid = new AgentDataGrid<RegionOnlyRow>
        {
            AgentId = "region-grid",
            Items = rows
        };

        var result = await grid.ExecuteActionAsync(AgentAction.Create("filter"));

        Assert.False(result.Succeeded);
        Assert.Contains("Required parameter 'column' is missing", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentDataGrid_SortWithoutColumn_FailsWithoutExplicitColumn()
    {
        var rows = new[]
        {
            new SupplierRow("S1", "EMEA", 30),
            new SupplierRow("S2", "APAC", 10),
            new SupplierRow("S3", "EMEA", 80)
        };

        var grid = new AgentDataGrid<SupplierRow>
        {
            AgentId = "supplier-grid",
            Items = rows
        };

        var result = await grid.ExecuteActionAsync(AgentAction.Create("sort", new Dictionary<string, object?>
        {
            ["intent"] = "now sort from highest to lowest",
            ["currentFilterColumn"] = "RiskScore"
        }));

        Assert.False(result.Succeeded);
        Assert.Contains("Required parameter 'column' is missing", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(grid.SortColumn);
        Assert.Equal("asc", grid.SortDirection);
    }

    [Fact]
    public async Task AgentDataGrid_FilterByName_ResolvesSupplierNameProperty()
    {
        var rows = new[]
        {
            new SupplierNameRow("SUP-001", "Alpine Components", 82),
            new SupplierNameRow("SUP-002", "Beacon Industrial", 55)
        };

        var grid = new AgentDataGrid<SupplierNameRow>
        {
            AgentId = "supplier-grid",
            Items = rows
        };

        var result = await grid.ExecuteActionAsync(AgentAction.Create("filter", new Dictionary<string, object?>
        {
            ["column"] = "SupplierName",
            ["operator"] = "contains",
            ["value"] = "Alpine"
        }));

        Assert.True(result.Succeeded);
        Assert.Equal("SupplierName", grid.FilterColumn);
        Assert.Contains("SupplierName contains Alpine", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Row inference from filter context was removed — rowKey is now required.")]
    public async Task AgentDataGrid_SelectRow_WithoutRowKey_InferFromHighRiskFilter_SelectsHighestRiskRow()
    {
        var rows = new[]
        {
            new SupplierRow("S1", "EMEA", 72),
            new SupplierRow("S2", "APAC", 91),
            new SupplierRow("S3", "NA", 80)
        };

        var grid = new AgentDataGrid<SupplierRow>
        {
            AgentId = "supplier-grid",
            Items = rows,
            RowKeyProperty = nameof(SupplierRow.SupplierId)
        };

        var filter = await grid.ExecuteActionAsync(AgentAction.Create("filter", new Dictionary<string, object?>
        {
            ["column"] = "RiskScore",
            ["operator"] = "gte",
            ["value"] = 70
        }));
        var select = await grid.ExecuteActionAsync(AgentAction.Create("select_row"));

        Assert.True(filter.Succeeded);
        Assert.True(select.Succeeded);
        Assert.Equal("S2", grid.FocusedRowKey);
        Assert.Contains("inferred from current view", select.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentDataGrid_SelectRow_WithoutRowKey_FailsWhenSelectionIsAmbiguous()
    {
        var rows = new[]
        {
            new SupplierRow("S1", "EMEA", 30),
            new SupplierRow("S2", "APAC", 10),
            new SupplierRow("S3", "NA", 80)
        };

        var grid = new AgentDataGrid<SupplierRow>
        {
            AgentId = "supplier-grid",
            Items = rows,
            RowKeyProperty = nameof(SupplierRow.SupplierId)
        };

        var result = await grid.ExecuteActionAsync(AgentAction.Create("select_row"));

        Assert.False(result.Succeeded);
        Assert.Contains("Required parameter 'rowKey' is missing", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentForm_ExecutesValidateSubmitAndReset_Actions()
    {
        // Note: SetField was removed - see comment below for recommended pattern
        var model = new SupplierFormModel
        {
            SupplierName = "Contoso",
            RequestedBudget = 1000m
        };

        var form = new AgentForm
        {
            AgentId = "supplier-form",
            Model = model
        };

        var callbacks = new EventCallbackFactory();
        bool? observedValidation = null;
        var submitCount = 0;
        form.ValidationChanged = callbacks.Create<bool>(this, valid => observedValidation = valid);
        form.Submitted = callbacks.Create(this, () => submitCount++);

        var validate = await form.ExecuteActionAsync(AgentAction.Create("validate"));
        var submit = await form.ExecuteActionAsync(AgentAction.Create("submit"));
        var reset = await form.ExecuteActionAsync(AgentAction.Create("reset"));

        Assert.True(validate.Succeeded);
        Assert.True(submit.Succeeded);
        Assert.True(reset.Succeeded);
        Assert.Equal("Contoso", model.SupplierName); // Reset restores original value
        Assert.Equal(1, submitCount);
        Assert.False(observedValidation); // Reset clears validation state
    }

    [Fact]
    public async Task AgentDialog_ExecutesOpenConfirmAndClose_Actions()
    {
        var dialog = new AgentDialog
        {
            AgentId = "supplier-dialog",
            OnConfirm = () => Task.FromResult(ActionResult.Applied("Confirmed supplier dialog."))
        };

        bool? observedVisible = null;
        var callbacks = new EventCallbackFactory();
        dialog.VisibleChanged = callbacks.Create<bool>(this, value => observedVisible = value);

        var open = await dialog.ExecuteActionAsync(AgentAction.Create("open"));
        var confirm = await dialog.ExecuteActionAsync(AgentAction.Create("confirm"));
        var close = await dialog.ExecuteActionAsync(AgentAction.Create("close"));

        Assert.True(open.Succeeded);
        Assert.True(confirm.Succeeded);
        Assert.True(close.Succeeded);
        Assert.False(dialog.Visible);
        Assert.False(observedVisible);
        Assert.Equal("Confirmed supplier dialog.", confirm.Message);

        var state = dialog.GetCurrentState();
        Assert.Equal(false, state["visible"]);
    }

    [Fact]
    public async Task AgentFormPageBase_SetField_UpdatesSingleField_AndOpensDialog()
    {
        var page = new RecipeFormPage();

        var result = await page.ExecuteActionAsync(AgentAction.Create("set_field", new Dictionary<string, object?>
        {
            ["field"] = "recipe title",
            ["value"] = "Test Recipe"
        }));

        Assert.True(result.Succeeded);
        Assert.Equal("Test Recipe", page.CurrentModel.Title);
        Assert.True(page.IsDialogOpen);
    }

    [Fact]
    public async Task AgentFormPageBase_SetAliasAction_AllowsPartialUpdates()
    {
        var page = new RecipeFormPage();

        var result = await page.ExecuteActionAsync(AgentAction.Create("set_recipe", new Dictionary<string, object?>
        {
            ["title"] = "Refined Recipe"
        }));

        Assert.True(result.Succeeded);
        Assert.Equal("Refined Recipe", page.CurrentModel.Title);
        Assert.Equal(15, page.CurrentModel.Minutes);
        Assert.True(page.IsDialogOpen);
    }

    [Fact]
    public async Task AgentSelect_ExecutesOpenSetValueClearAndClose_Actions()
    {
        var select = new AgentSelect<string>
        {
            AgentId = "country-select",
            Options = ["United Kingdom", "United States", "Canada"]
        };

        string? observedValue = null;
        var callbacks = new EventCallbackFactory();
        select.ValueChanged = callbacks.Create<string>(this, value => observedValue = value);

        var open = await select.ExecuteActionAsync(AgentAction.Create("open"));
        var set = await select.ExecuteActionAsync(AgentAction.Create("set_value", new Dictionary<string, object?>
        {
            ["value"] = "Canada"
        }));
        var clear = await select.ExecuteActionAsync(AgentAction.Create("clear"));
        var close = await select.ExecuteActionAsync(AgentAction.Create("close"));

        Assert.True(open.Succeeded);
        Assert.True(set.Succeeded);
        Assert.True(clear.Succeeded);
        Assert.True(close.Succeeded);

        Assert.Null(select.Value);
        Assert.Null(observedValue);

        var state = select.GetCurrentState();
        Assert.Equal(false, state["isOpen"]);
        Assert.Equal(3, ((string[])state["options"]!).Length);
        Assert.Null(state["value"]);
    }

    [Fact]
    public async Task AgentTabs_ExecutesSwitchTab_Action()
    {
        var tabs = new AgentTabs
        {
            AgentId = "components-tabs",
            ActivePanelIndex = 0
        };

        var observedIndex = -1;
        var callbacks = new EventCallbackFactory();
        tabs.ActivePanelIndexChanged = callbacks.Create<int>(this, index => observedIndex = index);

        var switchTab = await tabs.ExecuteActionAsync(AgentAction.Create("switch_tab", new Dictionary<string, object?>
        {
            ["index"] = 2
        }));

        Assert.True(switchTab.Succeeded);
        Assert.Equal(2, tabs.ActivePanelIndex);
        Assert.Equal(2, observedIndex);

        var state = tabs.GetCurrentState();
        Assert.Equal(2, state["activePanelIndex"]);
        Assert.Empty((string[])state["availableTabs"]!);
    }

    [Fact]
    public async Task AgentAutocomplete_ExecutesSetQuerySelectOptionAndClear_Actions()
    {
        var autocomplete = new AgentAutocomplete<string>
        {
            AgentId = "country-autocomplete",
            Options = ["United Kingdom", "United States", "Canada"]
        };

        string? observedQuery = null;
        string? observedValue = null;
        var callbacks = new EventCallbackFactory();
        autocomplete.QueryChanged = callbacks.Create<string?>(this, query => observedQuery = query);
        autocomplete.ValueChanged = callbacks.Create<string>(this, value => observedValue = value);

        var setQuery = await autocomplete.ExecuteActionAsync(AgentAction.Create("set_query", new Dictionary<string, object?>
        {
            ["query"] = "Uni"
        }));
        var selectOption = await autocomplete.ExecuteActionAsync(AgentAction.Create("select_option", new Dictionary<string, object?>
        {
            ["value"] = "United States"
        }));
        var clear = await autocomplete.ExecuteActionAsync(AgentAction.Create("clear"));

        Assert.True(setQuery.Succeeded);
        Assert.True(selectOption.Succeeded);
        Assert.True(clear.Succeeded);

        Assert.Null(autocomplete.Query);
        Assert.Null(autocomplete.Value);
        Assert.Null(observedQuery);
        Assert.Null(observedValue);

        var state = autocomplete.GetCurrentState();
        Assert.Null(state["query"]);
        Assert.Null(state["selectedValue"]);
        Assert.Equal(3, ((string[])state["options"]!).Length);
    }

    [Fact]
    public async Task AgentDatePicker_ExecutesSetDateAndClear_Actions()
    {
        var picker = new AgentDatePicker
        {
            AgentId = "invoice-date",
            MinDate = new DateTime(2026, 1, 1),
            MaxDate = new DateTime(2026, 12, 31)
        };

        DateTime? observed = null;
        var callbacks = new EventCallbackFactory();
        picker.ValueChanged = callbacks.Create<DateTime?>(this, value => observed = value);

        var set = await picker.ExecuteActionAsync(AgentAction.Create("set_date", new Dictionary<string, object?>
        {
            ["date"] = "2026-03-15"
        }));
        var clear = await picker.ExecuteActionAsync(AgentAction.Create("clear"));

        Assert.True(set.Succeeded);
        Assert.True(clear.Succeeded);
        Assert.Null(picker.Value);
        Assert.Null(observed);

        var state = picker.GetCurrentState();
        Assert.Null(state["value"]);
        Assert.Equal("2026-01-01", state["minDate"]);
        Assert.Equal("2026-12-31", state["maxDate"]);
    }

    [Fact]
    public async Task AgentDateRangePicker_ExecutesSetRangeAndClear_Actions()
    {
        var range = new AgentDateRangePicker
        {
            AgentId = "travel-range",
            MinDate = new DateTime(2026, 1, 1),
            MaxDate = new DateTime(2026, 12, 31)
        };

        DateTime? observedStart = null;
        DateTime? observedEnd = null;
        var callbacks = new EventCallbackFactory();
        range.StartDateChanged = callbacks.Create<DateTime?>(this, value => observedStart = value);
        range.EndDateChanged = callbacks.Create<DateTime?>(this, value => observedEnd = value);

        var set = await range.ExecuteActionAsync(AgentAction.Create("set_range", new Dictionary<string, object?>
        {
            ["startDate"] = "2026-05-01",
            ["endDate"] = "2026-05-15"
        }));
        var clear = await range.ExecuteActionAsync(AgentAction.Create("clear"));

        Assert.True(set.Succeeded);
        Assert.True(clear.Succeeded);
        Assert.Null(range.StartDate);
        Assert.Null(range.EndDate);
        Assert.Null(observedStart);
        Assert.Null(observedEnd);

        var state = range.GetCurrentState();
        Assert.Null(state["startDate"]);
        Assert.Null(state["endDate"]);
        Assert.Equal("2026-01-01", state["minDate"]);
        Assert.Equal("2026-12-31", state["maxDate"]);
    }

    [Fact]
    public async Task AgentDateRangePicker_SetRange_Fails_WhenStartIsAfterEnd()
    {
        var range = new AgentDateRangePicker
        {
            AgentId = "travel-range"
        };

        var result = await range.ExecuteActionAsync(AgentAction.Create("set_range", new Dictionary<string, object?>
        {
            ["startDate"] = "2026-06-30",
            ["endDate"] = "2026-06-01"
        }));

        Assert.False(result.Succeeded);
        Assert.Contains("Start date cannot be after end date", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentTreeView_ExecutesExpandSelectAndCollapse_Actions()
    {
        var tree = new AgentTreeView<string>
        {
            AgentId = "supplier-tree",
            NodeIds = ["root", "supplier-a", "supplier-b"]
        };

        string? observedSelected = null;
        IReadOnlyList<string>? observedExpanded = null;
        var callbacks = new EventCallbackFactory();
        tree.SelectedNodeIdChanged = callbacks.Create<string?>(this, value => observedSelected = value);
        tree.ExpandedNodeIdsChanged = callbacks.Create<IReadOnlyList<string>>(this, value => observedExpanded = value);

        var expand = await tree.ExecuteActionAsync(AgentAction.Create("expand", new Dictionary<string, object?>
        {
            ["nodeId"] = "root"
        }));
        var select = await tree.ExecuteActionAsync(AgentAction.Create("select_node", new Dictionary<string, object?>
        {
            ["nodeId"] = "supplier-a"
        }));
        var collapse = await tree.ExecuteActionAsync(AgentAction.Create("collapse", new Dictionary<string, object?>
        {
            ["nodeId"] = "root"
        }));

        Assert.True(expand.Succeeded);
        Assert.True(select.Succeeded);
        Assert.True(collapse.Succeeded);

        Assert.Equal("supplier-a", tree.SelectedNodeId);
        Assert.Equal("supplier-a", observedSelected);
        Assert.NotNull(observedExpanded);
        Assert.Empty(observedExpanded!);

        var state = tree.GetCurrentState();
        Assert.Equal("supplier-a", state["selectedNodeId"]);
        Assert.Empty((string[])state["expandedNodeIds"]!);
    }

    [Fact]
    public async Task AgentNavMenu_ExecutesInternalAndExternalNavigation_Actions()
    {
        var nav = new AgentNavMenu
        {
            AgentId = "demo-nav"
        };

        var navigation = new TestNavigationManager();
        typeof(AgentNavMenu)
            .GetProperty("Navigation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(nav, navigation);

        var internalNavigate = await nav.ExecuteActionAsync(AgentAction.Create("navigate_to", new Dictionary<string, object?>
        {
            ["uri"] = "/demo/components"
        }));
        var externalNavigate = await nav.ExecuteActionAsync(AgentAction.Create("navigate_external", new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/docs"
        }));

        Assert.True(internalNavigate.Succeeded);
        Assert.True(externalNavigate.Succeeded);
        Assert.Equal("https://example.com/docs", nav.CurrentUri);

        var state = nav.GetCurrentState();
        Assert.Equal("https://example.com/docs", state["uri"]);
    }

    [Fact]
    public async Task AgentStepper_ExecutesGoToNextAndPrevious_Actions()
    {
        var stepper = new AgentStepper
        {
            AgentId = "onboarding-steps",
            TotalSteps = 4
        };

        int observedStep = -1;
        var callbacks = new EventCallbackFactory();
        stepper.CurrentStepIndexChanged = callbacks.Create<int>(this, value => observedStep = value);

        var goTo = await stepper.ExecuteActionAsync(AgentAction.Create("go_to_step", new Dictionary<string, object?>
        {
            ["index"] = 1
        }));
        var next = await stepper.ExecuteActionAsync(AgentAction.Create("next"));
        var previous = await stepper.ExecuteActionAsync(AgentAction.Create("previous"));

        Assert.True(goTo.Succeeded);
        Assert.True(next.Succeeded);
        Assert.True(previous.Succeeded);

        Assert.Equal(1, stepper.CurrentStepIndex);
        Assert.Equal(1, observedStep);

        var state = stepper.GetCurrentState();
        Assert.Equal(1, state["currentStepIndex"]);
        Assert.Equal(4, state["totalSteps"]);
        Assert.Equal(true, state["canGoNext"]);
        Assert.Equal(true, state["canGoPrevious"]);
    }

    [Fact]
    public async Task AgentStepper_GoToStep_FailsWhenIndexIsOutOfRange()
    {
        var stepper = new AgentStepper
        {
            AgentId = "onboarding-steps",
            TotalSteps = 2
        };

        var result = await stepper.ExecuteActionAsync(AgentAction.Create("go_to_step", new Dictionary<string, object?>
        {
            ["index"] = 4
        }));

        Assert.False(result.Succeeded);
        Assert.Contains("out of range", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentCommandBar_ExecutesInvokeAndList_Actions()
    {
        var bar = new AgentCommandBar
        {
            AgentId = "global-command-bar",
            Commands = ["refresh", "export", "archive"]
        };

        string? observedCommand = null;
        var callbacks = new EventCallbackFactory();
        bar.CommandInvoked = callbacks.Create<string>(this, command => observedCommand = command);

        var invoke = await bar.ExecuteActionAsync(AgentAction.Create("invoke_command", new Dictionary<string, object?>
        {
            ["command"] = "export"
        }));
        var list = await bar.ExecuteActionAsync(AgentAction.Create("list_commands"));

        Assert.True(invoke.Succeeded);
        Assert.True(list.Succeeded);
        Assert.Equal("export", observedCommand);
        Assert.Contains("refresh", list.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("export", list.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("archive", list.Message, StringComparison.OrdinalIgnoreCase);

        var state = bar.GetCurrentState();
        Assert.Equal("export", state["lastInvokedCommand"]);
    }

    [Fact]
    public async Task AgentFileUpload_ExecutesAttachListAndRemove_Actions()
    {
        var upload = new AgentFileUpload<IReadOnlyList<IBrowserFile>>
        {
            AgentId = "attachments"
        };

        IReadOnlyList<string>? observedFiles = null;
        var callbacks = new EventCallbackFactory();
        upload.FileNamesChanged = callbacks.Create<IReadOnlyList<string>>(this, files => observedFiles = files);

        var attach = await upload.ExecuteActionAsync(AgentAction.Create("attach", new Dictionary<string, object?>
        {
            ["fileName"] = "quote.pdf"
        }));
        var list = await upload.ExecuteActionAsync(AgentAction.Create("list_files"));
        var remove = await upload.ExecuteActionAsync(AgentAction.Create("remove", new Dictionary<string, object?>
        {
            ["fileName"] = "quote.pdf"
        }));

        Assert.True(attach.Succeeded);
        Assert.True(list.Succeeded);
        Assert.True(remove.Succeeded);
        Assert.NotNull(observedFiles);
        Assert.Empty(observedFiles!);
        Assert.Contains("quote.pdf", list.Message, StringComparison.OrdinalIgnoreCase);

        var state = upload.GetCurrentState();
        Assert.Equal(0, state["fileCount"]);
    }

    // NOTE: SetField tests removed because the SetField action was removed from AgentForm.
    // The generic SetField approach is unreliable for forms inside dialogs (forms aren't mounted
    // when dialog is closed). The recommended pattern is to create compound workflow actions
    // with explicit parameters. See SupplierOnboardingAgent.razor for an example.

    private sealed record SupplierRow(string SupplierId, string Region, int RiskScore);

    private sealed record RegionOnlyRow(string SupplierId, string Region);

    private sealed record SupplierNameRow(string SupplierId, string SupplierName, int RiskScore);

    private sealed class SupplierFormModel
    {
        public string SupplierName { get; set; } = string.Empty;

        public decimal RequestedBudget { get; set; }
    }

    private sealed class PersonFormModel
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://example.com/", "https://example.com/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }

    private sealed class RecipeFormPage : AgentFormPageBase<RecipeModel>
    {
        protected override string AgentIdValue => "recipe-form-workflow";

        protected override string FormDisplayName => "Recipe";

        public RecipeModel CurrentModel => Model;

        public bool IsDialogOpen => DialogVisible;
    }

    private sealed class RecipeModel
    {
        public string Title { get; set; } = string.Empty;

        public int Minutes { get; set; } = 15;
    }
}

#pragma warning restore BL0005
