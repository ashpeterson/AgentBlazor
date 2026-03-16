using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using RuntimeComponentState = AgentBlazor.Core.Runtime.Components.ComponentState;

namespace AgentBlazor.Components;

public class AgentForm : MudForm, IAgentControllable, IDisposable
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
    public EventCallback<bool> ValidationChanged { get; set; }

    [Parameter]
    public EventCallback Submitted { get; set; }

    [Parameter]
    public string? FormName { get; set; }

    public string ComponentType => "Form";

    private AgentControllableComponentRuntimeSupport? _runtimeSupport;
    private ILogger? _logger;
    private object? _trackedModel;
    private Dictionary<string, object?> _initialValues = new(StringComparer.OrdinalIgnoreCase);

    [AgentReadable("Whether the form is currently valid")]
    public bool FormIsValid => IsValid;

    [AgentReadable("Whether any field has been touched")]
    public bool FormIsTouched => IsTouched;

    [AgentReadable("Current validation errors")]
    public string[] CurrentErrors => Errors;

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
        EnsureModelTracked();
        EnsureAgentUserAttributes();
    }

    public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);

    public virtual RuntimeComponentState GetCurrentState()
    {
        EnsureModel();

        var values = CaptureValues();
        var metadata = GetFieldMetadata();

        return new RuntimeComponentState
        {
            ["isValid"] = IsValid,
            ["isTouched"] = IsTouched,
            ["errors"] = Errors,
            ["fieldCount"] = values.Count,
            ["fields"] = values.Keys.ToArray(),
            ["fieldValues"] = values,
            ["fieldMetadata"] = metadata
        };
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

    [AgentAction("Validate the form", ActionId = "validate")]
    public async Task<ActionResult> ValidateForm()
    {
        EnsureModel();
        await Validate();
        await ValidationChanged.InvokeAsync(IsValid);
        await RequestComponentRefreshAsync();
        return IsValid
            ? ActionResult.Applied("Form validation passed.")
            : ActionResult.Failure("Form validation failed.");
    }

    [AgentAction("Reset the form to its initial values", ActionId = "reset")]
    public async Task<ActionResult> ResetForm()
    {
        EnsureModel();
        RestoreValues(_initialValues);
        ResetValidation();
        ResetTouched();
        await ValidationChanged.InvokeAsync(false);
        await RequestComponentRefreshAsync();
        return ActionResult.Applied("Reset form to initial values.");
    }

    [AgentAction("Submit the form", ActionId = "submit", RequiresApproval = true)]
    public async Task<ActionResult> SubmitForm()
    {
        EnsureModel();
        await Validate();
        await ValidationChanged.InvokeAsync(IsValid);
        if (!IsValid)
        {
            return ActionResult.Failure("Submit blocked because validation failed.");
        }

        await Submitted.InvokeAsync();
        await RequestComponentRefreshAsync();
        return ActionResult.Applied("Submitted form.");
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

    private void EnsureAgentUserAttributes()
    {
        UserAttributes ??= [];
        if (!UserAttributes.ContainsKey("id"))
        {
            UserAttributes["id"] = AgentId;
        }

        if (!string.IsNullOrWhiteSpace(FormName) && !UserAttributes.ContainsKey("name"))
        {
            UserAttributes["name"] = FormName;
        }

        UserAttributes["data-ab-type"] = "form";
        UserAttributes["data-ab-agentid"] = AgentId;
    }

    private void EnsureModel()
    {
        if (Model is null)
        {
            throw new InvalidOperationException($"{nameof(AgentForm)} requires a non-null {nameof(Model)}.");
        }
    }

    private void EnsureModelTracked()
    {
        EnsureModel();
        if (!ReferenceEquals(_trackedModel, Model))
        {
            _trackedModel = Model;
            _initialValues = CaptureValues();
        }
    }

    private Dictionary<string, object?> CaptureValues()
    {
        EnsureModel();

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in Model!.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            values[property.Name] = property.GetValue(Model);
        }

        return values;
    }

    private void RestoreValues(IReadOnlyDictionary<string, object?> values)
    {
        EnsureModel();

        foreach (var property in Model!.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!values.TryGetValue(property.Name, out var initial))
            {
                continue;
            }

            if (initial is null)
            {
                property.SetValue(Model, null);
                continue;
            }

            if (TryConvert(initial, property.PropertyType, out var converted))
            {
                property.SetValue(Model, converted);
            }
        }
    }

    private Dictionary<string, FieldMetadata> GetFieldMetadata()
    {
        EnsureModel();

        var result = new Dictionary<string, FieldMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in Model!.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyType = property.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(propertyType);
            var effectiveType = underlyingType ?? propertyType;

            string[]? allowedValues = effectiveType.IsEnum ? Enum.GetNames(effectiveType) : null;
            var required = property.GetCustomAttribute<RequiredAttribute>();
            var maxLength = property.GetCustomAttribute<MaxLengthAttribute>();
            var minLength = property.GetCustomAttribute<MinLengthAttribute>();
            var stringLength = property.GetCustomAttribute<StringLengthAttribute>();
            var range = property.GetCustomAttribute<RangeAttribute>();
            var regex = property.GetCustomAttribute<RegularExpressionAttribute>();
            var display = property.GetCustomAttribute<DisplayAttribute>();
            var displayName = property.GetCustomAttribute<DisplayNameAttribute>();

            result[property.Name] = new FieldMetadata
            {
                Type = GetFriendlyTypeName(effectiveType),
                IsRequired = required is not null,
                IsNullable = underlyingType is not null || !propertyType.IsValueType,
                MaxLength = maxLength?.Length ?? stringLength?.MaximumLength,
                MinLength = minLength?.Length ?? stringLength?.MinimumLength,
                Pattern = regex?.Pattern,
                MinValue = range?.Minimum,
                MaxValue = range?.Maximum,
                AllowedValues = allowedValues,
                DisplayName = display?.Name ?? displayName?.DisplayName,
                Description = display?.Description ?? display?.Prompt
            };
        }

        return result;
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return "integer";
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "datetime";
        if (type == typeof(DateOnly)) return "date";
        if (type == typeof(TimeOnly) || type == typeof(TimeSpan)) return "time";
        if (type == typeof(Guid)) return "guid";
        if (type.IsEnum) return "enum";
        return "object";
    }

    private static bool TryConvert(object raw, Type destinationType, out object? converted)
    {
        var target = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        try
        {
            if (target.IsEnum)
            {
                converted = raw is string text
                    ? Enum.Parse(target, text, ignoreCase: true)
                    : Enum.ToObject(target, raw);
                return true;
            }

            if (target == typeof(Guid))
            {
                converted = raw is Guid guid ? guid : Guid.Parse(raw.ToString() ?? string.Empty);
                return true;
            }

            converted = Convert.ChangeType(raw, target, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            converted = null;
            return false;
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

    void IDisposable.Dispose()
    {
        _runtimeSupport?.Dispose();
        base.Dispose();
    }
}
