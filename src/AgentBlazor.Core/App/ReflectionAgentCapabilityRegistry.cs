using System.Reflection;
using System.Text;
using System.Text.Json;
using AgentBlazor.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.App;

internal sealed class ReflectionAgentCapabilityRegistry(IEnumerable<Type> capabilityTypes) : IAgentCapabilityRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly NullabilityInfoContext NullabilityContext = new();
    private readonly CapabilityCacheEntry[] _capabilities = capabilityTypes
        .Distinct()
        .Select(BuildCacheEntry)
        .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<AgentCapabilityDescriptor> GetCapabilities(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return _capabilities
            .Select(entry => BuildDescriptor(serviceProvider, entry))
            .Where(static descriptor => descriptor.Actions.Count > 0)
            .ToArray();
    }

    public IReadOnlyList<AgentCapabilityActionDescriptor> GetActions(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return GetCapabilities(serviceProvider)
            .SelectMany(static descriptor => descriptor.Actions)
            .ToArray();
    }

    public bool TryGetAction(
        string actionId,
        IServiceProvider serviceProvider,
        out AgentCapabilityActionDescriptor action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        foreach (var capability in _capabilities)
        {
            var instance = ResolveInstance(serviceProvider, capability.CapabilityType);
            foreach (var info in capability.Actions)
            {
                if (!string.Equals(info.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                action = BuildActionDescriptor(instance, capability, info);
                return true;
            }
        }

        action = default!;
        return false;
    }

    public async Task<CapabilityResult> ExecuteAsync(
        string actionId,
        IReadOnlyDictionary<string, object?> arguments,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        foreach (var capability in _capabilities)
        {
            foreach (var info in capability.Actions)
            {
                if (!string.Equals(info.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var instance = ResolveInstance(serviceProvider, capability.CapabilityType);
                if (!CheckAvailability(instance, info.AvailabilityCheck))
                {
                    return CapabilityResult.Failure(
                        $"Capability action '{info.ActionId}' is not currently available.");
                }

                return await InvokeAsync(instance, info, arguments, cancellationToken).ConfigureAwait(false);
            }
        }

        return CapabilityResult.Failure($"Capability action '{actionId}' is not registered.");
    }

    private static AgentCapabilityDescriptor BuildDescriptor(IServiceProvider serviceProvider, CapabilityCacheEntry entry)
    {
        var instance = ResolveInstance(serviceProvider, entry.CapabilityType);
        var actions = entry.Actions
            .Select(info => BuildActionDescriptor(instance, entry, info))
            .Where(static action => action.IsAvailable)
            .ToArray();

        return new AgentCapabilityDescriptor(
            entry.CapabilityId,
            entry.Name,
            entry.Description,
            entry.Category,
            actions);
    }

    private static AgentCapabilityActionDescriptor BuildActionDescriptor(
        object instance,
        CapabilityCacheEntry capability,
        CapabilityActionCacheInfo info)
    {
        var isAvailable = CheckAvailability(instance, info.AvailabilityCheck);
        return new AgentCapabilityActionDescriptor(
            capability.CapabilityId,
            info.ActionId,
            info.LocalActionId,
            info.Name,
            info.Description,
            info.Category,
            info.RequiresApproval,
            isAvailable,
            info.InputSchema,
            info.Parameters);
    }

    private static CapabilityCacheEntry BuildCacheEntry(Type type)
    {
        var attribute = type.GetCustomAttribute<AgentCapabilityAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' must be annotated with [AgentCapability] to be registered.");

        var capabilityId = attribute.CapabilityId ?? ToCapabilityId(type.Name);
        var name = attribute.Name ?? HumanizeTypeName(type.Name);
        var description = attribute.Description;
        var category = attribute.Category;
        var actions = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.GetCustomAttribute<AgentActionAttribute>() is not null)
            .Select(method => BuildActionCacheInfo(type, capabilityId, method))
            .OrderBy(static action => action.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CapabilityCacheEntry(
            type,
            capabilityId,
            name,
            description,
            category,
            actions);
    }

    private static CapabilityActionCacheInfo BuildActionCacheInfo(Type type, string capabilityId, MethodInfo method)
    {
        var actionAttribute = method.GetCustomAttribute<AgentActionAttribute>()
            ?? throw new InvalidOperationException($"Method '{method.Name}' is missing [AgentAction].");
        var localActionId = actionAttribute.ActionId ?? ToSnakeCase(method.Name);
        var actionId = $"{capabilityId}.{localActionId}";
        var name = ResolveActionName(method);
        var description = actionAttribute.Description ?? name;
        var category = method.DeclaringType?.GetCustomAttribute<AgentCapabilityAttribute>()?.Category;

        return new CapabilityActionCacheInfo(
            actionId,
            localActionId,
            name,
            description,
            category,
            actionAttribute.RequiresApproval,
            BuildInputSchema(method),
            BuildParameterDescriptors(method),
            method,
            ResolveAvailabilityMember(type, actionAttribute.AvailableWhen));
    }

    private static IReadOnlyList<AgentCapabilityParameterDescriptor> BuildParameterDescriptors(MethodInfo method)
    {
        var result = new List<AgentCapabilityParameterDescriptor>();
        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            var attribute = parameter.GetCustomAttribute<AgentParamAttribute>();
            result.Add(new AgentCapabilityParameterDescriptor(
                parameter.Name ?? $"param{result.Count}",
                GetTypeLabel(parameter.ParameterType),
                IsParameterRequired(parameter, attribute),
                attribute?.Description,
                GetAllowedValues(parameter, attribute)));
        }

        return result;
    }

    private static async Task<CapabilityResult> InvokeAsync(
        object instance,
        CapabilityActionCacheInfo info,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var parameters = info.Method.GetParameters();
        var invocationArgs = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var parameterAttribute = parameter.GetCustomAttribute<AgentParamAttribute>();

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                invocationArgs[i] = cancellationToken;
                continue;
            }

            var key = parameter.Name ?? string.Empty;
            if (!TryGetArgValue(arguments, key, parameter.ParameterType, out var value))
            {
                if (IsParameterRequired(parameter, parameterAttribute))
                {
                    return CapabilityResult.NeedsClarification(
                        $"Required parameter '{key}' is missing for capability action '{info.ActionId}'.");
                }

                invocationArgs[i] = parameter.HasDefaultValue ? parameter.DefaultValue : GetDefault(parameter.ParameterType);
                continue;
            }

            invocationArgs[i] = value;
        }

        try
        {
            var result = info.Method.Invoke(instance, invocationArgs);
            if (result is Task<CapabilityResult> typedTask)
            {
                return await typedTask.ConfigureAwait(false);
            }

            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                return CapabilityResult.Success($"Executed {info.Name}.");
            }

            if (result is CapabilityResult capabilityResult)
            {
                return capabilityResult;
            }

            if (result is not null)
            {
                return CapabilityResult.Success($"Executed {info.Name}.") with
                {
                    Outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["result"] = result
                    }
                };
            }

            return CapabilityResult.Success($"Executed {info.Name}.");
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            return CapabilityResult.Failure(
                $"Capability action '{info.ActionId}' failed: {tie.InnerException.Message}");
        }
        catch (Exception ex)
        {
            return CapabilityResult.Failure(
                $"Capability action '{info.ActionId}' failed: {ex.Message}");
        }
    }

    private static object ResolveInstance(IServiceProvider serviceProvider, Type capabilityType)
        => ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, capabilityType);

    private static AvailabilityMember? ResolveAvailabilityMember(Type type, string? memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property is not null && property.PropertyType == typeof(bool))
        {
            return new AvailabilityMember(property, Method: null);
        }

        var method = type.GetMethod(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is not null && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0)
        {
            return new AvailabilityMember(Property: null, Method: method);
        }

        throw new InvalidOperationException(
            $"Capability availability member '{memberName}' on '{type.FullName}' must be a bool property or parameterless bool method.");
    }

    private static bool CheckAvailability(object instance, AvailabilityMember? availability)
    {
        if (availability is null)
        {
            return true;
        }

        if (availability.Property is not null)
        {
            return availability.Property.GetValue(instance) as bool? ?? false;
        }

        return availability.Method?.Invoke(instance, []) as bool? ?? false;
    }

    private static string BuildInputSchema(MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Where(static parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToArray();

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var required = new List<string>();

        foreach (var parameter in parameters)
        {
            var parameterAttribute = parameter.GetCustomAttribute<AgentParamAttribute>();
            properties[parameter.Name ?? string.Empty] = BuildParameterSchema(parameter, parameterAttribute);
            if (IsParameterRequired(parameter, parameterAttribute))
            {
                required.Add(parameter.Name ?? string.Empty);
            }
        }

        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.Serialize(schema, JsonOptions);
    }

    private static object BuildParameterSchema(ParameterInfo parameter, AgentParamAttribute? attribute)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = GetJsonSchemaType(parameter.ParameterType)
        };

        if (!string.IsNullOrWhiteSpace(attribute?.Description))
        {
            schema["description"] = attribute.Description;
        }

        var allowedValues = GetAllowedValues(parameter, attribute);
        if (allowedValues is { Count: > 0 })
        {
            schema["enum"] = allowedValues;
        }

        return schema;
    }

    private static IReadOnlyList<string>? GetAllowedValues(ParameterInfo parameter, AgentParamAttribute? attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute?.AllowedValues))
        {
            return attribute.AllowedValues
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        if (!parameterType.IsEnum)
        {
            return null;
        }

        return Enum.GetNames(parameterType);
    }

    private static string ResolveActionName(MethodInfo method)
    {
        var builder = new StringBuilder(method.Name.Length + 8);
        for (var i = 0; i < method.Name.Length; i++)
        {
            var current = method.Name[i];
            if (i > 0 && char.IsUpper(current) && char.IsLower(method.Name[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string HumanizeTypeName(string typeName)
    {
        var trimmed = typeName.EndsWith("Capabilities", StringComparison.Ordinal)
            ? typeName[..^"Capabilities".Length]
            : typeName;
        return HumanizeName(trimmed);
    }

    private static string HumanizeName(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current) && char.IsLower(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string ToCapabilityId(string typeName)
    {
        var trimmed = typeName.EndsWith("Capabilities", StringComparison.Ordinal)
            ? typeName[..^"Capabilities".Length]
            : typeName;
        return ToSnakeCase(trimmed);
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static string GetTypeLabel(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        if (underlyingType.IsArray)
        {
            return $"array of {GetTypeLabel(underlyingType.GetElementType()!)}";
        }

        if (TryGetEnumerableElementType(underlyingType, out var elementType))
        {
            return $"array of {GetTypeLabel(elementType)}";
        }

        if (underlyingType == typeof(string))
        {
            return "string";
        }

        if (underlyingType == typeof(bool))
        {
            return "boolean";
        }

        if (underlyingType.IsEnum)
        {
            return "string";
        }

        if (underlyingType == typeof(int) ||
            underlyingType == typeof(long) ||
            underlyingType == typeof(short))
        {
            return "integer";
        }

        if (underlyingType == typeof(float) ||
            underlyingType == typeof(double) ||
            underlyingType == typeof(decimal))
        {
            return "number";
        }

        return "object";
    }

    private static string GetJsonSchemaType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        if (underlyingType.IsArray || TryGetEnumerableElementType(underlyingType, out _))
        {
            return "array";
        }

        return GetTypeLabel(underlyingType) switch
        {
            "integer" => "integer",
            "number" => "number",
            "boolean" => "boolean",
            "string" => "string",
            _ => "object"
        };
    }

    private static bool IsParameterRequired(ParameterInfo parameter, AgentParamAttribute? attribute)
    {
        if (attribute is not null)
        {
            return attribute.Required;
        }

        if (parameter.HasDefaultValue)
        {
            return false;
        }

        if (Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
        {
            return false;
        }

        if (!parameter.ParameterType.IsValueType)
        {
            var nullability = NullabilityContext.Create(parameter);
            return nullability.WriteState == NullabilityState.NotNull;
        }

        return true;
    }

    private static bool TryGetArgValue(
        IReadOnlyDictionary<string, object?> arguments,
        string key,
        Type targetType,
        out object? value)
    {
        value = null;
        if (!arguments.TryGetValue(key, out var raw))
        {
            return false;
        }

        if (raw is null)
        {
            value = null;
            return true;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType.IsInstanceOfType(raw))
        {
            value = raw;
            return true;
        }

        try
        {
            if (raw is JsonElement json)
            {
                value = JsonSerializer.Deserialize(json.GetRawText(), targetType, JsonOptions);
                return true;
            }

            if (underlyingType != typeof(string))
            {
                var serialized = JsonSerializer.Serialize(raw, JsonOptions);
                var deserialized = JsonSerializer.Deserialize(serialized, targetType, JsonOptions);
                if (deserialized is not null || !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
                {
                    value = deserialized;
                    return true;
                }
            }

            if (underlyingType.IsEnum)
            {
                value = Enum.Parse(underlyingType, raw.ToString()!, ignoreCase: true);
                return true;
            }

            value = Convert.ChangeType(raw, underlyingType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? GetDefault(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = typeof(char);
            return false;
        }

        var enumerable = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(static candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerable is not null)
        {
            elementType = enumerable.GetGenericArguments()[0];
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    private sealed record CapabilityCacheEntry(
        Type CapabilityType,
        string CapabilityId,
        string Name,
        string? Description,
        string? Category,
        CapabilityActionCacheInfo[] Actions);

    private sealed record CapabilityActionCacheInfo(
        string ActionId,
        string LocalActionId,
        string Name,
        string Description,
        string? Category,
        bool RequiresApproval,
        string InputSchema,
        IReadOnlyList<AgentCapabilityParameterDescriptor> Parameters,
        MethodInfo Method,
        AvailabilityMember? AvailabilityCheck);

    private sealed record AvailabilityMember(PropertyInfo? Property, MethodInfo? Method);
}
