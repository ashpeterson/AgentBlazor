using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Components;

/// <summary>
/// Default schema-aware argument resolver. Uses component state to map display/LLM names
/// to canonical column names and semantic values to concrete values (e.g. "High" → 70 for RiskScore).
/// </summary>
public sealed class ComponentActionArgumentResolver : IComponentActionArgumentResolver
{
    /// <summary>State key: list of canonical column/property names.</summary>
    public const string StateKeyColumns = "columns";

    /// <summary>State key: display name or alias → canonical column name.</summary>
    public const string StateKeyColumnAliases = "columnAliases";

    /// <summary>State key: per-column semantic value → canonical value (e.g. "RiskScore" → { "High" → 70 }).</summary>
    public const string StateKeyValueMappings = "valueMappings";

    public IReadOnlyDictionary<string, object?> Resolve(
        string componentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        ComponentState componentState)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase);

        if (string.Equals(componentType, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
            {
                ResolveColumn(componentState, result);
                if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
                {
                    ResolveFilterValue(componentState, result);
                }
            }
        }

        return result;
    }

    private static void ResolveColumn(ComponentState state, IDictionary<string, object?> result)
    {
        if (!TryGetString(result, "column", out var columnHint) || string.IsNullOrWhiteSpace(columnHint))
        {
            return;
        }

        var canonical = ResolveColumnName(columnHint, state);
        if (canonical is not null)
        {
            result["column"] = canonical;
        }
    }

    private static string? ResolveColumnName(string hint, ComponentState state)
    {
        var columns = GetStringArray(state, StateKeyColumns);
        if (columns is not null)
        {
            var exact = columns.FirstOrDefault(c => string.Equals(c, hint, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        var aliases = GetColumnAliases(state);
        if (aliases is not null && aliases.TryGetValue(hint, out var mapped))
        {
            return mapped;
        }

        foreach (var (alias, canonical) in aliases ?? [])
        {
            if (string.Equals(alias, hint, StringComparison.OrdinalIgnoreCase))
            {
                return canonical;
            }
        }

        return null;
    }

    private static void ResolveFilterValue(ComponentState state, IDictionary<string, object?> result)
    {
        if (!TryGetString(result, "column", out var column) || string.IsNullOrWhiteSpace(column))
        {
            return;
        }

        var valueMappings = GetValueMappings(state);
        if (valueMappings is null)
        {
            return;
        }

        if (!valueMappings.TryGetValue(column, out var columnMap) || columnMap is null)
        {
            return;
        }

        if (!TryGetValue(result, "value", out var rawValue))
        {
            return;
        }

        var valueStr = rawValue?.ToString()?.Trim();
        if (string.IsNullOrEmpty(valueStr))
        {
            return;
        }

        if (columnMap.TryGetValue(valueStr, out var resolved))
        {
            result["value"] = resolved;
            return;
        }

        foreach (var (key, val) in columnMap)
        {
            if (string.Equals(key, valueStr, StringComparison.OrdinalIgnoreCase))
            {
                result["value"] = val;
                return;
            }
        }
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> dict, string key, out string? value)
    {
        value = null;
        if (!dict.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        value = raw.ToString()?.Trim();
        return !string.IsNullOrEmpty(value);
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, object?> dict, string key, out object? value)
    {
        return dict.TryGetValue(key, out value);
    }

    private static string[]? GetStringArray(ComponentState state, string key)
    {
        if (!state.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is string[] arr)
        {
            return arr;
        }

        if (raw is System.Collections.IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object>().Select(o => o?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToArray();
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string>? GetColumnAliases(ComponentState state)
    {
        if (!state.TryGetValue(StateKeyColumnAliases, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is IReadOnlyDictionary<string, string> ro)
        {
            return ro;
        }

        if (raw is IDictionary<string, string> d)
        {
            return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
        }

        if (raw is IDictionary<string, object?> dobj)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in dobj)
            {
                if (!string.IsNullOrEmpty(k) && v?.ToString() is { } s)
                {
                    result[k] = s;
                }
            }

            return result;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? GetValueMappings(ComponentState state)
    {
        if (!state.TryGetValue(StateKeyValueMappings, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> ro)
        {
            return ro;
        }

        if (raw is IDictionary<string, object?> top)
        {
            var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, colVal) in top)
            {
                if (string.IsNullOrEmpty(col))
                {
                    continue;
                }

                if (colVal is IReadOnlyDictionary<string, object?> nested)
                {
                    result[col] = nested;
                    continue;
                }

                if (colVal is IDictionary<string, object?> nestedDict)
                {
                    result[col] = new Dictionary<string, object?>(nestedDict, StringComparer.OrdinalIgnoreCase);
                }
            }

            return result;
        }

        return null;
    }
}
