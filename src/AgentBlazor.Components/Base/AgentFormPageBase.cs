using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Runtime;
using Microsoft.AspNetCore.Components;

namespace AgentBlazor;

/// <summary>
/// Base class for pages that expose a form with auto-generated agent actions.
/// Inherit from this and the fill action is automatically created from TModel's properties.
///
/// Usage:
/// <code>
/// @page "/my-form"
/// @inherits AgentFormPageBase&lt;MyFormModel&gt;
///
/// @code {
///     protected override string AgentIdValue => "my-form";
///     protected override string FormDisplayName => "My Form";
/// }
/// </code>
/// </summary>
/// <typeparam name="TModel">The form model type. Properties become action parameters.</typeparam>
public abstract class AgentFormPageBase<TModel> : AgentControllableComponentBase where TModel : class, new()
{
    private TModel _model = new();
    private bool _dialogVisible;
    private static readonly PropertyInfo[] ModelProperties = GetSettableProperties();

    /// <summary>
    /// The form model instance. Modify this in your page.
    /// </summary>
    protected TModel Model
    {
        get => _model;
        set => _model = value;
    }

    /// <summary>
    /// Whether the form dialog is visible.
    /// </summary>
    protected bool DialogVisible
    {
        get => _dialogVisible;
        set => _dialogVisible = value;
    }

    /// <summary>
    /// Override to provide the AgentId for this page.
    /// </summary>
    protected abstract string AgentIdValue { get; }

    /// <summary>
    /// Override to provide a human-friendly name for the form (used in action descriptions).
    /// Defaults to the model type name without "Model" suffix.
    /// </summary>
    protected virtual string FormDisplayName => GetDefaultFormName();

    /// <summary>
    /// The component type exposed to the agent. Defaults to model name.
    /// </summary>
    public override string ComponentType => typeof(TModel).Name.Replace("Model", "");

    protected override void OnInitialized()
    {
        AgentId = AgentIdValue;
        base.OnInitialized();
    }

    /// <summary>
    /// Builds the capability with auto-generated fill action.
    /// </summary>
    public override ComponentCapability GetCapability()
    {
        var capability = new ComponentCapability(ComponentType, $"Form for {FormDisplayName}");

        // Add the auto-generated fill action
        var fillAction = new ComponentActionCapability(
            GetFillActionId(),
            $"Fill out the {FormDisplayName} form. Opens the dialog and populates all fields.",
            RequiresApproval: false,
            InputSchema: BuildInputSchema());

        capability.UpsertAction(fillAction);

        // Add open/close actions
        capability.UpsertAction(new ComponentActionCapability(
            "open",
            $"Open the {FormDisplayName} dialog.",
            RequiresApproval: false,
            InputSchema: "()"));

        capability.UpsertAction(new ComponentActionCapability(
            "close",
            $"Close the {FormDisplayName} dialog.",
            RequiresApproval: false,
            InputSchema: "()"));

        return capability;
    }

    /// <summary>
    /// Executes the auto-generated actions.
    /// </summary>
    public override async Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken cancellationToken = default)
    {
        var actionId = action.Name.ToLowerInvariant().Replace("_", "");
        var fillActionId = GetFillActionId().ToLowerInvariant().Replace("_", "");

        if (actionId == fillActionId || actionId == "fill")
        {
            return await ExecuteFillActionAsync(action.Parameters);
        }

        if (actionId == "open")
        {
            _dialogVisible = true;
            await RequestComponentRefreshAsync();
            return ActionResult.Applied($"Opened {FormDisplayName} dialog.");
        }

        if (actionId == "close")
        {
            _dialogVisible = false;
            await RequestComponentRefreshAsync();
            return ActionResult.Applied($"Closed {FormDisplayName} dialog.");
        }

        return ActionResult.Unknown(action.Name);
    }

    /// <summary>
    /// Returns the current state of the form.
    /// </summary>
    public override ComponentState GetCurrentState()
    {
        var fieldNames = ModelProperties.Select(p => p.Name).ToArray();
        var fieldValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var fieldMetadata = new Dictionary<string, FieldMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in ModelProperties)
        {
            fieldValues[prop.Name] = prop.GetValue(_model);
            fieldMetadata[prop.Name] = BuildFieldMetadata(prop);
        }

        return new ComponentState
        {
            ["dialogOpen"] = _dialogVisible,
            ["formName"] = FormDisplayName,
            ["fieldCount"] = ModelProperties.Length,
            ["fields"] = fieldNames,
            ["fieldValues"] = fieldValues,
            ["fieldMetadata"] = fieldMetadata
        };
    }

    private static FieldMetadata BuildFieldMetadata(PropertyInfo prop)
    {
        var propType = prop.PropertyType;
        var underlying = Nullable.GetUnderlyingType(propType);
        var effective = underlying ?? propType;

        string[]? allowed = effective.IsEnum ? Enum.GetNames(effective) : null;
        var req = prop.GetCustomAttribute<RequiredAttribute>();
        var maxLen = prop.GetCustomAttribute<MaxLengthAttribute>();
        var minLen = prop.GetCustomAttribute<MinLengthAttribute>();
        var strLen = prop.GetCustomAttribute<StringLengthAttribute>();
        var range = prop.GetCustomAttribute<RangeAttribute>();
        var regex = prop.GetCustomAttribute<RegularExpressionAttribute>();
        var display = prop.GetCustomAttribute<DisplayAttribute>();
        var displayName = prop.GetCustomAttribute<DisplayNameAttribute>();
        var description = prop.GetCustomAttribute<DescriptionAttribute>();

        return new FieldMetadata
        {
            Type = GetFriendlyTypeName(propType),
            IsRequired = req is not null,
            IsNullable = underlying is not null || !propType.IsValueType,
            MaxLength = maxLen?.Length ?? strLen?.MaximumLength,
            MinLength = minLen?.Length ?? strLen?.MinimumLength,
            Pattern = regex?.Pattern,
            MinValue = range?.Minimum,
            MaxValue = range?.Maximum,
            AllowedValues = allowed,
            DisplayName = display?.Name ?? displayName?.DisplayName,
            Description = description?.Description ?? display?.Description ?? display?.Prompt
        };
    }

    /// <summary>
    /// Override to handle form submission.
    /// </summary>
    protected virtual Task OnFormSubmittedAsync() => Task.CompletedTask;

    /// <summary>
    /// Resets the form to a new model instance.
    /// </summary>
    protected void ResetForm()
    {
        _model = new TModel();
    }

    private async Task<ActionResult> ExecuteFillActionAsync(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return ActionResult.NeedsClarification($"Please provide values for the {FormDisplayName} form.");
        }

        var filledFields = new List<string>();

        foreach (var prop in ModelProperties)
        {
            // Try multiple naming conventions
            var value = TryGetParameterValue(parameters, prop.Name);
            if (value is null) continue;

            if (TrySetProperty(prop, value))
            {
                filledFields.Add(prop.Name);
            }
        }

        if (filledFields.Count == 0)
        {
            return ActionResult.NeedsClarification($"Could not match any parameters to {FormDisplayName} fields.");
        }

        _dialogVisible = true;
        await RequestComponentRefreshAsync();

        return ActionResult.Applied($"Opened {FormDisplayName} and filled {filledFields.Count} field(s): {string.Join(", ", filledFields)}.");
    }

    private object? TryGetParameterValue(IReadOnlyDictionary<string, object?> parameters, string propertyName)
    {
        // Try exact match
        if (parameters.TryGetValue(propertyName, out var value))
            return value;

        // Try camelCase
        var camelCase = ToCamelCase(propertyName);
        if (parameters.TryGetValue(camelCase, out value))
            return value;

        // Try snake_case
        var snakeCase = ToSnakeCase(propertyName);
        if (parameters.TryGetValue(snakeCase, out value))
            return value;

        // Try case-insensitive
        foreach (var kvp in parameters)
        {
            if (string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    private bool TrySetProperty(PropertyInfo prop, object? rawValue)
    {
        if (rawValue is null)
        {
            if (!prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) != null)
            {
                prop.SetValue(_model, null);
                return true;
            }
            return false;
        }

        // Unwrap JsonElement
        if (rawValue is JsonElement json)
        {
            rawValue = json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number when json.TryGetInt32(out var i) => i,
                JsonValueKind.Number when json.TryGetInt64(out var l) => l,
                JsonValueKind.Number when json.TryGetDecimal(out var d) => d,
                JsonValueKind.Number => json.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => json.ToString()
            };

            if (rawValue is null) return false;
        }

        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

        try
        {
            object? converted;

            if (targetType.IsInstanceOfType(rawValue))
            {
                converted = rawValue;
            }
            else if (targetType.IsEnum)
            {
                converted = Enum.Parse(targetType, rawValue.ToString()!, ignoreCase: true);
            }
            else if (targetType == typeof(Guid))
            {
                converted = Guid.Parse(rawValue.ToString()!);
            }
            else
            {
                converted = Convert.ChangeType(rawValue, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }

            prop.SetValue(_model, converted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetFillActionId() => $"fill_{ToSnakeCase(typeof(TModel).Name.Replace("Model", ""))}";

    private static string GetDefaultFormName()
    {
        var name = typeof(TModel).Name;
        if (name.EndsWith("Model", StringComparison.Ordinal))
            name = name[..^5];

        // Insert spaces before capitals: "SupplierOnboarding" -> "Supplier Onboarding"
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }

    private string BuildInputSchema()
    {
        var parts = new List<string>();

        foreach (var prop in ModelProperties)
        {
            var typeName = GetFriendlyTypeName(prop.PropertyType);
            var paramName = ToCamelCase(prop.Name);
            var required = prop.GetCustomAttribute<RequiredAttribute>() != null;
            var desc = prop.GetCustomAttribute<DescriptionAttribute>()?.Description
                    ?? prop.GetCustomAttribute<DisplayAttribute>()?.Description;

            var part = $"{typeName} {paramName}";
            part += required ? " [required]" : " [optional]";
            if (!string.IsNullOrEmpty(desc))
                part += $" — {desc}";

            parts.Add(part);
        }

        return $"({string.Join(", ", parts)})";
    }

    private static PropertyInfo[] GetSettableProperties()
    {
        return typeof(TModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying switch
        {
            _ when underlying == typeof(string) => "string",
            _ when underlying == typeof(int) || underlying == typeof(long) => "integer",
            _ when underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float) => "number",
            _ when underlying == typeof(bool) => "boolean",
            _ when underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) => "datetime",
            _ when underlying == typeof(DateOnly) => "date",
            _ when underlying.IsEnum => "string",
            _ => "any"
        };
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string ToSnakeCase(string name)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
