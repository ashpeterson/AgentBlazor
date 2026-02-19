using AgentBlazor.Components;
using System.Text.RegularExpressions;

namespace AgentBlazor.Runtime;

public static class ComponentActionArgumentNormalizer
{
    public static IReadOnlyDictionary<string, object?> Normalize(
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        string? reason = null)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (arguments is not null)
        {
            foreach (var pair in arguments)
            {
                normalized[pair.Key] = NormalizeValue(pair.Value);
            }
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            NormalizeDataGrid(actionId, normalized, reason);
        }
        else if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            NormalizeForm(actionId, normalized, reason);
        }
        else if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
        {
            NormalizeNavigation(actionId, normalized, reason);
        }
        else if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase))
        {
            NormalizeTabs(actionId, normalized, reason);
        }

        return normalized;
    }

    private static void NormalizeDataGrid(string actionId, Dictionary<string, object?> arguments, string? reason)
    {
        var intent = ResolveIntent(arguments, reason);
        if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
        {
            MapFirst(arguments, "column", "field", "property", "columnName", "filterColumn", "currentFilterColumn", "sortColumn", "currentSortColumn");
            MapFirst(arguments, "operator", "op", "comparison");
            MapFirst(arguments, "value", "fieldValue", "threshold", "query", "term");

            if (TryGetString(arguments, "column", out var column))
            {
                arguments["column"] = NormalizeColumnName(column);
            }

            if (TryGetString(arguments, "operator", out var op))
            {
                arguments["operator"] = NormalizeOperator(op);
            }

            InferDataGridFilterArguments(arguments, intent);
            return;
        }

        if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
        {
            MapFirst(arguments, "column", "field", "property", "columnName", "sortBy", "sortColumn", "currentSortColumn", "filterColumn", "currentFilterColumn");
            MapFirst(arguments, "direction", "order", "sortDirection");

            if (TryGetString(arguments, "column", out var column))
            {
                arguments["column"] = NormalizeColumnName(column);
            }

            if (TryGetString(arguments, "direction", out var direction))
            {
                arguments["direction"] = NormalizeSortDirection(direction);
            }

            InferDataGridSortArguments(arguments, intent);
            return;
        }

        if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridGoToPageActionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSetPageActionId, StringComparison.OrdinalIgnoreCase))
        {
            MapFirst(arguments, "pageIndex", "page", "pageNumber", "index");
            MapFirst(arguments, "pageSize", "size", "limit");
            CoerceInt(arguments, "pageIndex");
            CoerceInt(arguments, "pageSize");
            if (!arguments.ContainsKey("pageIndex") && TryInferPageIndex(intent, out var pageIndex))
            {
                arguments["pageIndex"] = pageIndex;
            }

            return;
        }

        if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSelectRowActionId, StringComparison.OrdinalIgnoreCase))
        {
            MapFirst(arguments, "rowKey", "row", "rowId", "id", "key", "supplierId");
            if (!arguments.ContainsKey("rowKey") && TryInferSupplierRowKey(intent, out var supplierId))
            {
                arguments["rowKey"] = supplierId;
            }
        }
    }

    private static void NormalizeForm(string actionId, Dictionary<string, object?> arguments, string? reason)
    {
        if (!string.Equals(actionId, AgentComponentV1CapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MapFirst(arguments, "field", "name", "key", "property", "column");
        MapFirst(arguments, "value", "fieldValue", "input", "newValue");

        var intent = ResolveIntent(arguments, reason);
        if (TryInferSetFieldFromIntent(intent, out var inferredField, out var inferredValue))
        {
            if (!arguments.ContainsKey("field") && !string.IsNullOrWhiteSpace(inferredField))
            {
                arguments["field"] = inferredField;
            }

            if (!arguments.ContainsKey("value") && inferredValue is not null)
            {
                arguments["value"] = inferredValue;
            }
        }
    }

    private static void NormalizeNavigation(string actionId, Dictionary<string, object?> arguments, string? reason)
    {
        if (!string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MapFirst(arguments, "uri", "url", "target", "path", "route", "href", "currentUri");
        if (TryGetString(arguments, "uri", out var uri))
        {
            arguments["uri"] = uri.Trim();
            return;
        }

        var intent = ResolveIntent(arguments, reason);
        if (TryInferUriFromIntent(intent, out var inferredUri))
        {
            arguments["uri"] = inferredUri;
        }
    }

    private static void NormalizeTabs(string actionId, Dictionary<string, object?> arguments, string? reason)
    {
        if (!string.Equals(actionId, AgentComponentV1CapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        MapFirst(arguments, "index", "tab", "tabIndex", "activeTab", "currentIndex", "activePanelIndex");
        CoerceInt(arguments, "index");
        if (arguments.ContainsKey("index"))
        {
            return;
        }

        var intent = ResolveIntent(arguments, reason);
        if (TryInferTabIndex(intent, out var inferredIndex))
        {
            arguments["index"] = inferredIndex;
        }
    }

    private static void MapFirst(Dictionary<string, object?> arguments, string canonicalKey, params string[] aliases)
    {
        if (arguments.TryGetValue(canonicalKey, out var canonicalValue) && canonicalValue is not null)
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (arguments.TryGetValue(alias, out var value) && value is not null)
            {
                arguments[canonicalKey] = value;
                return;
            }
        }
    }

    private static void CoerceInt(Dictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var raw) || raw is null)
        {
            return;
        }

        switch (raw)
        {
            case int:
                return;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                arguments[key] = (int)l;
                return;
            case string s when int.TryParse(s, out var parsed):
                arguments[key] = parsed;
                return;
            case double d when d is >= int.MinValue and <= int.MaxValue:
                arguments[key] = (int)d;
                return;
        }
    }

    private static bool TryGetString(Dictionary<string, object?> arguments, string key, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static string NormalizeSortDirection(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "ascending" or "up" => "asc",
            "descending" or "down" => "desc",
            var normalized => normalized
        };

    private static string NormalizeOperator(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "equal" or "equals" or "=" => "eq",
            "notequal" or "not_equals" => "neq",
            "greater_than" or "greaterthan" or "high" or "highest" or "top" or "max" => ">=",
            "less_than" or "lessthan" or "low" or "lowest" or "bottom" or "min" => "<=",
            var normalized => normalized
        };

    private static void InferDataGridFilterArguments(
        Dictionary<string, object?> arguments,
        string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return;
        }

        if (!TryGetString(arguments, "column", out var column) &&
            TryInferDataGridColumn(intent, out var inferredColumn))
        {
            column = inferredColumn;
            arguments["column"] = column;
        }

        if (!TryGetString(arguments, "operator", out var filterOperator) &&
            TryInferRiskOperator(intent, out var inferredOperator))
        {
            filterOperator = inferredOperator;
            arguments["operator"] = filterOperator;
        }

        if (!arguments.TryGetValue("value", out var rawValue) || rawValue is null)
        {
            if (string.Equals(column, "RiskScore", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(filterOperator))
            {
                if (TryExtractFirstInteger(intent, out var threshold))
                {
                    arguments["value"] = threshold;
                    return;
                }

                if (IsHighIntent(filterOperator, intent))
                {
                    arguments["value"] = 70;
                }
                else if (IsLowIntent(filterOperator, intent))
                {
                    arguments["value"] = 30;
                }
            }
        }
    }

    private static void InferDataGridSortArguments(
        Dictionary<string, object?> arguments,
        string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return;
        }

        if (!TryGetString(arguments, "column", out var column) &&
            TryInferDataGridColumn(intent, out var inferredColumn))
        {
            arguments["column"] = inferredColumn;
        }

        if (TryGetString(arguments, "direction", out _))
        {
            return;
        }

        if (TryInferSortDirection(intent, out var inferredDirection))
        {
            arguments["direction"] = inferredDirection;
        }
    }

    private static bool TryInferDataGridColumn(string intent, out string column)
    {
        if (ContainsAny(intent, "risk score", "risk", "highest risk", "lowest risk"))
        {
            column = "RiskScore";
            return true;
        }

        if (ContainsAny(intent, "region"))
        {
            column = "Region";
            return true;
        }

        if (ContainsAny(intent, "last audit", "audit date"))
        {
            column = "LastAuditDate";
            return true;
        }

        if (ContainsAny(intent, "supplier id", "sup-"))
        {
            column = "SupplierId";
            return true;
        }

        if (ContainsAny(intent, "supplier", "name"))
        {
            column = "Name";
            return true;
        }

        column = string.Empty;
        return false;
    }

    private static bool TryInferRiskOperator(string intent, out string @operator)
    {
        if (ContainsAny(intent, "lowest", "low risk", "least risk", "minimum risk", "lowest risk", "bottom risk"))
        {
            @operator = "<=";
            return true;
        }

        if (ContainsAny(intent, "highest", "high risk", "most risk", "maximum risk", "highest risk", "top risk"))
        {
            @operator = ">=";
            return true;
        }

        @operator = string.Empty;
        return false;
    }

    private static bool TryInferSortDirection(string intent, out string direction)
    {
        if (ContainsAny(intent, "highest to lowest", "high to low", "descending"))
        {
            direction = "desc";
            return true;
        }

        if (ContainsAny(intent, "lowest to highest", "low to high", "ascending"))
        {
            direction = "asc";
            return true;
        }

        if (ContainsAny(intent, "ascending", "lowest", "low to high", "lowest to highest", "smallest first", "least first"))
        {
            direction = "asc";
            return true;
        }

        if (ContainsAny(intent, "descending", "highest", "high to low", "highest to lowest", "largest first", "most first"))
        {
            direction = "desc";
            return true;
        }

        direction = string.Empty;
        return false;
    }

    private static bool TryInferSetFieldFromIntent(
        string? intent,
        out string field,
        out object? value)
    {
        field = string.Empty;
        value = null;

        if (string.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        var match = Regex.Match(
            intent,
            @"(?:set|update|change)\s+(?<field>[a-zA-Z][a-zA-Z0-9_\s-]{1,48})\s*(?:to|=)\s*(?<value>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        field = NormalizeFieldName(match.Groups["field"].Value);
        var raw = match.Groups["value"].Value.Trim().Trim('"', '\'');
        if (raw.Length == 0)
        {
            return false;
        }

        if (int.TryParse(raw, out var i))
        {
            value = i;
        }
        else if (double.TryParse(raw, out var d))
        {
            value = d;
        }
        else if (bool.TryParse(raw, out var b))
        {
            value = b;
        }
        else
        {
            value = raw;
        }

        return field.Length > 0;
    }

    private static bool TryInferUriFromIntent(string? intent, out string uri)
    {
        uri = string.Empty;
        if (string.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        var webMatch = Regex.Match(
            intent,
            @"https?://[^\s""']+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (webMatch.Success)
        {
            uri = webMatch.Value;
            return true;
        }

        var routeMatch = Regex.Match(
            intent,
            @"(?<!\w)(/[a-zA-Z0-9_/\-]+)",
            RegexOptions.CultureInvariant);
        if (routeMatch.Success)
        {
            uri = routeMatch.Groups[1].Value;
            return true;
        }

        if (ContainsAny(intent, "suppliers"))
        {
            uri = "/suppliers";
            return true;
        }

        if (ContainsAny(intent, "settings"))
        {
            uri = "/settings";
            return true;
        }

        if (ContainsAny(intent, "home", "dashboard"))
        {
            uri = "/";
            return true;
        }

        return false;
    }

    private static bool TryInferTabIndex(string? intent, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        if (ContainsAny(intent, "first tab", "tab one", "tab 1"))
        {
            index = 0;
            return true;
        }

        if (ContainsAny(intent, "second tab", "tab two", "tab 2"))
        {
            index = 1;
            return true;
        }

        if (ContainsAny(intent, "third tab", "tab three", "tab 3"))
        {
            index = 2;
            return true;
        }

        if (ContainsAny(intent, "fourth tab", "tab four", "tab 4"))
        {
            index = 3;
            return true;
        }

        var indexMatch = Regex.Match(
            intent,
            @"\bindex\s*(?<index>\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (indexMatch.Success && int.TryParse(indexMatch.Groups["index"].Value, out var parsed))
        {
            index = parsed;
            return true;
        }

        return false;
    }

    private static bool TryInferPageIndex(string? intent, out int pageIndex)
    {
        pageIndex = 0;
        if (string.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        var pageMatch = Regex.Match(
            intent,
            @"\bpage\s*(?<page>\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!pageMatch.Success || !int.TryParse(pageMatch.Groups["page"].Value, out var oneBased))
        {
            return false;
        }

        pageIndex = Math.Max(0, oneBased - 1);
        return true;
    }

    private static bool TryInferSupplierRowKey(string? intent, out string rowKey)
    {
        rowKey = string.Empty;
        if (string.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        var supplierMatch = Regex.Match(
            intent,
            @"\bSUP[-\s]?(?<id>\d{3,})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!supplierMatch.Success)
        {
            return false;
        }

        rowKey = $"SUP-{supplierMatch.Groups["id"].Value}";
        return true;
    }

    private static bool TryExtractFirstInteger(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Regex.Match(text, @"\b(?<value>\d{1,3})\b", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["value"].Value, out value);
    }

    private static bool IsHighIntent(string filterOperator, string intent)
        => filterOperator is ">" or ">=" or "gt" or "gte" ||
           ContainsAny(intent, "highest", "high risk", "most risk", "top risk");

    private static bool IsLowIntent(string filterOperator, string intent)
        => filterOperator is "<" or "<=" or "lt" or "lte" ||
           ContainsAny(intent, "lowest", "low risk", "least risk", "bottom risk");

    private static string NormalizeFieldName(string raw)
    {
        var parts = raw
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant());
        return string.Concat(parts);
    }

    private static string NormalizeColumnName(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var key = Regex.Replace(normalized, @"[\s_\-]", string.Empty, RegexOptions.CultureInvariant)
            .ToLowerInvariant();
        return key switch
        {
            "riskscore" => "RiskScore",
            "region" => "Region",
            "name" or "supplier" or "suppliername" => "Name",
            "supplierid" => "SupplierId",
            "lastaudit" or "lastauditdate" => "LastAuditDate",
            _ => normalized
        };
    }

    private static string? ResolveIntent(Dictionary<string, object?> arguments, string? reason)
    {
        if (TryGetString(arguments, "intent", out var intent))
        {
            return intent;
        }

        if (TryGetString(arguments, "prompt", out var prompt))
        {
            return prompt;
        }

        if (TryGetString(arguments, "request", out var request))
        {
            return request;
        }

        if (TryGetString(arguments, "message", out var message))
        {
            return message;
        }

        if (TryGetString(arguments, "reason", out var reasonValue))
        {
            return reasonValue;
        }

        return reason;
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static object? NormalizeValue(object? raw)
    {
        if (raw is System.Text.Json.JsonElement json)
        {
            return json.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => json.GetString(),
                System.Text.Json.JsonValueKind.Number when json.TryGetInt64(out var i64) => i64,
                System.Text.Json.JsonValueKind.Number when json.TryGetDouble(out var d) => d,
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => json.ToString()
            };
        }

        return raw;
    }
}
