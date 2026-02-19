using AgentBlazor.Components;
using AgentBlazor.Runtime;

namespace AgentBlazor.Demo.Services;

public sealed class AgentDataGridDemoState
{
    private const int PageSize = 5;
    private readonly object _gate = new();
    private readonly List<SupplierRow> _allRows =
    [
        new("SUP-001", "Alpine Components", "EMEA", 82, new DateTime(2025, 12, 04)),
        new("SUP-002", "Beacon Industrial", "NA", 71, new DateTime(2026, 01, 12)),
        new("SUP-003", "Crestline Textiles", "APAC", 49, new DateTime(2025, 11, 21)),
        new("SUP-004", "Deltawave Metals", "EMEA", 67, new DateTime(2026, 02, 06)),
        new("SUP-005", "Everfield Logistics", "NA", 38, new DateTime(2025, 10, 19)),
        new("SUP-006", "Farside Plastics", "APAC", 91, new DateTime(2026, 02, 10)),
        new("SUP-007", "Grayson Foods", "EMEA", 55, new DateTime(2025, 09, 15)),
        new("SUP-008", "Harbor Electric", "LATAM", 62, new DateTime(2025, 12, 28)),
        new("SUP-009", "Ionis Chemical", "NA", 76, new DateTime(2026, 01, 31)),
        new("SUP-010", "Juniper BioWorks", "APAC", 44, new DateTime(2025, 08, 07)),
        new("SUP-011", "Keystone Minerals", "EMEA", 88, new DateTime(2026, 02, 02)),
        new("SUP-012", "Lighthouse MedTech", "NA", 59, new DateTime(2025, 11, 03))
    ];
    private readonly Queue<string> _recentEvents = new();
    private int? _minRiskScore;
    private int? _maxRiskScore;
    private string _filterSummary = "All suppliers";
    private bool _sortDescending = true;
    private int _currentPage = 1;
    private string? _focusedSupplierId;

    public event Action? Changed;

    public AgentDataGridDemoSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return BuildSnapshotUnsafe();
            }
        }
    }

    public (bool Succeeded, string Message) ApplyAction(
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        bool succeeded;
        string message;
        lock (_gate)
        {
            (succeeded, message) = actionId.Trim().ToLowerInvariant() switch
            {
                AgentComponentV1CapabilityProfile.DataGridFilterActionId => ApplyRiskFilterUnsafe(arguments),
                AgentComponentV1CapabilityProfile.DataGridClearFiltersActionId => ClearFiltersUnsafe(),
                AgentComponentV1CapabilityProfile.DataGridSortActionId => ApplyRiskSortUnsafe(arguments),
                AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId => FocusSupplierUnsafe(arguments),
                AgentComponentV1CapabilityProfile.DataGridSelectRowActionId => FocusSupplierUnsafe(arguments),
                AgentComponentV1CapabilityProfile.DataGridGoToPageActionId => SetPageUnsafe(arguments),
                AgentComponentV1CapabilityProfile.DataGridSetPageActionId => SetPageUnsafe(arguments),
                _ => (
                    false,
                    $"AgentDataGrid action '{actionId}' is not implemented by demo state.")
            };

            EnqueueEventUnsafe($"{DateTime.UtcNow:HH:mm:ss} - {message}");
        }

        Changed?.Invoke();
        return (succeeded, message);
    }

    private (bool Succeeded, string Message) ApplyRiskFilterUnsafe(IReadOnlyDictionary<string, object?>? arguments)
    {
        var column = TryGetString(arguments, "column");
        var op = TryGetString(arguments, "operator")?.Trim().ToLowerInvariant();
        var threshold = TryGetInt(arguments, "value");

        if (string.Equals(column, "RiskScore", StringComparison.OrdinalIgnoreCase) &&
            threshold is not null &&
            !string.IsNullOrWhiteSpace(op))
        {
            switch (op)
            {
                case ">":
                case ">=":
                case "gt":
                case "gte":
                    _minRiskScore = threshold;
                    _maxRiskScore = null;
                    _filterSummary = $"Risk >= {threshold.Value}";
                    _currentPage = 1;
                    return (true, $"Applied risk filter (risk score >= {threshold.Value}).");

                case "<":
                case "<=":
                case "lt":
                case "lte":
                    _maxRiskScore = threshold;
                    _minRiskScore = null;
                    _filterSummary = $"Risk <= {threshold.Value}";
                    _currentPage = 1;
                    return (true, $"Applied risk filter (risk score <= {threshold.Value}).");

                case "eq":
                case "==":
                case "=":
                    _minRiskScore = threshold;
                    _maxRiskScore = threshold;
                    _filterSummary = $"Risk = {threshold.Value}";
                    _currentPage = 1;
                    return (true, $"Applied risk filter (risk score = {threshold.Value}).");
            }
        }

        // Demo fallback when tool arguments are not supplied.
        if (_minRiskScore is 70 && _maxRiskScore is null)
        {
            _minRiskScore = null;
            _maxRiskScore = null;
            _filterSummary = "All suppliers";
            _currentPage = 1;
            return (true, "Cleared high-risk filter.");
        }

        _minRiskScore = 70;
        _maxRiskScore = null;
        _filterSummary = "Risk >= 70";
        _currentPage = 1;
        return (true, "Applied high-risk filter (risk score >= 70).");
    }

    private (bool Succeeded, string Message) ApplyRiskSortUnsafe(IReadOnlyDictionary<string, object?>? arguments)
    {
        var direction = TryGetString(arguments, "direction")?.Trim().ToLowerInvariant();
        if (direction is "asc" or "ascending")
        {
            _sortDescending = false;
        }
        else if (direction is "desc" or "descending")
        {
            _sortDescending = true;
        }
        else
        {
            _sortDescending = !_sortDescending;
        }

        _currentPage = 1;
        var message = _sortDescending
            ? "Sorted by risk score descending."
            : "Sorted by risk score ascending.";
        return (true, message);
    }

    private (bool Succeeded, string Message) ClearFiltersUnsafe()
    {
        _minRiskScore = null;
        _maxRiskScore = null;
        _filterSummary = "All suppliers";
        _currentPage = 1;
        return (true, "Cleared all filters.");
    }

    private (bool Succeeded, string Message) FocusSupplierUnsafe(IReadOnlyDictionary<string, object?>? arguments)
    {
        var requestedRowKey = TryGetString(arguments, "rowKey");
        if (!string.IsNullOrWhiteSpace(requestedRowKey))
        {
            var requested = _allRows.FirstOrDefault(row =>
                string.Equals(row.SupplierId, requestedRowKey, StringComparison.OrdinalIgnoreCase));
            if (requested is not null)
            {
                _focusedSupplierId = requested.SupplierId;
                var orderedRows = BuildOrderedRowsUnsafe();
                var index = orderedRows.FindIndex(row => row.SupplierId == requested.SupplierId);
                if (index >= 0)
                {
                    _currentPage = (index / PageSize) + 1;
                }

                return (true, $"Focused supplier row: {requested.SupplierId} ({requested.SupplierName}).");
            }
        }

        var ordered = BuildOrderedRowsUnsafe();
        var target = ordered.FirstOrDefault();
        if (target is null)
        {
            _focusedSupplierId = null;
            return (false, "No supplier rows are available to focus.");
        }

        _focusedSupplierId = target.SupplierId;
        return (true, $"Focused supplier row: {target.SupplierId} ({target.SupplierName}).");
    }

    private (bool Succeeded, string Message) SetPageUnsafe(IReadOnlyDictionary<string, object?>? arguments)
    {
        var ordered = BuildOrderedRowsUnsafe();
        var totalPages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)PageSize));

        var requested = TryGetInt(arguments, "pageIndex") ?? TryGetInt(arguments, "page");
        if (requested is not null)
        {
            _currentPage = Math.Clamp(requested.Value + 1, 1, totalPages);
            return (true, $"Set page to {_currentPage} of {totalPages}.");
        }

        _currentPage = _currentPage >= totalPages ? 1 : _currentPage + 1;
        return (true, $"Set page to {_currentPage} of {totalPages}.");
    }

    private AgentDataGridDemoSnapshot BuildSnapshotUnsafe()
    {
        var ordered = BuildOrderedRowsUnsafe();
        var totalPages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)PageSize));
        if (_currentPage > totalPages)
        {
            _currentPage = totalPages;
        }

        var visibleRows = ordered
            .Skip((_currentPage - 1) * PageSize)
            .Take(PageSize)
            .ToArray();

        return new AgentDataGridDemoSnapshot(
            FilterSummary: _filterSummary,
            SortDescending: _sortDescending,
            CurrentPage: _currentPage,
            TotalPages: totalPages,
            FocusedSupplierId: _focusedSupplierId,
            VisibleRows: visibleRows,
            RecentEvents: _recentEvents.ToArray());
    }

    private List<SupplierRow> BuildOrderedRowsUnsafe()
    {
        IEnumerable<SupplierRow> query = _allRows;
        if (_minRiskScore is not null)
        {
            var min = _minRiskScore.Value;
            query = query.Where(row => row.RiskScore >= min);
        }

        if (_maxRiskScore is not null)
        {
            var max = _maxRiskScore.Value;
            query = query.Where(row => row.RiskScore <= max);
        }

        query = _sortDescending
            ? query.OrderByDescending(static row => row.RiskScore).ThenBy(static row => row.SupplierId, StringComparer.Ordinal)
            : query.OrderBy(static row => row.RiskScore).ThenBy(static row => row.SupplierId, StringComparer.Ordinal);

        return query.ToList();
    }

    private void EnqueueEventUnsafe(string message)
    {
        _recentEvents.Enqueue(message);
        while (_recentEvents.Count > 8)
        {
            _ = _recentEvents.Dequeue();
        }
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?>? arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e => e.GetString(),
            System.Text.Json.JsonElement e => e.ToString(),
            _ => value.ToString()
        };
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, object?>? arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } e when e.TryGetInt32(out var parsed) => parsed,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e when int.TryParse(e.GetString(), out var parsed) => parsed,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }
}

public sealed class DemoDataGridActionExecutor(
    IAgentComponentRegistry componentRegistry,
    IAgentNavigationIntentService navigationIntentService,
    AgentDataGridDemoState state) : IDataGridActionExecutor
{
    public async Task<ComponentActionExecutionResult> ExecuteAsync(
        DataGridActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (handled, componentResult) = await RegisteredComponentExecutorBridge.TryExecuteAsync(
            componentRegistry,
            expectedComponentType: "DataGrid",
            componentId: AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            actionId: request.ActionId,
            arguments: request.Arguments,
            cancellationToken);

        if (!handled)
        {
            // No DataGrid is registered yet — we're mid-navigation.
            // Store the action so the destination page's AgentDataGrid picks it up on init.
            navigationIntentService.Enqueue("DataGrid", AgentAction.Create(request.ActionId, request.Arguments));
        }

        var (succeeded, message) = state.ApplyAction(request.ActionId, request.Arguments);
        var stateResult = new ComponentActionExecutionResult(
            ComponentId: AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            ActionId: request.ActionId,
            Succeeded: succeeded,
            Message: message);

        return handled ? componentResult : stateResult;
    }
}

public sealed record AgentDataGridDemoSnapshot(
    string FilterSummary,
    bool SortDescending,
    int CurrentPage,
    int TotalPages,
    string? FocusedSupplierId,
    IReadOnlyList<SupplierRow> VisibleRows,
    IReadOnlyList<string> RecentEvents);

public sealed record SupplierRow(
    string SupplierId,
    string SupplierName,
    string Region,
    int RiskScore,
    DateTime LastAuditDate);
