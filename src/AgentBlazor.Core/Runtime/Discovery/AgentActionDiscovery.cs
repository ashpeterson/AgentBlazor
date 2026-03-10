using System.Reflection;
using System.Text;
using System.Text.Json;
using AgentBlazor.Attributes;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;

namespace AgentBlazor.Core.Runtime.Discovery;

/// <summary>
/// Reflection-based engine that drives the [AgentAction] / [AgentReadable] attribute system.
/// Builds capability catalogs, state snapshots, and dispatches action invocations without
/// requiring components to implement GetCapability() / ExecuteActionAsync() manually.
/// </summary>
public static class AgentActionDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Cache reflection results per component type to avoid repeated scanning
    private static readonly Dictionary<Type, DiscoveryCacheEntry> Cache = new();
    private static readonly Lock CacheLock = new();
    private static readonly NullabilityInfoContext NullabilityContext = new();

    /// <summary>
    /// Returns true when the component type has at least one [AgentAction]-decorated method.
    /// </summary>
    public static bool IsAttributeDriven(IAgentControllable component)
        => GetCacheEntry(component.GetType()).Actions.Length > 0;

    /// <summary>
    /// Builds a ComponentCapability from the component's [AgentAction]-decorated methods.
    /// Only includes actions that pass their AvailableWhen check at this moment.
    /// </summary>
    public static ComponentCapability BuildCapability(IAgentControllable component)
    {
        var type = component.GetType();
        var entry = GetCacheEntry(type);
        var capability = new ComponentCapability(component.ComponentType, $"Agent-controlled {component.ComponentType}");

        foreach (var info in entry.Actions)
        {
            if (!CheckAvailability(component, info.AvailabilityCheck)) continue;
            capability.UpsertAction(new ComponentActionCapability(
                info.ActionId,
                info.Description,
                info.RequiresApproval,
                info.InputSchema));
        }

        return capability;
    }

    /// <summary>
    /// Builds a ComponentState from the component's [AgentReadable]-decorated properties.
    /// </summary>
    public static ComponentState BuildState(IAgentControllable component)
    {
        var type = component.GetType();
        var entry = GetCacheEntry(type);
        var state = new ComponentState();

        foreach (var info in entry.Readables)
        {
            try
            {
                var value = info.Property.GetValue(component);
                state[info.StateKey] = SerializeReadable(value, info.MaxItems);
            }
            catch
            {
                // Keep runtime resilient to property read faults
                state[info.StateKey] = "null";
            }
        }

        return state;
    }

    /// <summary>
    /// Returns a structured description of every [AgentAction] method on the component,
    /// including parameter names, types, constraints, and approval requirements.
    /// Used by the runtime to build MountedComponentState.Actions for the planner prompt.
    /// </summary>
    internal static IReadOnlyList<DiscoveredAction> GetDiscoveredActions(IAgentControllable component)
    {
        var entry = GetCacheEntry(component.GetType());
        var result = new List<DiscoveredAction>(entry.Actions.Length);
        foreach (var info in entry.Actions)
        {
            if (!CheckAvailability(component, info.AvailabilityCheck)) continue;
            result.Add(new DiscoveredAction(
                info.ActionId,
                info.Description,
                info.RequiresApproval,
                BuildStructuredParameters(info.Method),
                info.Instructions));
        }
        return result;
    }

    private static IReadOnlyList<DiscoveredParameter> BuildStructuredParameters(MethodInfo method)
    {
        var result = new List<DiscoveredParameter>();
        foreach (var p in method.GetParameters())
        {
            if (p.ParameterType == typeof(CancellationToken)) continue;

            var attr = p.GetCustomAttribute<AgentParamAttribute>();
            var allowedValues = GetAllowedValues(p, attr);

            result.Add(new DiscoveredParameter(
                p.Name ?? $"param{result.Count}",
                GetTypeLabel(p.ParameterType),
                Required: IsParameterRequired(p, attr),
                Description: attr?.Description,
                AllowedValues: allowedValues));
        }
        return result;
    }

    /// <summary>
    /// Dispatches an AgentAction invocation to the matching [AgentAction]-decorated method
    /// on the component, coercing arguments from the action's parameter dictionary.
    /// </summary>
    public static async Task<ActionResult> ExecuteActionAsync(
        IAgentControllable component,
        AgentAction action,
        CancellationToken cancellationToken = default)
    {
        var type = component.GetType();
        var entry = GetCacheEntry(type);
        var actionId = action.Name;

        var info = Array.Find(entry.Actions,
            a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

        if (info is null)
        {
            return ActionResult.Unknown(actionId);
        }

        // Build the invocation argument array
        var methodParams = info.Method.GetParameters();
        var args = new object?[methodParams.Length];

        for (var i = 0; i < methodParams.Length; i++)
        {
            var param = methodParams[i];
            var paramAttr = param.GetCustomAttribute<AgentParamAttribute>();

            // CancellationToken is injected automatically
            if (param.ParameterType == typeof(CancellationToken))
            {
                args[i] = cancellationToken;
                continue;
            }

            // Try to find the value in the action's parameters dictionary
            var key = paramAttr is not null && param.Name is not null
                ? param.Name
                : param.Name ?? string.Empty;

            if (!TryGetArgValue(action.Parameters, key, param.ParameterType, out var argValue))
            {
                if (IsParameterRequired(param, paramAttr))
                {
                    return ActionResult.NeedsClarification(
                        $"Required parameter '{key}' is missing for action '{actionId}'.");
                }

                // Use default value
                args[i] = param.HasDefaultValue ? param.DefaultValue : GetDefault(param.ParameterType);
                continue;
            }

            args[i] = argValue;
        }

        try
        {
            var result = info.Method.Invoke(component, args);

            // Handle Task / Task<T> / ValueTask return types
            if (result is Task<ActionResult> typedTask)
            {
                return await typedTask.ConfigureAwait(false);
            }

            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                return ActionResult.Applied($"Executed {actionId}.");
            }

            if (result is ActionResult directResult)
            {
                return directResult;
            }

            return ActionResult.Applied($"Executed {actionId}.");
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            return ActionResult.Failure($"Action '{actionId}' failed: {tie.InnerException.Message}");
        }
        catch (Exception ex)
        {
            return ActionResult.Failure($"Action '{actionId}' failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Cache management
    // -------------------------------------------------------------------------

    private static DiscoveryCacheEntry GetCacheEntry(Type type)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(type, out var cached))
                return cached;

            var entry = BuildCacheEntry(type);
            Cache[type] = entry;
            return entry;
        }
    }

    private static DiscoveryCacheEntry BuildCacheEntry(Type type)
    {
        var actions = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Concat(GetInheritedAgentActionMethods(type))
            .Select(m => new { Method = m, Attr = m.GetCustomAttribute<AgentActionAttribute>() })
            .Where(x => x.Attr is not null)
            .Select(x => new ActionCacheInfo(
                ActionId: x.Attr!.ActionId ?? ToSnakeCase(x.Method.Name),
                Description: ResolveActionDescription(x.Method, x.Attr.Description),
                RequiresApproval: x.Attr.RequiresApproval,
                FollowUp: x.Attr.FollowUp,
                InputSchema: BuildInputSchema(x.Method),
                Method: x.Method,
                AvailabilityCheck: ResolveAvailabilityMember(type, x.Attr.AvailableWhen),
                Instructions: x.Attr.Instructions))
            .ToArray();

        var readables = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new { Prop = p, Attr = p.GetCustomAttribute<AgentReadableAttribute>() })
            .Where(x => x.Attr is not null)
            .Select(x => new ReadableCacheInfo(
                StateKey: x.Attr!.StateKey ?? ToCamelCase(x.Prop.Name),
                Description: ResolveReadableDescription(x.Prop, x.Attr.Description),
                MaxItems: x.Attr.MaxItems,
                Property: x.Prop))
            .ToArray();

        return new DiscoveryCacheEntry(actions, readables);
    }

    private static IEnumerable<MethodInfo> GetInheritedAgentActionMethods(Type type)
    {
        // Walk the inheritance chain (but stop at object) to pick up [AgentAction] on base classes
        var baseType = type.BaseType;
        while (baseType is not null && baseType != typeof(object))
        {
            foreach (var m in baseType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.GetCustomAttribute<AgentActionAttribute>() is not null)
                    yield return m;
            }

            baseType = baseType.BaseType;
        }
    }

    // -------------------------------------------------------------------------
    // Availability gating helpers
    // -------------------------------------------------------------------------

    private static MemberInfo? ResolveAvailabilityMember(Type type, string? memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName)) return null;

        // Try property first
        var property = type.GetProperty(memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property?.PropertyType == typeof(bool)) return property;

        // Try parameterless method
        var method = type.GetMethod(memberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, types: Type.EmptyTypes, modifiers: null);
        if (method?.ReturnType == typeof(bool)) return method;

        // Not found — warn at runtime (no logger available here; caller logs)
        return null;
    }

    private static bool CheckAvailability(IAgentControllable component, MemberInfo? check)
    {
        if (check is null) return true;

        try
        {
            return check switch
            {
                PropertyInfo prop => prop.GetValue(component) is true,
                MethodInfo meth => meth.Invoke(component, null) is true,
                _ => true
            };
        }
        catch
        {
            // If availability check throws, treat action as available to avoid silent gating
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // Schema and serialization helpers
    // -------------------------------------------------------------------------

    private static string BuildInputSchema(MethodInfo method)
    {
        var sb = new StringBuilder();
        sb.Append('(');

        var parameters = method.GetParameters()
            .Where(static p => p.ParameterType != typeof(CancellationToken))
            .ToArray();

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var attr = p.GetCustomAttribute<AgentParamAttribute>();
            var allowedValues = GetAllowedValues(p, attr);
            var isRequired = IsParameterRequired(p, attr);

            if (i > 0) sb.Append(", ");

            sb.Append(GetTypeLabel(p.ParameterType));
            sb.Append(' ');
            sb.Append(p.Name ?? $"param{i}");

            if (isRequired)
                sb.Append(" [required]");
            else if (!p.HasDefaultValue)
                sb.Append(" [optional]");

            if (allowedValues is { Length: > 0 })
                sb.Append($" [allowed: {string.Join(",", allowedValues)}]");

            if (attr?.Description is { Length: > 0 } desc)
                sb.Append($" — {desc}");
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string GetTypeLabel(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        // Array types: string[] → "array of string"
        if (underlying.IsArray)
            return $"array of {GetTypeLabel(underlying.GetElementType()!)}";

        // List<T> → "array of T"
        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
            return $"array of {GetTypeLabel(underlying.GenericTypeArguments[0])}";

        // IEnumerable<T> and IReadOnlyList<T> → "array of T"
        if (underlying.IsGenericType &&
            (underlying.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
             underlying.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
             underlying.GetGenericTypeDefinition() == typeof(IList<>)))
            return $"array of {GetTypeLabel(underlying.GenericTypeArguments[0])}";

        return underlying switch
        {
            _ when underlying == typeof(string) => "string",
            _ when underlying == typeof(int) => "integer",
            _ when underlying == typeof(long) => "integer",
            _ when underlying == typeof(double) => "number",
            _ when underlying == typeof(float) => "number",
            _ when underlying == typeof(decimal) => "number",
            _ when underlying == typeof(bool) => "boolean",
            _ when underlying.IsEnum => "string",
            _ => "any"
        };
    }

    private static object? SerializeReadable(object? value, int maxItems)
    {
        if (value is null)
            return null;

        if (value is string s)
            return s;

        if (value is bool or int or long or float or double or decimal)
            return value;

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(item);
                if (maxItems > 0 && list.Count >= maxItems)
                    break;
            }

            return JsonSerializer.Serialize(list, JsonOptions);
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    // -------------------------------------------------------------------------
    // Argument coercion
    // -------------------------------------------------------------------------

    private static bool TryGetArgValue(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        Type targetType,
        out object? result)
    {
        result = null;

        // Try exact match, then case-insensitive
        if (!parameters.TryGetValue(key, out var raw))
        {
            raw = parameters
                .FirstOrDefault(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (raw is null && !parameters.Any(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (raw is null)
        {
            result = null;
            return true;
        }

        // Unwrap System.Text.Json.JsonElement (common when args come from JSON deserialization)
        if (raw is System.Text.Json.JsonElement je)
        {
            // Handle array JsonElement directly for collection targets
            if (je.ValueKind == JsonValueKind.Array && IsCollectionTarget(targetType))
            {
                result = CoerceJsonArray(je, targetType);
                return result is not null;
            }
            raw = UnwrapJsonElement(je);
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // Handle raw List<object?> (produced by UnwrapJsonElement for array JsonElements) → collection target
        if (raw is List<object?> rawList && IsCollectionTarget(underlying))
        {
            result = CoerceObjectList(rawList, underlying);
            return result is not null;
        }

        // Already the right type
        if (underlying.IsInstanceOfType(raw))
        {
            result = raw;
            return true;
        }

        // Enum coercion from string or int
        if (underlying.IsEnum)
        {
            try
            {
                result = Enum.Parse(underlying, raw?.ToString() ?? string.Empty, ignoreCase: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // General conversion
        try
        {
            result = Convert.ChangeType(raw, underlying, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCollectionTarget(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsArray ||
               (t.IsGenericType && (
                   t.GetGenericTypeDefinition() == typeof(List<>) ||
                   t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                   t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ||
                   t.GetGenericTypeDefinition() == typeof(IList<>)));
    }

    private static object? CoerceJsonArray(System.Text.Json.JsonElement arrayElement, Type targetType)
    {
        var elementType = GetCollectionElementType(targetType);
        if (elementType is null) return null;

        var items = arrayElement.EnumerateArray()
            .Select(e => CoerceScalar(UnwrapJsonElement(e), elementType))
            .ToList();

        return BuildCollectionResult(items, elementType, targetType);
    }

    private static object? CoerceObjectList(List<object?> rawList, Type targetType)
    {
        var elementType = GetCollectionElementType(targetType);
        if (elementType is null) return null;

        var items = rawList.Select(item => CoerceScalar(item, elementType)).ToList();
        return BuildCollectionResult(items, elementType, targetType);
    }

    private static object? CoerceScalar(object? value, Type targetType)
    {
        if (value is null) return null;
        if (targetType.IsInstanceOfType(value)) return value;
        if (targetType.IsEnum) return Enum.Parse(targetType, value.ToString()!, ignoreCase: true);
        try { return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return value; }
    }

    private static object? BuildCollectionResult(List<object?> items, Type elementType, Type targetType)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsArray)
        {
            var arr = Array.CreateInstance(elementType, items.Count);
            for (var i = 0; i < items.Count; i++) arr.SetValue(items[i], i);
            return arr;
        }

        // List<T> or interface types — build a List<T>
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var item in items) list.Add(item);
        return list;
    }

    private static Type? GetCollectionElementType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsArray) return t.GetElementType();
        if (t.IsGenericType) return t.GenericTypeArguments.Length > 0 ? t.GenericTypeArguments[0] : null;
        return null;
    }

    private static object? UnwrapJsonElement(System.Text.Json.JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(e => UnwrapJsonElement(e)).ToList(),
            JsonValueKind.Object => el.Deserialize<Dictionary<string, object?>>(),
            _ => el.ToString()
        };
    }

    private static object? GetDefault(Type type)
        => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static string[]? GetAllowedValues(ParameterInfo parameter, AgentParamAttribute? attr)
    {
        if (!string.IsNullOrWhiteSpace(attr?.AllowedValues))
        {
            return attr.AllowedValues
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var underlying = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        return underlying.IsEnum ? Enum.GetNames(underlying) : null;
    }

    private static bool IsParameterRequired(ParameterInfo parameter, AgentParamAttribute? attr)
    {
        if (attr is not null)
        {
            return attr.Required;
        }

        if (parameter.HasDefaultValue)
        {
            return false;
        }

        var type = parameter.ParameterType;
        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is null;
        }

        try
        {
            return NullabilityContext.Create(parameter).WriteState == NullabilityState.NotNull;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveActionDescription(MethodInfo method, string? configuredDescription)
    {
        if (!string.IsNullOrWhiteSpace(configuredDescription))
        {
            return configuredDescription.Trim();
        }

        return HumanizeName(method.Name);
    }

    private static string ResolveReadableDescription(PropertyInfo property, string? configuredDescription)
    {
        if (!string.IsNullOrWhiteSpace(configuredDescription))
        {
            return configuredDescription.Trim();
        }

        return HumanizeName(property.Name);
    }

    // -------------------------------------------------------------------------
    // Name helpers
    // -------------------------------------------------------------------------

    private static string HumanizeName(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "Value";
        }

        var source = identifier.Replace('_', ' ').Replace('-', ' ');
        var sb = new StringBuilder(source.Length + 8);

        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            if (i > 0 &&
                char.IsUpper(current) &&
                char.IsLetterOrDigit(source[i - 1]) &&
                source[i - 1] != ' ')
            {
                sb.Append(' ');
            }

            sb.Append(current);
        }

        var text = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Value";
        }

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    // -------------------------------------------------------------------------
    // Internal cache types
    // -------------------------------------------------------------------------

    private sealed record DiscoveryCacheEntry(
        ActionCacheInfo[] Actions,
        ReadableCacheInfo[] Readables);

    private sealed record ActionCacheInfo(
        string ActionId,
        string Description,
        bool RequiresApproval,
        bool FollowUp,
        string InputSchema,
        MethodInfo Method,
        MemberInfo? AvailabilityCheck,
        string? Instructions);

    private sealed record ReadableCacheInfo(
        string StateKey,
        string Description,
        int MaxItems,
        PropertyInfo Property);
}

// -------------------------------------------------------------------------
// Data transfer types for structured action discovery
// -------------------------------------------------------------------------

internal sealed record DiscoveredAction(
    string ActionId,
    string Description,
    bool RequiresApproval,
    IReadOnlyList<DiscoveredParameter> Parameters,
    string? Instructions = null);

internal sealed record DiscoveredParameter(
    string Name,
    string Type,
    bool Required,
    string? Description,
    string[]? AllowedValues);
