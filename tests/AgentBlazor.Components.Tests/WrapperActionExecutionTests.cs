using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using Microsoft.AspNetCore.Components;

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
            ["pageIndex"] = 1,
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
        Assert.Contains("Applied filter SupplierName contains Alpine", result.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task AgentForm_ExecutesSetValidateSubmitAndReset_Actions()
    {
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

        var setField = await form.ExecuteActionAsync(AgentAction.Create("set_field", new Dictionary<string, object?>
        {
            ["field"] = nameof(SupplierFormModel.SupplierName),
            ["value"] = "Northwind"
        }));

        var validate = await form.ExecuteActionAsync(AgentAction.Create("validate"));
        var submit = await form.ExecuteActionAsync(AgentAction.Create("submit"));
        var reset = await form.ExecuteActionAsync(AgentAction.Create("reset"));

        Assert.True(setField.Succeeded);
        Assert.True(validate.Succeeded);
        Assert.True(submit.Succeeded);
        Assert.True(reset.Succeeded);
        Assert.Equal("Contoso", model.SupplierName);
        Assert.Equal(1, submitCount);
        Assert.False(observedValidation);
    }

    [Fact]
    public async Task AgentForm_SetField_ResolvesNaturalFieldHint_ToCanonicalProperty()
    {
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

        var result = await form.ExecuteActionAsync(AgentAction.Create("set_field", new Dictionary<string, object?>
        {
            ["field"] = "SupplierName",
            ["value"] = "Ash"
        }));

        Assert.True(result.Succeeded);
        Assert.Equal("Ash", model.SupplierName);
        Assert.Contains("SupplierName", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentForm_SetField_FailsWhenFieldHintIsAmbiguous()
    {
        var model = new PersonFormModel
        {
            FirstName = "Ada",
            LastName = "Lovelace"
        };

        var form = new AgentForm
        {
            AgentId = "person-form",
            Model = model
        };

        var result = await form.ExecuteActionAsync(AgentAction.Create("set_field", new Dictionary<string, object?>
        {
            ["field"] = "name",
            ["value"] = "Ash"
        }));

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Ada", model.FirstName);
        Assert.Equal("Lovelace", model.LastName);
    }

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
}

#pragma warning restore BL0005
