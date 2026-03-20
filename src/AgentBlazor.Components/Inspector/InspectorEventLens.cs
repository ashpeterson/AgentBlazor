using System.Text.Json;
using AgentBlazor.Core.Paid;

namespace AgentBlazor.Components.Inspector;

public static class InspectorEventLens
{
    public sealed record JsonEntry(string Key, string ValuePreview);

    public static string ClassifyPhase(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "other";
        }

        if (kind.Equals("PlanningStarted", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("PlanningFinished", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("PlannedAction", StringComparison.OrdinalIgnoreCase))
        {
            return "planning";
        }

        if (kind.Equals("ValidationStarted", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ValidationPassed", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ValidationFailed", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ApprovalRequired", StringComparison.OrdinalIgnoreCase))
        {
            return "validation";
        }

        if (kind.Equals("ExecutionStarted", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ExecutionFinished", StringComparison.OrdinalIgnoreCase))
        {
            return "execution";
        }

        if (kind.Equals("StateSnapshot", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("StateDelta", StringComparison.OrdinalIgnoreCase))
        {
            return "state";
        }

        if (kind.Equals("AgentHandoff", StringComparison.OrdinalIgnoreCase))
        {
            return "handoff";
        }

        if (kind.Equals("RunStarted", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("RunFinished", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("RunError", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("RunCanceled", StringComparison.OrdinalIgnoreCase))
        {
            return "run";
        }

        if (kind.Equals("ToolCallStart", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ToolCallResult", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ToolCallFailed", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("TextMessageStart", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("TextMessageContent", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("TextMessageEnd", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("ClarificationRequired", StringComparison.OrdinalIgnoreCase))
        {
            return "stream";
        }

        return "other";
    }

    public static bool IsStreamKind(string? kind)
        => string.Equals(ClassifyPhase(kind), "stream", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ExtractJsonTopLevelKeys(string? detail, int maxKeys = 8)
    {
        if (string.IsNullOrWhiteSpace(detail) || maxKeys <= 0)
        {
            return [];
        }

        var trimmed = detail.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return [];
            }

            return doc.RootElement
                .EnumerateObject()
                .Select(static property => property.Name)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Take(maxKeys)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<JsonEntry> ExtractJsonTopLevelEntries(
        string? detail,
        int maxEntries = 6,
        int maxValueLength = 40)
    {
        if (string.IsNullOrWhiteSpace(detail) || maxEntries <= 0)
        {
            return [];
        }

        var trimmed = detail.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return [];
            }

            var result = new List<JsonEntry>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (result.Count >= maxEntries)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    continue;
                }

                var preview = BuildValuePreview(property.Value, maxValueLength);
                result.Add(new JsonEntry(property.Name, preview));
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<JsonEntry> ExtractJsonLeafPaths(
        string? detail,
        int maxEntries = 8,
        int maxValueLength = 36,
        int maxDepth = 4,
        int maxArrayItemsPerNode = 3)
    {
        if (string.IsNullOrWhiteSpace(detail) || maxEntries <= 0)
        {
            return [];
        }

        var trimmed = detail.Trim();
        if (!(trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) &&
            !(trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var result = new List<JsonEntry>(Math.Min(maxEntries, 16));
            Visit(doc.RootElement, path: "$", depth: 0, result, maxEntries, maxValueLength, maxDepth, maxArrayItemsPerNode);
            return result;
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<InspectorEventGroup> GroupByPhase(IReadOnlyList<InspectorEvent> events)
    {
        if (events.Count == 0)
        {
            return [];
        }

        var grouped = events
            .GroupBy(ev => ClassifyPhase(ev.Kind), StringComparer.OrdinalIgnoreCase)
            .Select(group => new InspectorEventGroup(
                Phase: group.Key,
                Label: GetPhaseLabel(group.Key),
                Events: group.ToList()))
            .OrderBy(group => GetPhaseSortOrder(group.Phase))
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return grouped;
    }

    private static int GetPhaseSortOrder(string phase)
    {
        return phase.ToLowerInvariant() switch
        {
            "planning" => 0,
            "validation" => 1,
            "execution" => 2,
            "state" => 3,
            "handoff" => 4,
            "stream" => 5,
            "run" => 6,
            _ => 7
        };
    }

    public static string GetPhaseLabel(string phase)
    {
        return phase.ToLowerInvariant() switch
        {
            "planning" => "Workflow Planning",
            "validation" => "Approval and Validation",
            "execution" => "Workflow Execution",
            "state" => "State",
            "handoff" => "Handoff",
            "stream" => "Stream",
            "run" => "Run",
            _ => "Other"
        };
    }

    private static string BuildValuePreview(JsonElement value, int maxValueLength)
    {
        var preview = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "(null)",
            JsonValueKind.Null => "(null)",
            JsonValueKind.Object => "{...}",
            JsonValueKind.Array => "[...]",
            _ => value.ToString()
        };

        if (preview.Length <= maxValueLength || maxValueLength <= 3)
        {
            return preview;
        }

        return preview[..(maxValueLength - 3)] + "...";
    }

    private static void Visit(
        JsonElement element,
        string path,
        int depth,
        List<JsonEntry> result,
        int maxEntries,
        int maxValueLength,
        int maxDepth,
        int maxArrayItemsPerNode)
    {
        if (result.Count >= maxEntries)
        {
            return;
        }

        if (depth > maxDepth)
        {
            result.Add(new JsonEntry(path, "(max-depth)"));
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (result.Count >= maxEntries)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(property.Name))
                    {
                        continue;
                    }

                    var childPath = path == "$"
                        ? $"$.{property.Name}"
                        : $"{path}.{property.Name}";
                    Visit(property.Value, childPath, depth + 1, result, maxEntries, maxValueLength, maxDepth, maxArrayItemsPerNode);
                }

                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (result.Count >= maxEntries)
                    {
                        break;
                    }

                    if (index >= maxArrayItemsPerNode)
                    {
                        result.Add(new JsonEntry($"{path}[...]", "(truncated)"));
                        break;
                    }

                    Visit(item, $"{path}[{index}]", depth + 1, result, maxEntries, maxValueLength, maxDepth, maxArrayItemsPerNode);
                    index++;
                }

                break;
            }
            default:
                result.Add(new JsonEntry(path, BuildValuePreview(element, maxValueLength)));
                break;
        }
    }
}

public sealed record InspectorEventGroup(
    string Phase,
    string Label,
    IReadOnlyList<InspectorEvent> Events);
