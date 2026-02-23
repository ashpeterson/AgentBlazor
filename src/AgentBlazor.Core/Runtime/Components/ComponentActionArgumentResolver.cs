using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Components;

/// <summary>
/// Default schema-aware argument resolver. Uses component state to map display/LLM names
/// to canonical column names and semantic values to concrete values (e.g. "High" → 70 for RiskScore).
/// Column names are resolved by: (1) exact match on discovered columns, (2) optional alias map,
/// (3) deterministic discovery-based match (token overlap against reflected property names).
/// </summary>
public sealed class ComponentActionArgumentResolver : IComponentActionArgumentResolver
{
    /// <summary>State key: list of canonical column/property names (from reflection on the grid's TItem).</summary>
    public const string StateKeyColumns = "columns";

    /// <summary>State key: optional display name or alias → canonical column name.</summary>
    public const string StateKeyColumnAliases = "columnAliases";

    /// <summary>State key: per-column semantic value → canonical value (e.g. "RiskScore" → { "High" → 70 }).</summary>
    public const string StateKeyValueMappings = "valueMappings";

    public IReadOnlyDictionary<string, object?> Resolve(
        string componentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        ComponentState componentState)
        => ResolveArguments(componentType, actionId, arguments, componentState);

    /// <summary>
    /// Resolves arguments using component state (column discovery, aliases, value mappings).
    /// Use this when an <see cref="IComponentActionArgumentResolver"/> instance is not available (e.g. optional DI).
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ResolveArguments(
        string componentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        ComponentState componentState)
        => ResolveArguments(componentType, actionId, arguments, componentState, knownColumns: null);

    /// <summary>
    /// Resolves arguments with optional explicit column list (from reflection). When <paramref name="knownColumns"/> is provided,
    /// it is used for column resolution instead of state["columns"], so resolution works even when state does not expose columns.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ResolveArguments(
        string componentType,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        ComponentState componentState,
        string[]? knownColumns)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in arguments)
        {
            result[kv.Key] = kv.Value;
        }

        // ComponentType may be "DataGrid" (from IAgentControllable) or "AgentDataGrid" (from capability profile)
        if (IsDataGridComponentType(componentType))
        {
            if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
            {
                ResolveColumn(componentState, result, knownColumns);
                if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
                {
                    ResolveFilterValue(componentState, result);
                }
            }
        }

        return new Dictionary<string, object?>(result, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDataGridComponentType(string componentType) =>
        string.Equals(componentType, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(componentType, "DataGrid", StringComparison.OrdinalIgnoreCase);

    private static void ResolveColumn(ComponentState state, IDictionary<string, object?> result, string[]? knownColumns = null)
    {
        if (!TryGetString((IReadOnlyDictionary<string, object?>)result, "column", out var columnHint) || string.IsNullOrWhiteSpace(columnHint))
        {
            return;
        }

        var canonical = ResolveColumnName(columnHint, state, knownColumns);
        if (canonical is not null)
        {
            result["column"] = canonical;
        }
    }

    private static string? ResolveColumnName(string hint, ComponentState state, string[]? knownColumns = null)
    {
        var columns = knownColumns ?? GetStringArray(state, StateKeyColumns);
        if (columns is not null && columns.Length > 0)
        {
            // 1) Exact match on discovered columns (from reflection on grid TItem)
            var exact = columns.FirstOrDefault(c => string.Equals(c, hint, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            // 2) Optional alias map (app can override or add display-name → canonical)
            var aliases = GetColumnAliases(state);
            if (aliases is not null && aliases.TryGetValue(hint, out var mapped))
            {
                return mapped;
            }
            if (aliases is not null)
            {
                foreach (var (alias, canonical) in aliases)
                {
                    if (string.Equals(alias, hint, StringComparison.OrdinalIgnoreCase))
                    {
                        return canonical;
                    }
                }
            }

            // 3) Deterministic discovery-based match: resolve LLM-style names (e.g. "riskLevel") to canonical property names (e.g. "RiskScore") using token overlap over reflected columns. No app configuration required.
            if (TryResolveColumnByDiscovery(hint, columns, out var discovered))
            {
                return discovered;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a column hint to a canonical column name by token overlap over the discovered column list.
    /// Deterministic: same hint and same column list always yield the same result.
    /// </summary>
    private static bool TryResolveColumnByDiscovery(string hint, string[] columns, out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(hint) || columns.Length == 0)
        {
            return false;
        }

        var hintTokens = GetIdentifierTokens(hint);
        if (hintTokens.Count == 0)
        {
            return false;
        }

        int bestScore = -1;
        string? bestColumn = null;

        foreach (var column in columns)
        {
            var columnTokens = GetIdentifierTokens(column);
            int score = 0;
            foreach (var token in hintTokens)
            {
                if (columnTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    score++;
                }
            }
            if (score > bestScore && score > 0)
            {
                bestScore = score;
                bestColumn = column;
            }
            else if (score == bestScore && bestColumn is not null && string.Compare(column, bestColumn, StringComparison.OrdinalIgnoreCase) < 0)
            {
                bestColumn = column;
            }
        }

        if (bestColumn is not null && bestScore > 0)
        {
            canonical = bestColumn;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits an identifier into tokens (e.g. "riskLevel" → ["risk","level"], "RiskScore" → ["risk","score"]) for deterministic matching.
    /// Splits on non-letter/digit and on camelCase boundaries (uppercase after lowercase).
    /// </summary>
    private static List<string> GetIdentifierTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            if (char.IsUpper(c) && current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }

            current.Append(char.ToLowerInvariant(c));
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>Built-in semantic values for filter resolution when the app does not provide ValueMappings.
    /// Enables "high risk", "low", etc. to work with zero app configuration.</summary>
    private static readonly IReadOnlyDictionary<string, object> SemanticValueFallbacks =
        new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["high"] = 70,
            ["medium"] = 50,
            ["low"] = 30
        };

    private static void ResolveFilterValue(ComponentState state, IDictionary<string, object?> result)
    {
        if (!TryGetString((IReadOnlyDictionary<string, object?>)result, "column", out var column) || string.IsNullOrWhiteSpace(column))
            return;
        if (!TryGetValue((IReadOnlyDictionary<string, object?>)result, "value", out var rawValue))
            return;
        var valueStr = rawValue?.ToString()?.Trim();
        if (string.IsNullOrEmpty(valueStr))
            return;

        // 1) App-provided ValueMappings (optional)
        var valueMappings = GetValueMappings(state);
        if (valueMappings is not null && valueMappings.TryGetValue(column, out var columnMap) && columnMap is not null)
        {
            if (columnMap.TryGetValue(valueStr, out var resolved))
            {
                result["value"] = resolved;
                PromoteEqToGteForSemanticThreshold(result);
                return;
            }
            foreach (var (key, val) in columnMap)
            {
                if (string.Equals(key, valueStr, StringComparison.OrdinalIgnoreCase))
                {
                    result["value"] = val;
                    PromoteEqToGteForSemanticThreshold(result);
                    return;
                }
            }
        }

        // 2) Built-in semantic fallback so apps don't need ValueMappings for common terms
        if (SemanticValueFallbacks.TryGetValue(valueStr, out var fallback))
        {
            result["value"] = fallback;
            PromoteEqToGteForSemanticThreshold(result);
        }
    }

    /// <summary>
    /// When a filter value was resolved from a semantic term (e.g. "high" → 70), treat "eq" as "gte"
    /// so that "high risk" shows rows with score >= 70 instead of exactly 70.
    /// </summary>
    private static void PromoteEqToGteForSemanticThreshold(IDictionary<string, object?> result)
    {
        if (!TryGetString((IReadOnlyDictionary<string, object?>)result, "operator", out var op))
            return;
        if (string.Equals(op, "eq", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(op, "==", StringComparison.OrdinalIgnoreCase))
        {
            result["operator"] = "gte";
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
