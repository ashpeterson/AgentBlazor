using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace AgentBlazor.Components;

[CascadingTypeParameter(nameof(TItem))]
public class AgentDataGrid<TItem> : MudDataGrid<TItem>, IAgentControllable
{
    [Inject]
    private IAgentComponentRegistry ComponentRegistry { get; set; } = default!;

    [Inject]
    private IAgentNavigationIntentService NavigationIntentService { get; set; } = default!;

    [Inject]
    private NavigationManager? Navigation { get; set; }

    [Inject]
    private ILoggerFactory? LoggerFactory { get; set; }

    [Inject]
    private IAgentDeferredActionEvents? DeferredActionEvents { get; set; }

    [Parameter]
    public string AgentId { get; set; } = string.Empty;

    [Parameter]
    public string? SortColumn { get; set; }

    [Parameter]
    public string SortDirection { get; set; } = "asc";

    [Parameter]
    public EventCallback<string?> SortColumnChanged { get; set; }

    [Parameter]
    public EventCallback<string> SortDirectionChanged { get; set; }

    [Parameter]
    public string? FilterColumn { get; set; }

    [Parameter]
    public string? FilterOperator { get; set; }

    [Parameter]
    public object? FilterValue { get; set; }

    [Parameter]
    public EventCallback<string?> FilterColumnChanged { get; set; }

    [Parameter]
    public EventCallback<string?> FilterOperatorChanged { get; set; }

    [Parameter]
    public EventCallback<object?> FilterValueChanged { get; set; }

    [Parameter]
    public int CurrentPageIndex { get; set; }

    [Parameter]
    public EventCallback<int> CurrentPageIndexChanged { get; set; }

    [Parameter]
    public int PageSize { get; set; }

    [Parameter]
    public EventCallback<int> PageSizeChanged { get; set; }

    [Parameter]
    public string? FocusedRowKey { get; set; }

    [Parameter]
    public EventCallback<string?> FocusedRowKeyChanged { get; set; }

    [Parameter]
    public string RowKeyProperty { get; set; } = "Id";

    [Parameter]
    public IReadOnlyDictionary<string, string>? ColumnAliases { get; set; }

    public string ComponentType => "DataGrid";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private string? _lastSortColumn;
    private string? _lastSortDirection;
    private string? _lastFilterColumn;
    private string? _lastFilterOperator;
    private string? _lastFilterValue;
    private int _lastCurrentPageIndex = -1;
    private int _lastPageSize = -1;
    private string? _lastFocusedRowKey;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        _runtimeSupport ??= CreateRuntimeSupport();
        _runtimeSupport.OnInitialized();
        EnsureAgentUserAttributes();
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _runtimeSupport ??= CreateRuntimeSupport();
        await _runtimeSupport.OnInitializedAsync();
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        await SyncLegacyStateAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtimeSupport?.Dispose();
        }

        base.Dispose(disposing);
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual ComponentState GetCurrentState()
    {
        var columns = GetStateColumns();
        var columnTypes = GetColumnTypes(columns);
        var focusedRow = ResolveFocusedRowSnapshot();
        var currentViewRows = BuildCurrentViewRowSnapshots(maxRows: 25);
        var rowCount = ResolveRowCount();

        var state = new ComponentState
        {
            ["rowCount"] = rowCount,
            ["sortColumn"] = ResolveActiveSortColumn(),
            ["sortDirection"] = ResolveActiveSortDirection(),
            ["filterColumn"] = ResolveActiveFilterColumn(),
            ["filterOperator"] = ResolveActiveFilterOperator(),
            ["filterValue"] = ResolveActiveFilterValue(),
            ["currentPageIndex"] = CurrentPage,
            ["currentPage"] = CurrentPage + 1,
            ["pageSize"] = RowsPerPage,
            ["rowKeyProperty"] = RowKeyProperty,
            ["focusedRowKey"] = FocusedRowKey,
            ["columns"] = columns,
            ["columnTypes"] = columnTypes
        };

        if (focusedRow is not null)
        {
            state["focusedRow"] = focusedRow;
        }

        if (currentViewRows.Count > 0)
        {
            state["currentViewRows"] = currentViewRows;
        }

        return state;
    }

    public virtual async Task<ActionResult> ExecuteActionAsync(
        AgentAction action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ActionResult result = ActionResult.Applied($"Executed '{action.Name}'.");
            await InvokeAsync(async () =>
            {
                result = await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
            });
            return result;
        }
        catch (InvalidOperationException)
        {
            return await AgentActionDiscovery.ExecuteActionAsync(this, action, cancellationToken);
        }
    }

    [AgentAction("Sort the data grid by a column")]
    public async Task<ActionResult> Sort(
        [AgentParam("Column/property name to sort by (use exact column name from state)", Required = true)] string column,
        [AgentParam("Sort direction", Required = true, AllowedValues = "asc,desc")] string direction = "asc")
    {
        var normalizedDirection = direction.Trim().ToLowerInvariant();
        if (!IsSupportedSortDirection(normalizedDirection))
        {
            return ActionResult.NeedsClarification("Sort direction must be 'asc' or 'desc'.");
        }

        var resolved = ResolveColumn(column);
        if (resolved is null)
        {
            var available = string.Join(", ", GetStateColumns());
            return ActionResult.NeedsClarification(
                $"Column '{column}' not found. Available columns: {available}.");
        }

        SortColumn = resolved.PropertyName;
        SortDirection = normalizedDirection;
        await SortColumnChanged.InvokeAsync(SortColumn);
        await SortDirectionChanged.InvokeAsync(SortDirection);

        if (resolved.PropertyName is not null && RenderedColumns.Count > 0)
        {
            await SetSortAsync(
                resolved.PropertyName,
                normalizedDirection == "desc" ? MudBlazor.SortDirection.Descending : MudBlazor.SortDirection.Ascending,
                item => ResolveComparableSortKey(item, resolved.PropertyName));
        }

        return await RefreshAsync(ActionResult.Applied($"Sorted by '{SortColumn}' ({SortDirection})."));
    }

    [AgentAction("Filter data grid rows by a column value")]
    public async Task<ActionResult> Filter(
        [AgentParam("Column/property name to filter (use exact column name from state)", Required = true)] string column,
        [AgentParam("Filter operator", Required = true, AllowedValues = "eq,neq,gt,gte,lt,lte,contains,startswith,endswith,in,notin,isnull,notnull")] string @operator,
        [AgentParam("Filter value (not needed for isnull/notnull)")] object? value = null)
    {
        var normalizedOp = NormalizeFilterOperator(@operator);
        if (!IsSupportedFilterOperator(normalizedOp))
        {
            return ActionResult.Failure(
                "Operator must be one of: eq, neq, gt, gte, lt, lte, contains, startswith, endswith, in, notin, isnull, notnull.");
        }

        if (RequiresFilterValue(normalizedOp) && value is null)
        {
            return ActionResult.NeedsClarification($"Operator '{normalizedOp}' requires a value.");
        }

        var resolved = ResolveColumn(column);
        if (resolved is null)
        {
            var available = string.Join(", ", GetStateColumns());
            return ActionResult.NeedsClarification(
                $"Column '{column}' not found. Available columns: {available}.");
        }

        var normalizedValue = NormalizeValue(value);
        FilterColumn = resolved.PropertyName;
        FilterOperator = normalizedOp;
        FilterValue = normalizedValue;
        CurrentPageIndex = 0;

        await FilterColumnChanged.InvokeAsync(FilterColumn);
        await FilterOperatorChanged.InvokeAsync(FilterOperator);
        await FilterValueChanged.InvokeAsync(FilterValue);
        await CurrentPageIndexChanged.InvokeAsync(CurrentPageIndex);

        if (resolved.Column is not null)
        {
            await ClearFiltersAsync();
            await AddFilterAsync(new FilterDefinition<TItem>
            {
                Column = resolved.Column,
                Title = resolved.Column.Title,
                Operator = MapMudFilterOperator(normalizedOp, resolved.PropertyType),
                Value = normalizedValue,
                FilterFunction = item => MatchesFilter(item, resolved.PropertyName!, normalizedOp, normalizedValue)
            });
            await SetCurrentPageSafelyAsync(0);
        }

        return await RefreshAsync(ActionResult.Applied($"Filtered {FilterColumn} {FilterOperator} {FilterValue ?? "null"}."));
    }

    [AgentAction("Clear all active filters on the data grid")]
    public async Task<ActionResult> ClearFilters()
    {
        FilterColumn = null;
        FilterOperator = null;
        FilterValue = null;
        CurrentPageIndex = 0;

        await FilterColumnChanged.InvokeAsync(FilterColumn);
        await FilterOperatorChanged.InvokeAsync(FilterOperator);
        await FilterValueChanged.InvokeAsync(FilterValue);
        await CurrentPageIndexChanged.InvokeAsync(CurrentPageIndex);

        if (RenderedColumns.Count > 0)
        {
            await ClearFiltersAsync();
            await SetCurrentPageSafelyAsync(0);
        }

        return await RefreshAsync(ActionResult.Applied("Cleared filters."));
    }

    [AgentAction("Navigate to a specific page in the data grid")]
    public async Task<ActionResult> GoToPage(
        [AgentParam("One-based page number", Required = true)] int? page = null,
        [AgentParam("Legacy zero-based page index")] int? pageIndex = null,
        [AgentParam("Number of rows per page")] int? pageSize = null)
    {
        var resolvedPageIndex = pageIndex;
        if (page.HasValue)
        {
            if (page.Value < 1)
            {
                return ActionResult.NeedsClarification("Page number must be 1 or greater.");
            }

            resolvedPageIndex = page.Value - 1;
        }

        if (!resolvedPageIndex.HasValue)
        {
            return ActionResult.NeedsClarification("Page number is required.");
        }

        if (resolvedPageIndex.Value < 0)
        {
            return ActionResult.NeedsClarification("Page index must be zero or greater.");
        }

        CurrentPageIndex = resolvedPageIndex.Value;
        await SetCurrentPageSafelyAsync(CurrentPageIndex);
        await CurrentPageIndexChanged.InvokeAsync(CurrentPageIndex);

        if (pageSize is > 0)
        {
            PageSize = pageSize.Value;
            await SetRowsPerPageSafelyAsync(PageSize, resetPage: false);
            await PageSizeChanged.InvokeAsync(PageSize);
        }

        return await RefreshAsync(ActionResult.Applied(RowsPerPage > 0
            ? $"Navigated to page {CurrentPage + 1} (page size {RowsPerPage})."
            : $"Navigated to page {CurrentPage + 1}."));
    }

    [AgentAction("Select and focus a specific row by its key value", ActionId = "select_row")]
    public async Task<ActionResult> SelectRow(
        [AgentParam("The row key value (from RowKeyProperty)", Required = true)] string rowKey)
    {
        var item = FindItemByKey(rowKey);
        if (item is null)
        {
            return ActionResult.NeedsClarification($"Row with key '{rowKey}' not found.");
        }

        if (RowsPerPage > 0 && TryResolveRowIndex(rowKey, out var rowIndex))
        {
            CurrentPageIndex = rowIndex / RowsPerPage;
            await SetCurrentPageSafelyAsync(CurrentPageIndex);
            await CurrentPageIndexChanged.InvokeAsync(CurrentPageIndex);
        }

        await SetSelectedItemSafelyAsync(item);
        FocusedRowKey = rowKey;
        await FocusedRowKeyChanged.InvokeAsync(FocusedRowKey);

        return await RefreshAsync(ActionResult.Applied($"Selected row '{FocusedRowKey}'."));
    }

    [AgentAction("Focus or navigate to a specific row by its key value", ActionId = "navigate_to_row")]
    public Task<ActionResult> NavigateToRow(
        [AgentParam("The row key value (from RowKeyProperty)", Required = true)] string rowKey)
        => SelectRow(rowKey);

    private AgentControllableComponentRuntimeSupport CreateRuntimeSupport()
    {
        return new AgentControllableComponentRuntimeSupport(
            componentType: GetType(),
            component: this,
            componentRegistry: ComponentRegistry,
            navigationIntentService: NavigationIntentService,
            navigation: Navigation,
            logger: _logger,
            deferredActionEvents: DeferredActionEvents,
            getComponentType: () => ComponentType,
            getAgentId: () => AgentId,
            setAgentId: value => AgentId = value,
            executeActionAsync: action => ExecuteActionAsync(action),
            requestComponentRefreshAsync: RequestComponentRefreshAsync);
    }

    private async Task SyncLegacyStateAsync()
    {
        EnsureAgentUserAttributes();

        var activeSortColumn = ResolveActiveSortColumn();
        var activeSortDirection = ResolveActiveSortDirection();
        var activeFilterColumn = ResolveActiveFilterColumn();
        var activeFilterOperator = ResolveActiveFilterOperator();
        var activeFilterValue = ResolveActiveFilterValue();
        var focusedRowKey = ResolveFocusedRowKey();

        if (!string.Equals(_lastSortColumn, activeSortColumn, StringComparison.OrdinalIgnoreCase))
        {
            _lastSortColumn = SortColumn = activeSortColumn;
            await SortColumnChanged.InvokeAsync(SortColumn);
        }

        if (!string.Equals(_lastSortDirection, activeSortDirection, StringComparison.OrdinalIgnoreCase))
        {
            _lastSortDirection = SortDirection = activeSortDirection;
            await SortDirectionChanged.InvokeAsync(SortDirection);
        }

        if (!string.Equals(_lastFilterColumn, activeFilterColumn, StringComparison.OrdinalIgnoreCase))
        {
            _lastFilterColumn = FilterColumn = activeFilterColumn;
            await FilterColumnChanged.InvokeAsync(FilterColumn);
        }

        if (!string.Equals(_lastFilterOperator, activeFilterOperator, StringComparison.OrdinalIgnoreCase))
        {
            _lastFilterOperator = FilterOperator = activeFilterOperator;
            await FilterOperatorChanged.InvokeAsync(FilterOperator);
        }

        if (!string.Equals(_lastFilterValue, activeFilterValue, StringComparison.Ordinal))
        {
            _lastFilterValue = activeFilterValue;
            FilterValue = activeFilterValue;
            await FilterValueChanged.InvokeAsync(FilterValue);
        }

        if (_lastCurrentPageIndex != CurrentPage)
        {
            _lastCurrentPageIndex = CurrentPageIndex = CurrentPage;
            await CurrentPageIndexChanged.InvokeAsync(CurrentPageIndex);
        }

        if (_lastPageSize != RowsPerPage)
        {
            _lastPageSize = PageSize = RowsPerPage;
            await PageSizeChanged.InvokeAsync(PageSize);
        }

        if (!string.Equals(_lastFocusedRowKey, focusedRowKey, StringComparison.OrdinalIgnoreCase))
        {
            _lastFocusedRowKey = FocusedRowKey = focusedRowKey;
            await FocusedRowKeyChanged.InvokeAsync(FocusedRowKey);
        }
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= new Dictionary<string, object?>();
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "datagrid";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private string? ResolveActiveSortColumn()
    {
        if (SortDefinitions.Count > 0)
        {
            return SortDefinitions.Values
                .OrderBy(static definition => definition.Index)
                .Select(static definition => definition.SortBy)
                .FirstOrDefault();
        }

        return SortColumn;
    }

    private string ResolveActiveSortDirection()
    {
        if (SortDefinitions.Count > 0)
        {
            var definition = SortDefinitions.Values.OrderBy(static x => x.Index).First();
            return definition.Descending ? "desc" : "asc";
        }

        return SortDirection;
    }

    private string? ResolveActiveFilterColumn()
    {
        if (FilterDefinitions.Count > 0)
        {
            return FilterDefinitions.FirstOrDefault()?.Column?.PropertyName ?? FilterColumn;
        }

        return FilterColumn;
    }

    private string? ResolveActiveFilterOperator()
    {
        if (FilterDefinitions.Count > 0)
        {
            var filter = FilterDefinitions.FirstOrDefault();
            return NormalizeFilterOperator(filter?.Operator ?? string.Empty);
        }

        return FilterOperator;
    }

    private string? ResolveActiveFilterValue()
    {
        if (FilterDefinitions.Count > 0)
        {
            return FilterDefinitions.FirstOrDefault()?.Value?.ToString();
        }

        return FilterValue?.ToString();
    }

    private string? ResolveFocusedRowKey()
    {
        var selectedItem = GetSelectedItem();
        if (selectedItem is not null && TryGetPropertyValue(selectedItem, RowKeyProperty, out var keyValue))
        {
            return keyValue?.ToString();
        }

        return FocusedRowKey;
    }

    private int ResolveRowCount()
    {
        if (ServerData is not null || VirtualizeServerData is not null)
        {
            return GetFilteredItemsCount();
        }

        return Items switch
        {
            null => 0,
            ICollection<TItem> collection => collection.Count,
            IReadOnlyCollection<TItem> readOnlyCollection => readOnlyCollection.Count,
            _ => Items.Count()
        };
    }

    private IReadOnlyList<string> GetStateColumns()
    {
        if (RenderedColumns.Count == 0)
        {
            return GetFilterableColumns();
        }

        return RenderedColumns
            .Select(static column => column.PropertyName ?? column.Title)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static Dictionary<string, string> GetColumnTypes(IEnumerable<string> columns)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var properties = typeof(TItem)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (properties.TryGetValue(column, out var property))
            {
                result[column] = GetFriendlyTypeName(property.PropertyType);
            }
            else
            {
                result[column] = "object";
            }
        }

        return result;
    }

    private ColumnResolution? ResolveColumn(string hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        if (RenderedColumns.Count > 0)
        {
            var byProperty = RenderedColumns.FirstOrDefault(column =>
                !string.IsNullOrWhiteSpace(column.PropertyName) &&
                string.Equals(column.PropertyName, hint, StringComparison.OrdinalIgnoreCase));
            if (byProperty is not null)
            {
                return new ColumnResolution(byProperty.PropertyName, byProperty, ResolvePropertyType(byProperty.PropertyName));
            }

            var byTitle = RenderedColumns.FirstOrDefault(column =>
                !string.IsNullOrWhiteSpace(column.Title) &&
                string.Equals(column.Title, hint, StringComparison.OrdinalIgnoreCase));
            if (byTitle is not null)
            {
                var propertyName = byTitle.PropertyName ?? ResolveAliasedPropertyName(hint);
                return new ColumnResolution(propertyName, byTitle, ResolvePropertyType(propertyName));
            }
        }

        var aliased = ResolveAliasedPropertyName(hint);
        if (!string.IsNullOrWhiteSpace(aliased))
        {
            return new ColumnResolution(aliased, null, ResolvePropertyType(aliased));
        }

        var reflectionMatch = GetFilterableColumns()
            .FirstOrDefault(column => string.Equals(column, hint, StringComparison.OrdinalIgnoreCase)) ??
            GetFilterableColumns().FirstOrDefault(column => column.StartsWith(hint, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(reflectionMatch))
        {
            return new ColumnResolution(reflectionMatch, null, ResolvePropertyType(reflectionMatch));
        }

        return null;
    }

    private string? ResolveAliasedPropertyName(string hint)
    {
        if (ColumnAliases is not null &&
            ColumnAliases.TryGetValue(hint, out var aliased) &&
            !string.IsNullOrWhiteSpace(aliased))
        {
            return aliased;
        }

        return null;
    }

    private static Type? ResolvePropertyType(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var property = typeof(TItem)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return property?.PropertyType;
    }

    private IEnumerable<TItem> EffectiveItems => ApplyPaging(ApplySort(ApplyFilter(Items ?? [])));

    private IEnumerable<TItem> ApplyFilter(IEnumerable<TItem> source)
    {
        if (string.IsNullOrWhiteSpace(FilterColumn) || string.IsNullOrWhiteSpace(FilterOperator))
        {
            return source;
        }

        return source.Where(item => MatchesFilter(item, FilterColumn!, FilterOperator!, FilterValue));
    }

    private IEnumerable<TItem> ApplySort(IEnumerable<TItem> source)
    {
        if (string.IsNullOrWhiteSpace(SortColumn))
        {
            return source;
        }

        var descending = string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return descending
            ? source.OrderByDescending(item => ResolveComparableSortKey(item, SortColumn!))
            : source.OrderBy(item => ResolveComparableSortKey(item, SortColumn!));
    }

    private IEnumerable<TItem> ApplyPaging(IEnumerable<TItem> source)
    {
        var pageSize = RowsPerPage > 0 ? RowsPerPage : PageSize;
        if (pageSize <= 0)
        {
            return source;
        }

        var pageIndex = CurrentPageIndex >= 0 ? CurrentPageIndex : CurrentPage;
        return source.Skip(Math.Max(0, pageIndex) * pageSize).Take(pageSize);
    }

    private TItem? FindItemByKey(string rowKey)
    {
        if (string.IsNullOrWhiteSpace(RowKeyProperty))
        {
            return default;
        }

        var source = ServerData is not null || VirtualizeServerData is not null
            ? FilteredItems
            : ApplySort(ApplyFilter(Items ?? []));

        foreach (var item in source)
        {
            if (TryGetPropertyValue(item, RowKeyProperty, out var keyValue) &&
                string.Equals(keyValue?.ToString(), rowKey, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return default;
    }

    private bool TryResolveRowIndex(string rowKey, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(RowKeyProperty))
        {
            return false;
        }

        var current = 0;
        foreach (var row in ApplySort(ApplyFilter(Items ?? [])))
        {
            if (TryGetPropertyValue(row, RowKeyProperty, out var raw) &&
                string.Equals(raw?.ToString(), rowKey, StringComparison.OrdinalIgnoreCase))
            {
                index = current;
                return true;
            }

            current++;
        }

        return false;
    }

    private IReadOnlyDictionary<string, object?>? ResolveFocusedRowSnapshot()
    {
        var item = GetSelectedItem() ?? (!string.IsNullOrWhiteSpace(FocusedRowKey) ? FindItemByKey(FocusedRowKey!) : default);
        return item is null ? null : BuildRowSnapshot(item);
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildCurrentViewRowSnapshots(int maxRows)
    {
        if (maxRows <= 0)
        {
            return [];
        }

        if (ServerData is not null || VirtualizeServerData is not null)
        {
            return FilteredItems.Take(maxRows).Select(BuildRowSnapshot).ToArray();
        }

        return EffectiveItems.Take(maxRows).Select(BuildRowSnapshot).ToArray();
    }

    private static IReadOnlyDictionary<string, object?> BuildRowSnapshot(TItem row)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(TItem).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            snapshot[property.Name] = NormalizeStateValue(property.GetValue(row));
        }

        return snapshot;
    }

    private static object? NormalizeStateValue(object? value) => value switch
    {
        null => null,
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
        DateTime dt => dt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        Enum enumValue => enumValue.ToString(),
        _ => value.ToString()
    };

    private static bool MatchesFilter(TItem item, string propertyName, string @operator, object? expectedRaw)
    {
        if (!TryGetPropertyValue(item, propertyName, out var actual))
        {
            return false;
        }

        var op = @operator.Trim().ToLowerInvariant();
        var expected = NormalizeValue(expectedRaw);

        return op switch
        {
            "isnull" or "is_null" => actual is null,
            "notnull" or "not_null" => actual is not null,
            "in" or "notin" or "not_in" => EvalIn(actual, expected, op == "in"),
            _ => EvalComparison(actual, expected, op)
        };
    }

    private static bool EvalIn(object? actual, object? expected, bool wantIn)
    {
        var values = UnwrapSequence(expected).ToArray();
        if (values.Length == 0)
        {
            return false;
        }

        var contains = values.Any(value => CompareValues(actual, CoerceFor(actual, value)) == 0);
        return wantIn ? contains : !contains;
    }

    private static bool EvalComparison(object? actual, object? expected, string op)
    {
        if (actual is null)
        {
            return op is "eq" or "==" or "equals" ? expected is null
                : op is "neq" or "!=" or "notequals" && expected is not null;
        }

        var actualString = actual.ToString() ?? string.Empty;
        var expectedString = expected?.ToString() ?? string.Empty;

        if (op is "contains")
        {
            return actualString.Contains(expectedString, StringComparison.OrdinalIgnoreCase);
        }

        if (op is "startswith" or "starts_with")
        {
            return actualString.StartsWith(expectedString, StringComparison.OrdinalIgnoreCase);
        }

        if (op is "endswith" or "ends_with")
        {
            return actualString.EndsWith(expectedString, StringComparison.OrdinalIgnoreCase);
        }

        if (!TryCoerce(expected, actual.GetType(), out var coerced))
        {
            coerced = expected;
        }

        var comparison = CompareValues(actual, coerced);
        return op switch
        {
            "eq" or "==" or "equals" => comparison == 0,
            "neq" or "!=" or "notequals" => comparison != 0,
            "gt" or ">" => comparison > 0,
            "gte" or ">=" => comparison >= 0,
            "lt" or "<" => comparison < 0,
            "lte" or "<=" => comparison <= 0,
            _ => false
        };
    }

    private static IComparable? ResolveComparableSortKey(TItem item, string propertyName)
    {
        if (!TryGetPropertyValue(item, propertyName, out var value) || value is null)
        {
            return null;
        }

        return value as IComparable ?? value.ToString();
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (left is IComparable comparable && right.GetType().IsAssignableTo(left.GetType()))
        {
            return comparable.CompareTo(right);
        }

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPropertyValue(TItem item, string propertyName, out object? value)
    {
        var property = typeof(TItem)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        if (property is null)
        {
            value = null;
            return false;
        }

        value = property.GetValue(item);
        return true;
    }

    private static string[] GetFilterableColumns() =>
        typeof(TItem)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var effectiveType = underlying ?? type;
        var name = effectiveType switch
        {
            _ when effectiveType == typeof(string) => "string",
            _ when effectiveType == typeof(int) || effectiveType == typeof(long) || effectiveType == typeof(short) || effectiveType == typeof(byte) => "integer",
            _ when effectiveType == typeof(decimal) || effectiveType == typeof(double) || effectiveType == typeof(float) => "number",
            _ when effectiveType == typeof(bool) => "boolean",
            _ when effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset) => "datetime",
            _ when effectiveType == typeof(DateOnly) => "date",
            _ when effectiveType == typeof(TimeOnly) || effectiveType == typeof(TimeSpan) => "time",
            _ when effectiveType == typeof(Guid) => "guid",
            _ when effectiveType.IsEnum => "enum",
            _ => "object"
        };
        return underlying is not null ? name + "?" : name;
    }

    private static string NormalizeFilterOperator(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "equal" or "equals" or "=" => "eq",
            "notequal" or "not_equals" => "neq",
            "greater_than" or "greaterthan" => "gt",
            "less_than" or "lessthan" => "lt",
            "not_in" => "notin",
            var normalized => normalized
        };

    private static bool IsSupportedSortDirection(string direction) =>
        string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedFilterOperator(string op) =>
        op is "eq" or "==" or "equals" or "neq" or "!=" or "notequals" or
            "gt" or ">" or "gte" or ">=" or "lt" or "<" or "lte" or "<=" or
            "contains" or "startswith" or "starts_with" or "endswith" or "ends_with" or
            "in" or "notin" or "not_in" or "isnull" or "is_null" or "notnull" or "not_null";

    private static bool RequiresFilterValue(string op) =>
        op is not "isnull" and not "is_null" and not "notnull" and not "not_null";

    private static object? NormalizeValue(object? raw)
    {
        if (raw is System.Text.Json.JsonElement json)
        {
            return json.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => json.GetString(),
                System.Text.Json.JsonValueKind.Number when json.TryGetInt64(out var int64) => int64,
                System.Text.Json.JsonValueKind.Number when json.TryGetDouble(out var number) => number,
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => json.ToString()
            };
        }

        return raw;
    }

    private static bool TryCoerce(object? value, Type targetType, out object? coerced)
    {
        coerced = value;
        if (value is null)
        {
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsAssignableFrom(value.GetType()))
        {
            coerced = value;
            return true;
        }

        try
        {
            coerced = Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            coerced = value;
            return false;
        }
    }

    private static object? CoerceFor(object? actual, object? candidate)
    {
        if (actual is null || candidate is null)
        {
            return candidate;
        }

        return TryCoerce(candidate, actual.GetType(), out var coerced) ? coerced : candidate;
    }

    private static IEnumerable<object?> UnwrapSequence(object? value)
    {
        if (value is null)
        {
            return [];
        }

        if (value is string)
        {
            return [value];
        }

        if (value is IEnumerable<object?> typed)
        {
            return typed;
        }

        if (value is System.Collections.IEnumerable untyped)
        {
            var list = new List<object?>();
            foreach (var item in untyped)
            {
                list.Add(item);
            }

            return list;
        }

        return [value];
    }

    private static string MapMudFilterOperator(string op, Type? propertyType)
    {
        var effectiveType = Nullable.GetUnderlyingType(propertyType ?? typeof(string)) ?? propertyType ?? typeof(string);
        if (effectiveType == typeof(string))
        {
            return op switch
            {
                "eq" => MudBlazor.FilterOperator.String.Equal,
                "neq" => MudBlazor.FilterOperator.String.NotEqual,
                "contains" => MudBlazor.FilterOperator.String.Contains,
                "startswith" => MudBlazor.FilterOperator.String.StartsWith,
                "endswith" => MudBlazor.FilterOperator.String.EndsWith,
                "isnull" => MudBlazor.FilterOperator.String.Empty,
                "notnull" => MudBlazor.FilterOperator.String.NotEmpty,
                _ => MudBlazor.FilterOperator.String.Contains
            };
        }

        if (effectiveType == typeof(bool))
        {
            return MudBlazor.FilterOperator.Boolean.Is;
        }

        if (effectiveType.IsEnum)
        {
            return op == "neq" ? MudBlazor.FilterOperator.Enum.IsNot : MudBlazor.FilterOperator.Enum.Is;
        }

        if (effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset))
        {
            return op switch
            {
                "eq" => MudBlazor.FilterOperator.DateTime.Is,
                "neq" => MudBlazor.FilterOperator.DateTime.IsNot,
                "gt" => MudBlazor.FilterOperator.DateTime.After,
                "gte" => MudBlazor.FilterOperator.DateTime.OnOrAfter,
                "lt" => MudBlazor.FilterOperator.DateTime.Before,
                "lte" => MudBlazor.FilterOperator.DateTime.OnOrBefore,
                "isnull" => MudBlazor.FilterOperator.DateTime.Empty,
                "notnull" => MudBlazor.FilterOperator.DateTime.NotEmpty,
                _ => MudBlazor.FilterOperator.DateTime.Is
            };
        }

        if (effectiveType == typeof(DateOnly))
        {
            return op switch
            {
                "eq" => MudBlazor.FilterOperator.DateOnly.Is,
                "neq" => MudBlazor.FilterOperator.DateOnly.IsNot,
                "gt" => MudBlazor.FilterOperator.DateOnly.After,
                "gte" => MudBlazor.FilterOperator.DateOnly.OnOrAfter,
                "lt" => MudBlazor.FilterOperator.DateOnly.Before,
                "lte" => MudBlazor.FilterOperator.DateOnly.OnOrBefore,
                "isnull" => MudBlazor.FilterOperator.DateOnly.Empty,
                "notnull" => MudBlazor.FilterOperator.DateOnly.NotEmpty,
                _ => MudBlazor.FilterOperator.DateOnly.Is
            };
        }

        return op switch
        {
            "eq" => MudBlazor.FilterOperator.Number.Equal,
            "neq" => MudBlazor.FilterOperator.Number.NotEqual,
            "gt" => MudBlazor.FilterOperator.Number.GreaterThan,
            "gte" => MudBlazor.FilterOperator.Number.GreaterThanOrEqual,
            "lt" => MudBlazor.FilterOperator.Number.LessThan,
            "lte" => MudBlazor.FilterOperator.Number.LessThanOrEqual,
            "isnull" => MudBlazor.FilterOperator.Number.Empty,
            "notnull" => MudBlazor.FilterOperator.Number.NotEmpty,
            _ => MudBlazor.FilterOperator.Number.Equal
        };
    }

    private async Task<ActionResult> RefreshAsync(ActionResult result)
    {
        await RequestComponentRefreshAsync();
        return result;
    }

    private Task SetCurrentPageSafelyAsync(int pageIndex)
    {
        try
        {
            CurrentPage = pageIndex;
            return Task.CompletedTask;
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }

    private async Task SetRowsPerPageSafelyAsync(int size, bool resetPage)
    {
        try
        {
            await SetRowsPerPageAsync(size, resetPage);
        }
        catch (InvalidOperationException)
        {
            // MudDataGrid assigns paging state before requesting a render.
        }
    }

    private async Task SetSelectedItemSafelyAsync(TItem item)
    {
        try
        {
            await SetSelectedItemAsync(item);
        }
        catch (InvalidOperationException)
        {
            // MudDataGrid updates selection state before requesting a render.
        }
    }

    private Task RequestComponentRefreshAsync()
    {
        try
        {
            return InvokeAsync(StateHasChanged);
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record ColumnResolution(string? PropertyName, Column<TItem>? Column, Type? PropertyType);

    private TItem? GetSelectedItem() => MudPrivateParameterStateAccessor.GetValue<TItem>(this, "_selectedItemState");
}
