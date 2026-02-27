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

    /// <summary>
    /// Returns true when the component type has at least one [AgentAction]-decorated method.
    /// </summary>
    public static bool IsAttributeDriven(IAgentControllable component)
        => GetCacheEntry(component.GetType()).Actions.Length > 0;

    /// <summary>
    /// Builds a ComponentCapability from the component's [AgentAction]-decorated methods.
    /// </summary>
    public static ComponentCapability BuildCapability(IAgentControllable component)
    {
        var type = component.GetType();
        var entry = GetCacheEntry(type);
        var capability = new ComponentCapability(component.ComponentType, $"Agent-controlled {component.ComponentType}");

        foreach (var info in entry.Actions)
        {
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
                if (paramAttr?.Required == true)
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
                Description: x.Attr.Description,
                RequiresApproval: x.Attr.RequiresApproval,
                InputSchema: BuildInputSchema(x.Method),
                Method: x.Method))
            .ToArray();

        var readables = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => new { Prop = p, Attr = p.GetCustomAttribute<AgentReadableAttribute>() })
            .Where(x => x.Attr is not null)
            .Select(x => new ReadableCacheInfo(
                StateKey: x.Attr!.StateKey ?? ToCamelCase(x.Prop.Name),
                Description: x.Attr.Description,
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

            if (i > 0) sb.Append(", ");

            sb.Append(GetTypeLabel(p.ParameterType));
            sb.Append(' ');
            sb.Append(p.Name ?? $"param{i}");

            if (attr?.Required == true)
                sb.Append(" [required]");
            else if (!p.HasDefaultValue)
                sb.Append(" [optional]");

            if (attr?.AllowedValues is { Length: > 0 } av)
                sb.Append($" [allowed: {av}]");

            if (attr?.Description is { Length: > 0 } desc)
                sb.Append($" — {desc}");
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string GetTypeLabel(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
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
            raw = UnwrapJsonElement(je);
        }

        // Already the right type
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
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
                result = Enum.Parse(underlying, raw.ToString()!, ignoreCase: true);
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

    // -------------------------------------------------------------------------
    // Name helpers
    // -------------------------------------------------------------------------

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
        string InputSchema,
        MethodInfo Method);

    private sealed record ReadableCacheInfo(
        string StateKey,
        string Description,
        int MaxItems,
        PropertyInfo Property);
}
