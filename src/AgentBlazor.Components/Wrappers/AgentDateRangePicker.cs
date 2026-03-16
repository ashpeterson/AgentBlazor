using System.Reflection;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using RuntimeComponentState = AgentBlazor.Core.Runtime.Components.ComponentState;

namespace AgentBlazor.Components;

public class AgentDateRangePicker : MudDateRangePicker, IAgentControllable
{
    private static readonly FieldInfo? DateRangeField =
        typeof(MudDateRangePicker).GetField("_dateRange", BindingFlags.Instance | BindingFlags.NonPublic);

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
    public DateTime? StartDate { get; set; }

    [Parameter]
    public EventCallback<DateTime?> StartDateChanged { get; set; }

    [Parameter]
    public DateTime? EndDate { get; set; }

    [Parameter]
    public EventCallback<DateTime?> EndDateChanged { get; set; }

    public string ComponentType => "DateRangePicker";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private EventCallback<DateRange?> _externalDateRangeChanged;
    private EventCallback<DateRange?> _wrappedDateRangeChanged;

    [AgentReadable("Current range start date (yyyy-MM-dd)")]
    public string? CurrentStartDate => FormatDate(DateRange?.Start ?? StartDate);

    [AgentReadable("Current range end date (yyyy-MM-dd)")]
    public string? CurrentEndDate => FormatDate(DateRange?.End ?? EndDate);

    [AgentReadable("Minimum allowed date (yyyy-MM-dd)")]
    public string? MinimumDate => FormatDate(MinDate);

    [AgentReadable("Maximum allowed date (yyyy-MM-dd)")]
    public string? MaximumDate => FormatDate(MaxDate);

    [AgentReadable("Whether the picker is open")]
    public bool IsOpen => Open;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _logger ??= LoggerFactory?.CreateLogger(GetType());
        _runtimeSupport ??= CreateRuntimeSupport();
        _runtimeSupport.OnInitialized();
        EnsureDateRangeChangedBridge();
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
        EnsureDateRangeChangedBridge();

        if (ShouldApplyAliasRange())
        {
            var aliasRange = CreateDateRange(StartDate, EndDate);
            if (!DateRangeEquals(DateRange, aliasRange))
            {
                DateRange = aliasRange;
            }
        }
        else
        {
            StartDate = DateRange?.Start;
            EndDate = DateRange?.End;
        }

        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _runtimeSupport?.Dispose();
        await base.DisposeAsyncCore();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState() => new()
    {
        ["startDate"] = FormatDate(DateRange?.Start ?? StartDate),
        ["endDate"] = FormatDate(DateRange?.End ?? EndDate),
        ["minDate"] = FormatDate(MinDate),
        ["maxDate"] = FormatDate(MaxDate),
        ["disabled"] = Disabled,
        ["readOnly"] = ReadOnly,
        ["isOpen"] = Open
    };

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

    [AgentAction("Set start and end dates for the range", ActionId = "set_range")]
    public async Task<ActionResult> SetRange(
        [AgentParam("Range start date (for example 2026-03-01)", Required = true)] string startDate,
        [AgentParam("Range end date (for example 2026-03-31)", Required = true)] string endDate)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot set range while date range picker is disabled or read-only.");
        }

        EnsureDateRangeChangedBridge();

        if (!TryParseDate(startDate, out var parsedStart))
        {
            return ActionResult.NeedsClarification($"Could not parse start date '{startDate}'.");
        }

        if (!TryParseDate(endDate, out var parsedEnd))
        {
            return ActionResult.NeedsClarification($"Could not parse end date '{endDate}'.");
        }

        if (parsedStart.Date > parsedEnd.Date)
        {
            return ActionResult.Failure("Start date cannot be after end date.");
        }

        if (!IsWithinRange(parsedStart) || !IsWithinRange(parsedEnd))
        {
            return ActionResult.Failure("Date range is outside the allowed bounds.");
        }

        await SetRangeSafelyAsync(new DateRange(parsedStart.Date, parsedEnd.Date));
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Set range to {FormatDate(DateRange?.Start ?? StartDate)} through {FormatDate(DateRange?.End ?? EndDate)}.");
    }

    [AgentAction("Clear selected date range", ActionId = "clear")]
    public async Task<ActionResult> ClearRange()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot clear range while date range picker is disabled or read-only.");
        }

        EnsureDateRangeChangedBridge();
        await ClearRangeSafelyAsync();
        await RequestComponentRefreshAsync();
        return ActionResult.Applied("Cleared date range.");
    }

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

    private void EnsureDateRangeChangedBridge()
    {
        if (!_wrappedDateRangeChanged.HasDelegate || !DateRangeChanged.Equals(_wrappedDateRangeChanged))
        {
            _externalDateRangeChanged = DateRangeChanged;
        }

        _wrappedDateRangeChanged = EventCallback.Factory.Create<DateRange?>(this, HandleDateRangeChangedAsync);
        DateRangeChanged = _wrappedDateRangeChanged;
    }

    private async Task HandleDateRangeChangedAsync(DateRange? range)
    {
        StartDate = range?.Start;
        EndDate = range?.End;

        if (StartDateChanged.HasDelegate)
        {
            await StartDateChanged.InvokeAsync(StartDate);
        }

        if (EndDateChanged.HasDelegate)
        {
            await EndDateChanged.InvokeAsync(EndDate);
        }

        if (_externalDateRangeChanged.HasDelegate && !_externalDateRangeChanged.Equals(_wrappedDateRangeChanged))
        {
            await _externalDateRangeChanged.InvokeAsync(range);
        }
    }

    private async Task SetRangeSafelyAsync(DateRange? range)
    {
        try
        {
            await SetDateRangeAsync(range, true);
        }
        catch (InvalidOperationException)
        {
            ApplyHeadlessRange(range);
            await HandleDateRangeChangedAsync(range);
        }
    }

    private async Task ClearRangeSafelyAsync()
    {
        try
        {
            await ClearAsync(false);
        }
        catch (InvalidOperationException)
        {
            ApplyHeadlessRange(null);
            await HandleDateRangeChangedAsync(null);
        }
    }

    private void ApplyHeadlessRange(DateRange? range)
    {
        DateRangeField?.SetValue(this, range);
        _value = range?.End;
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "date-range-picker";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private bool ShouldApplyAliasRange()
    {
        return StartDateChanged.HasDelegate
            || EndDateChanged.HasDelegate
            || StartDate is not null
            || EndDate is not null;
    }

    private bool IsWithinRange(DateTime value)
    {
        var date = value.Date;
        if (MinDate is { } min && date < min.Date)
        {
            return false;
        }

        if (MaxDate is { } max && date > max.Date)
        {
            return false;
        }

        return true;
    }

    private static DateRange? CreateDateRange(DateTime? startDate, DateTime? endDate)
    {
        if (startDate is null && endDate is null)
        {
            return null;
        }

        return new DateRange(startDate?.Date, endDate?.Date);
    }

    private static bool DateRangeEquals(DateRange? left, DateRange? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return Nullable.Equals(left.Start?.Date, right.Start?.Date)
            && Nullable.Equals(left.End?.Date, right.End?.Date);
    }

    private static string? FormatDate(DateTime? value) =>
        value?.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static bool TryParseDate(string raw, out DateTime date)
    {
        if (DateOnly.TryParseExact(
                raw,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dateOnly))
        {
            date = dateOnly.ToDateTime(TimeOnly.MinValue);
            return true;
        }

        if (DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsedInvariant))
        {
            date = parsedInvariant.Date;
            return true;
        }

        if (DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsedCurrent))
        {
            date = parsedCurrent.Date;
            return true;
        }

        date = default;
        return false;
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
}
