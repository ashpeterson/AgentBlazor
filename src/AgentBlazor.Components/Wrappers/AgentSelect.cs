using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;
using MudBlazor;
using RuntimeComponentState = AgentBlazor.Core.Runtime.Components.ComponentState;

namespace AgentBlazor.Components;

[CascadingTypeParameter(nameof(T))]
public class AgentSelect<T> : MudSelect<T>, IAgentControllable
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
    public IEnumerable<T?>? Options { get; set; }

    [Parameter]
    public bool AllowEmptyOption { get; set; } = true;

    [Parameter]
    public string EmptyOptionText { get; set; } = "-- select --";

    public string ComponentType => "Select";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private RenderFragment? _generatedChildContent;
    private bool _hasRendered;
    private bool _isOpen;

    [AgentReadable("Currently selected value")]
    public T? SelectedValue => Value;

    [AgentReadable("Whether the select is currently disabled")]
    public bool IsDisabled => Disabled;

    [AgentReadable("Whether the select is currently read-only")]
    public bool IsReadOnly => ReadOnly;

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
        base.OnParametersSet();
        EnsureGeneratedChildContent();
        EnsureAgentUserAttributes();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender)
        {
            _hasRendered = true;
        }
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _runtimeSupport?.Dispose();
        await base.DisposeAsyncCore();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState() => new()
    {
        ["value"] = ConvertOptionToText(Value),
        ["options"] = GetAvailableOptionTexts(),
        ["disabled"] = Disabled,
        ["readOnly"] = ReadOnly,
        ["isOpen"] = _isOpen
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

    [AgentAction("Open the select list", ActionId = "open")]
    public async Task<ActionResult> OpenSelect()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot open select while it is disabled or read-only.");
        }

        await OpenMenuSafelyAsync();
        return ActionResult.Applied("Opened select list.");
    }

    [AgentAction("Close the select list", ActionId = "close")]
    public async Task<ActionResult> CloseSelect()
    {
        await CloseMenuSafelyAsync();
        return ActionResult.Applied("Closed select list.");
    }

    [AgentAction("Set selected option value", ActionId = "set_value")]
    public async Task<ActionResult> SetSelectedValue(
        [AgentParam("Value to select", Required = true)] string value)
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot change select value while it is disabled or read-only.");
        }

        if (!TryResolveOptionValue(value, out var resolved))
        {
            return ActionResult.NeedsClarification($"Option '{value}' is not available.");
        }

        await SelectOptionSafelyAsync(resolved);
        return ActionResult.Applied($"Selected '{value}'.");
    }

    [AgentAction("Clear current selection", ActionId = "clear")]
    public new async Task<ActionResult> Clear()
    {
        if (Disabled || ReadOnly)
        {
            return ActionResult.Failure("Cannot clear select while it is disabled or read-only.");
        }

        await ClearSafelyAsync();
        return ActionResult.Applied("Cleared selection.");
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

    private void EnsureGeneratedChildContent()
    {
        if (Options is not null && (ChildContent is null || ReferenceEquals(ChildContent, _generatedChildContent)))
        {
            _generatedChildContent = BuildOptionsContent();
            ChildContent = _generatedChildContent;
            return;
        }

        if (Options is null && ReferenceEquals(ChildContent, _generatedChildContent))
        {
            ChildContent = null;
            _generatedChildContent = null;
        }
    }

    private RenderFragment BuildOptionsContent()
    {
        return builder =>
        {
            if (AllowEmptyOption)
            {
                AddGeneratedOption(builder, default, EmptyOptionText);
            }

            if (Options is null)
            {
                return;
            }

            foreach (var option in Options)
            {
                var text = ConvertOptionToText(option);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                AddGeneratedOption(builder, option, text);
            }
        };
    }

    private static void AddGeneratedOption(RenderTreeBuilder builder, T? value, string text)
    {
        builder.OpenComponent<MudSelectItem<T>>(0);
        builder.AddAttribute(1, nameof(MudSelectItem<T>.Value), value);
        builder.AddAttribute(2, nameof(MudSelectItem<T>.ChildContent), (RenderFragment)(contentBuilder =>
        {
            contentBuilder.AddContent(0, text);
        }));
        builder.CloseComponent();
    }

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        UserAttributes["data-ab-type"] = "select";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private string[] GetAvailableOptionTexts()
    {
        if (Items.Count > 0)
        {
            return Items
                .Select(static item => item.Value)
                .Select(ConvertOptionToText)
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Select(static text => text!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Options?
            .Select(ConvertOptionToText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Select(static text => text!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private bool TryResolveOptionValue(string input, out T? resolved)
    {
        if (TryResolveFromItems(input, out resolved) || TryResolveFromOptions(input, out resolved))
        {
            return true;
        }

        return TryConvertFromString(input, out resolved);
    }

    private bool TryResolveFromItems(string input, out T? resolved)
    {
        foreach (var item in Items)
        {
            if (OptionMatches(item.Value, input))
            {
                resolved = item.Value;
                return true;
            }
        }

        resolved = default;
        return false;
    }

    private bool TryResolveFromOptions(string input, out T? resolved)
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

        resolved = default;
        return false;
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

    private async Task OpenMenuSafelyAsync()
    {
        try
        {
            await OpenMenu();
            _isOpen = true;
        }
        catch (InvalidOperationException)
        {
            _isOpen = true;
            await OnOpen.InvokeAsync();
        }
        catch (Exception) when (!_hasRendered)
        {
            _isOpen = true;
            await OnOpen.InvokeAsync();
        }
    }

    private async Task CloseMenuSafelyAsync()
    {
        try
        {
            await CloseMenu(focusAgain: false);
            _isOpen = false;
        }
        catch (InvalidOperationException)
        {
            _isOpen = false;
            await OnClose.InvokeAsync();
        }
        catch (Exception) when (!_hasRendered)
        {
            _isOpen = false;
            await OnClose.InvokeAsync();
        }
    }

    private async Task SelectOptionSafelyAsync(T? value)
    {
        try
        {
            await SelectOption(value);
        }
        catch (InvalidOperationException)
        {
            Value = value;
            await ValueChanged.InvokeAsync(value);
        }
        catch (Exception) when (!_hasRendered)
        {
            Value = value;
            await ValueChanged.InvokeAsync(value);
        }
    }

    private async Task ClearSafelyAsync()
    {
        try
        {
            await ClearAsync();
        }
        catch (InvalidOperationException)
        {
            Value = default;
            await ValueChanged.InvokeAsync(Value);
        }
        catch (Exception) when (!_hasRendered)
        {
            Value = default;
            await ValueChanged.InvokeAsync(Value);
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
