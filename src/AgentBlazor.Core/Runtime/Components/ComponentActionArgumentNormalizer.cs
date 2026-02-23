using System.Text.Json;
using AgentBlazor.Components;

namespace AgentBlazor.Core.Runtime.Components;

public static class ComponentActionArgumentNormalizer
{
    public static IReadOnlyDictionary<string, object?> Normalize(
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        string? reason = null)
    {
        _ = reason;

        if (arguments is null || arguments.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in arguments)
        {
            normalized[pair.Key] = JsonArgumentHelper.Unwrap(pair.Value);
        }

        ApplyCanonicalAliases(componentId, actionId, normalized);
        ApplyTypeCoercions(componentId, actionId, normalized);
        return normalized;
    }

    private static void ApplyCanonicalAliases(
        string componentId,
        string actionId,
        IDictionary<string, object?> arguments)
    {
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
            {
                PromoteAlias(arguments, "column", "field", "property");
                PromoteAlias(arguments, "operator", "op", "comparison");
                PromoteAlias(arguments, "value", "fieldValue", "threshold", "query", "term");
                return;
            }

            if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
            {
                PromoteAlias(arguments, "column", "field", "property");
                PromoteAlias(arguments, "direction", "order", "sortDirection");
                if (!HasValue(arguments, "direction") &&
                    TryGetNonEmptyString(arguments, out var directionAlias, "operator", "op", "comparison") &&
                    IsSortDirection(directionAlias))
                {
                    arguments["direction"] = NormalizeSortDirection(directionAlias)!;
                }

                return;
            }

            if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSetPageActionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, AgentComponentCapabilityProfile.DataGridGoToPageActionId, StringComparison.OrdinalIgnoreCase))
            {
                PromoteAlias(arguments, "pageIndex", "index", "page");
                PromoteAlias(arguments, "pageSize", "size", "limit");
                return;
            }

            if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridNavigateToRowActionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSelectRowActionId, StringComparison.OrdinalIgnoreCase))
            {
                PromoteAlias(arguments, "rowKey", "row", "rowId", "id", "key");
            }

            return;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(actionId, AgentComponentCapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(actionId, AgentComponentCapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase)))
        {
            PromoteAlias(arguments, "uri", "url", "target", "route", "path", "destination", "href");
            return;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase))
        {
            PromoteAlias(arguments, "index", "tab", "tabIndex", "value", "page", "panel");
            return;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase))
        {
            PromoteAlias(arguments, "field", "column", "name", "property", "key");
            PromoteAlias(arguments, "value", "fieldValue", "input", "field_value");
        }
    }

    private static void ApplyTypeCoercions(
        string componentId,
        string actionId,
        IDictionary<string, object?> arguments)
    {
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase) &&
            arguments.TryGetValue("index", out var indexRaw) &&
            TryParseIndex(indexRaw, out var parsedIndex))
        {
            arguments["index"] = parsedIndex;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
        {
            CoerceBoolean(arguments, "replaceHistory");
            CoerceBoolean(arguments, "openInNewTab");
        }
    }

    private static void PromoteAlias(
        IDictionary<string, object?> arguments,
        string canonicalKey,
        params string[] aliases)
    {
        if (HasValue(arguments, canonicalKey))
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (!HasValue(arguments, alias))
            {
                continue;
            }

            arguments[canonicalKey] = arguments[alias];
            return;
        }
    }

    private static bool HasValue(IDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var raw))
        {
            return false;
        }

        return raw switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };
    }

    private static bool TryGetNonEmptyString(
        IDictionary<string, object?> arguments,
        out string value,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            var text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void CoerceBoolean(IDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var raw) || raw is null)
        {
            return;
        }

        if (raw is bool)
        {
            return;
        }

        if (bool.TryParse(raw.ToString(), out var parsed))
        {
            arguments[key] = parsed;
        }
    }

    private static bool TryParseIndex(object? raw, out int index)
    {
        index = 0;
        if (raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case int i:
                index = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                index = (int)l;
                return true;
            case double d when d >= int.MinValue && d <= int.MaxValue:
                index = (int)d;
                return true;
        }

        var text = raw.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (int.TryParse(text, out var parsed))
        {
            index = parsed;
            return true;
        }

        if (TryParseOrdinalIndex(text, out parsed))
        {
            index = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseOrdinalIndex(string value, out int index)
    {
        index = 0;
        var normalized = value.Trim().ToLowerInvariant();
        index = normalized switch
        {
            "first" => 0,
            "second" => 1,
            "third" => 2,
            "fourth" => 3,
            "fifth" => 4,
            _ => -1
        };

        return index >= 0;
    }

    private static bool IsSortDirection(string value) =>
        string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "ascending", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "descending", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSortDirection(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "asc" or "ascending" => "asc",
            "desc" or "descending" => "desc",
            _ => null
        };
}

internal static class JsonArgumentHelper
{
    public static object? Unwrap(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            JsonElement element => UnwrapJsonElement(element),
            IReadOnlyDictionary<string, object?> dictionary => UnwrapDictionary(dictionary),
            IDictionary<string, object?> dictionary => UnwrapDictionary(dictionary),
            IEnumerable<object?> sequence => sequence.Select(Unwrap).ToArray(),
            System.Collections.IEnumerable sequence when raw is not string => UnwrapEnumerable(sequence),
            _ => raw
        };
    }

    private static object? UnwrapJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var int32) => int32,
            JsonValueKind.Number when element.TryGetInt64(out var int64) => int64,
            JsonValueKind.Number when element.TryGetDouble(out var @double) => @double,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => element.EnumerateArray().Select(UnwrapJsonElement).ToArray(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    static property => property.Name,
                    static property => UnwrapJsonElement(property.Value),
                    StringComparer.OrdinalIgnoreCase),
            _ => element.ToString()
        };
    }

    private static Dictionary<string, object?> UnwrapDictionary(
        IEnumerable<KeyValuePair<string, object?>> source)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            normalized[pair.Key] = Unwrap(pair.Value);
        }

        return normalized;
    }

    private static object?[] UnwrapEnumerable(System.Collections.IEnumerable source)
    {
        var values = new List<object?>();
        foreach (var item in source)
        {
            values.Add(Unwrap(item));
        }

        return values.ToArray();
    }
}
