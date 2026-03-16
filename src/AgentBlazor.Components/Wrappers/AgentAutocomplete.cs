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

[CascadingTypeParameter(nameof(T))]
public class AgentAutocomplete<T> : MudAutocomplete<T>, IAgentControllable
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
    public string? Query { get; set; }

    [Parameter]
    public EventCallback<string?> QueryChanged { get; set; }

    [Parameter]
    public IEnumerable<T?>? Options { get; set; }

    public string ComponentType => "Autocomplete";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private bool _hasRendered;
    private Func<string?, CancellationToken, Task<IEnumerable<T>>?>? _generatedSearchFunc;
    private EventCallback<string> _externalTextChanged;
    private EventCallback<string> _wrappedTextChanged;
    private string? _lastSyncedQuery;

    [AgentReadable("Current query text")]
    public string? CurrentQuery => Query ?? Text;

    [AgentReadable("Currently selected value")]
    public T? SelectedValue => Value;

    [AgentReadable("Available option values")]
    public string[] AvailableOptions => GetAvailableOptionTexts();

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
        EnsureGeneratedSearchFunc();
        EnsureTextChangedBridge();

        if (Query is not null && !string.Equals(Query, Text, StringComparison.Ordinal))
        {
            Text = Query;
        }

        base.OnParametersSet();
        EnsureAgentUserAttributes();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            _hasRendered = true;
        }

        await SyncQueryAliasAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _runtimeSupport?.Dispose();
        await base.DisposeAsyncCore();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState() => new()
    {
        ["query"] = Query ?? Text,
        ["selectedValue"] = ConvertOptionToText(Value),
        ["options"] = GetAvailableOptionTexts(),
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

    [AgentAction("Set autocomplete query text", ActionId = "set_query")]
    public async Task<ActionResult> SetQueryText(
        [AgentParam("Query text", Required = true)] string query)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot change autocomplete query while it is disabled or read-only.");
        }

        await SetTextSafelyAsync(query);

        if (TryResolveOptionValue(query, out var resolved))
        {
            await SelectOptionSafelyAsync(resolved);
        }
        else
        {
            await ClearValueSafelyAsync();
            await OpenMenuSafelyAsync();
        }

        return ActionResult.Applied($"Set query to '{query}'.");
    }

    [AgentAction("Select one autocomplete option by value", ActionId = "select_option")]
    public async Task<ActionResult> SelectAutocompleteOption(
        [AgentParam("Option value to select", Required = true)] string value)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot select an autocomplete option while disabled or read-only.");
        }

        if (!TryResolveOptionValue(value, out var resolved))
        {
            return ActionResult.NeedsClarification($"Option '{value}' is not available.");
        }

        await SelectOptionSafelyAsync(resolved);
        return ActionResult.Applied($"Selected '{value}'.");
    }

    [AgentAction("Clear query text and selected value", ActionId = "clear")]
    public async Task<ActionResult> ClearAutocomplete()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot clear autocomplete while disabled or read-only.");
        }

        await ClearAutocompleteSafelyAsync();
        return ActionResult.Applied("Cleared autocomplete input.");
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

    private void EnsureGeneratedSearchFunc()
    {
        if (Options is not null && (SearchFunc is null || ReferenceEquals(SearchFunc, _generatedSearchFunc)))
        {
            _generatedSearchFunc ??= SearchGeneratedOptionsAsync;
            SearchFunc = _generatedSearchFunc;
            return;
        }

        if (Options is null && ReferenceEquals(SearchFunc, _generatedSearchFunc))
        {
            SearchFunc = null;
        }
    }

    private void EnsureTextChangedBridge()
    {
        if (!_wrappedTextChanged.HasDelegate || !TextChanged.Equals(_wrappedTextChanged))
        {
            _externalTextChanged = TextChanged;
        }

        _wrappedTextChanged = EventCallback.Factory.Create<string>(this, HandleTextChangedAsync);
        TextChanged = _wrappedTextChanged;
    }

    private async Task HandleTextChangedAsync(string text)
    {
        Query = text;
        await QueryChanged.InvokeAsync(text);

        if (_externalTextChanged.HasDelegate && !_externalTextChanged.Equals(_wrappedTextChanged))
        {
            await _externalTextChanged.InvokeAsync(text);
        }
    }

    private async Task SyncQueryAliasAsync()
    {
        var current = Text;
        if (string.Equals(_lastSyncedQuery, current, StringComparison.Ordinal))
        {
            return;
        }

        _lastSyncedQuery = Query = current;
        await QueryChanged.InvokeAsync(Query);
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "autocomplete";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private string[] GetAvailableOptionTexts() =>
        Options?
            .Select(ConvertOptionToText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(static text => text!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private Task<IEnumerable<T>>? SearchGeneratedOptionsAsync(string? query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<T?> source = Options ?? [];
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(option =>
                ConvertOptionToText(option)?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
        }

        return Task.FromResult(source
            .Where(static option => option is not null)
            .Select(static option => option!)
            .AsEnumerable());
    }

    private bool TryResolveOptionValue(string input, out T? resolved)
    {
        if (Options is not null)
        {
            foreach (var option in Options)
            {
                if (OptionMatches(option, input))
                {
                    resolved = option;
                    return true;
                }
            }
        }

        return TryConvertFromString(input, out resolved);
    }

    private bool OptionMatches(T? option, string input)
    {
        var text = ConvertOptionToText(option);
        if (string.Equals(text, input, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return option is not null &&
               string.Equals(option.ToString(), input, StringComparison.OrdinalIgnoreCase);
    }

    private string? ConvertOptionToText(T? option)
    {
        if (option is null)
        {
            return null;
        }

        return ToStringFunc?.Invoke(option) ?? option.ToString();
    }

    private static bool TryConvertFromString(string input, out T? converted)
    {
        try
        {
            var destinationType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (destinationType == typeof(string))
            {
                converted = (T?)(object?)input;
                return true;
            }

            if (destinationType.IsEnum)
            {
                converted = (T?)Enum.Parse(destinationType, input, ignoreCase: true);
                return true;
            }

            if (destinationType == typeof(Guid))
            {
                converted = (T?)(object)Guid.Parse(input);
                return true;
            }

            converted = (T?)Convert.ChangeType(input, destinationType, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            converted = default;
            return false;
        }
    }

    private async Task SetTextSafelyAsync(string? text)
    {
        Text = text;

        if (text is null)
        {
            Query = null;
            await QueryChanged.InvokeAsync(null);
            return;
        }

        try
        {
            await TextChanged.InvokeAsync(text);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task OpenMenuSafelyAsync()
    {
        try
        {
            await OpenMenuAsync();
        }
        catch (InvalidOperationException)
        {
            Open = true;
            await OpenChanged.InvokeAsync(true);
        }
        catch (Exception) when (!_hasRendered)
        {
            Open = true;
            await OpenChanged.InvokeAsync(true);
        }
    }

    private async Task SelectOptionSafelyAsync(T? value)
    {
        try
        {
            await SelectOptionAsync(value!);
        }
        catch (InvalidOperationException)
        {
            Value = value;
            await ValueChanged.InvokeAsync(value);
            await SetTextSafelyAsync(ConvertOptionToText(value));
        }
        catch (Exception) when (!_hasRendered)
        {
            Value = value;
            await ValueChanged.InvokeAsync(value);
            await SetTextSafelyAsync(ConvertOptionToText(value));
        }
    }

    private async Task ClearValueSafelyAsync()
    {
        Value = default;
        await ValueChanged.InvokeAsync(Value);
    }

    private async Task ClearAutocompleteSafelyAsync()
    {
        try
        {
            await ClearAsync();
        }
        catch (InvalidOperationException)
        {
            await SetTextSafelyAsync(null);
            await ClearValueSafelyAsync();
        }
        catch (Exception) when (!_hasRendered)
        {
            await SetTextSafelyAsync(null);
            await ClearValueSafelyAsync();
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
}
