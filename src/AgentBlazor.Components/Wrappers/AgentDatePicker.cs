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

public class AgentDatePicker : MudDatePicker, IAgentControllable
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
    public DateTime? Value { get; set; }

    [Parameter]
    public EventCallback<DateTime?> ValueChanged { get; set; }

    public string ComponentType => "DatePicker";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private EventCallback<DateTime?> _externalDateChanged;
    private EventCallback<DateTime?> _wrappedDateChanged;

    [AgentReadable("Currently selected date (yyyy-MM-dd)")]
    public string? CurrentDate => FormatDate(Date ?? Value);

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
        EnsureDateChangedBridge();
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
        EnsureDateChangedBridge();
        if (!Nullable.Equals(Value, Date))
        {
            Date = Value;
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
        ["value"] = FormatDate(Date ?? Value),
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

    [AgentAction("Set selected date from an ISO date or natural-language date", ActionId = "set_date")]
    public async Task<ActionResult> SetDateValue(
        [AgentParam("Date value (for example 2026-03-02 or March 2 2026)", Required = true)] string date)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot set date while date picker is disabled or read-only.");
        }

        EnsureDateChangedBridge();

        if (!TryParseDate(date, out var parsed))
        {
            return ActionResult.NeedsClarification($"Could not parse '{date}' as a date.");
        }

        if (!IsWithinRange(parsed))
        {
            return ActionResult.Failure("Date is outside the allowed range.");
        }

        await SetDateSafelyAsync(parsed.Date);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied($"Set date to {FormatDate(Date ?? Value)}.");
    }

    [AgentAction("Clear selected date", ActionId = "clear")]
    public async Task<ActionResult> ClearDate()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot clear date while date picker is disabled or read-only.");
        }

        EnsureDateChangedBridge();
        await ClearDateSafelyAsync();
        await RequestComponentRefreshAsync();
        return ActionResult.Applied("Cleared date.");
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

    private void EnsureDateChangedBridge()
    {
        if (!_wrappedDateChanged.HasDelegate || !DateChanged.Equals(_wrappedDateChanged))
        {
            _externalDateChanged = DateChanged;
        }

        _wrappedDateChanged = EventCallback.Factory.Create<DateTime?>(this, HandleDateChangedAsync);
        DateChanged = _wrappedDateChanged;
    }

    private async Task HandleDateChangedAsync(DateTime? date)
    {
        Value = date;

        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(date);
        }

        if (_externalDateChanged.HasDelegate && !_externalDateChanged.Equals(_wrappedDateChanged))
        {
            await _externalDateChanged.InvokeAsync(date);
        }
    }

    private async Task SetDateSafelyAsync(DateTime? date)
    {
        try
        {
            await SetDateAsync(date, true);
        }
        catch (Exception)
        {
            Date = date;
            _value = date;
            await HandleDateChangedAsync(date);
        }
    }

    private async Task ClearDateSafelyAsync()
    {
        try
        {
            await ClearAsync();
        }
        catch (Exception)
        {
            Date = null;
            _value = null;
            await HandleDateChangedAsync(null);
        }
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "date-picker";
        UserAttributes["data-ab-agentid"] = AgentId;
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
