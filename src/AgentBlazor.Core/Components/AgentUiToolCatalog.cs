using System.Text.Json;

namespace AgentBlazor.Core.Components;

/// <summary>
/// Identifier constants for deterministic generated-UI tools.
/// </summary>
public static class AgentUiToolIds
{
    public const string SummaryCard = "summary.card";
    public const string FormDraft = "form.draft";
    public const string ActionConfirmation = "action.confirmation";
    public const string TableView = "table.view";
    public const string ChartView = "chart.view";
}

/// <summary>
/// A deterministic generated-UI tool call emitted by the planner.
/// </summary>
public sealed record AgentUiToolCall
{
    public required string ToolId { get; init; }

    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Declarative metadata about an available generated-UI tool.
/// </summary>
public sealed record AgentUiToolDescriptor
{
    public required string ToolId { get; init; }
    public required string Description { get; init; }
    public required string InputSchema { get; init; }
}

/// <summary>
/// Catalog of deterministic generated-UI tools and their block renderers.
/// </summary>
public interface IAgentUiToolCatalog
{
    IReadOnlyList<AgentUiToolDescriptor> GetTools();

    AgentUiDocument? BuildDocument(
        IReadOnlyList<AgentUiToolCall> toolCalls,
        out IReadOnlyList<string> errors);
}

public sealed class DefaultAgentUiToolCatalog : IAgentUiToolCatalog
{
    private static readonly IReadOnlyList<AgentUiToolDescriptor> Tools =
    [
        new AgentUiToolDescriptor
        {
            ToolId = AgentUiToolIds.SummaryCard,
            Description = "Render a deterministic summary card with optional follow-up action.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "blockId": { "type": "string" },
                    "title": { "type": "string" },
                    "description": { "type": "string" },
                    "actions": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "id": { "type": "string" },
                          "label": { "type": "string" },
                          "prompt": { "type": "string" },
                          "arguments": { "type": "object" }
                        },
                        "required": ["id"]
                      }
                    },
                    "actionId": { "type": "string" },
                    "actionLabel": { "type": "string" },
                    "actionPrompt": { "type": "string" }
                  },
                  "required": ["title"]
                }
                """
        },
        new AgentUiToolDescriptor
        {
            ToolId = AgentUiToolIds.FormDraft,
            Description = "Render a deterministic form draft with fields and optional follow-up actions.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "blockId": { "type": "string" },
                    "title": { "type": "string" },
                    "description": { "type": "string" },
                    "fields": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "name": { "type": "string" },
                          "label": { "type": "string" },
                          "type": { "type": "string" },
                          "placeholder": { "type": "string" },
                          "value": { "type": ["string", "number", "integer", "boolean"] },
                          "required": { "type": "boolean" }
                        },
                        "required": ["name"]
                      }
                    },
                    "actions": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "id": { "type": "string" },
                          "label": { "type": "string" },
                          "prompt": { "type": "string" },
                          "arguments": { "type": "object" }
                        },
                        "required": ["id"]
                      }
                    },
                    "actionId": { "type": "string" },
                    "actionLabel": { "type": "string" },
                    "actionPrompt": { "type": "string" }
                  }
                }
                """
        },
        new AgentUiToolDescriptor
        {
            ToolId = AgentUiToolIds.ActionConfirmation,
            Description = "Render a deterministic action confirmation card.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "blockId": { "type": "string" },
                    "title": { "type": "string" },
                    "description": { "type": "string" },
                    "actions": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "id": { "type": "string" },
                          "label": { "type": "string" },
                          "prompt": { "type": "string" },
                          "arguments": { "type": "object" }
                        },
                        "required": ["id"]
                      }
                    }
                  }
                }
                """
        },
        new AgentUiToolDescriptor
        {
            ToolId = AgentUiToolIds.TableView,
            Description = "Render a deterministic table view with optional follow-up actions.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "blockId": { "type": "string" },
                    "title": { "type": "string" },
                    "description": { "type": "string" },
                    "columns": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "key": { "type": "string" },
                          "header": { "type": "string" }
                        },
                        "required": ["key"]
                      }
                    },
                    "rows": {
                      "type": "array",
                      "items": { "type": "object" }
                    },
                    "actions": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "id": { "type": "string" },
                          "label": { "type": "string" },
                          "prompt": { "type": "string" },
                          "arguments": { "type": "object" }
                        },
                        "required": ["id"]
                      }
                    }
                  },
                  "required": ["columns"]
                }
                """
        },
        new AgentUiToolDescriptor
        {
            ToolId = AgentUiToolIds.ChartView,
            Description = "Render a deterministic chart from inline data or a named chart data source.",
            InputSchema = """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "blockId": { "type": "string" },
                    "title": { "type": "string" },
                    "description": { "type": "string" },
                    "chartType": { "type": "string", "enum": ["line", "bar", "pie"] },
                    "labels": { "type": "array", "items": { "type": "string" } },
                    "dataSource": { "type": "string" },
                    "dataArguments": { "type": "object" },
                    "series": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "name": { "type": "string" },
                          "data": { "type": "array", "items": { "type": ["number", "integer"] } }
                        },
                        "required": ["name", "data"]
                      }
                    },
                    "actions": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "additionalProperties": false,
                        "properties": {
                          "id": { "type": "string" },
                          "label": { "type": "string" },
                          "prompt": { "type": "string" },
                          "arguments": { "type": "object" }
                        },
                        "required": ["id"]
                      }
                    }
                  },
                  "anyOf": [
                    { "required": ["chartType", "labels", "series"] },
                    { "required": ["dataSource"] }
                  ]
                }
                """
        }
    ];

    public IReadOnlyList<AgentUiToolDescriptor> GetTools() => Tools;

    public AgentUiDocument? BuildDocument(
        IReadOnlyList<AgentUiToolCall> toolCalls,
        out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        if (toolCalls.Count == 0)
        {
            errors = errorList;
            return null;
        }

        var blocks = new List<AgentUiBlock>(toolCalls.Count);
        foreach (var toolCall in toolCalls)
        {
            if (string.IsNullOrWhiteSpace(toolCall.ToolId))
            {
                errorList.Add("Generated UI tool call is missing toolId.");
                continue;
            }

            var args = NormalizeArguments(toolCall.Arguments);
            if (!TryRenderTool(toolCall.ToolId, args, out var block, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    errorList.Add(error!);
                }

                continue;
            }

            if (block is null)
            {
                continue;
            }

            var uniqueBlock = EnsureUniqueBlockId(block, blocks);
            blocks.Add(uniqueBlock);
        }

        if (blocks.Count == 0)
        {
            errors = errorList;
            return null;
        }

        var document = new AgentUiDocument
        {
            Blocks = blocks
        };

        if (!document.TryValidate(out var validationError))
        {
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                errorList.Add(validationError!);
            }

            errors = errorList;
            return null;
        }

        errors = errorList;
        return document;
    }

    private static bool TryRenderTool(
        string toolId,
        IReadOnlyDictionary<string, object?> args,
        out AgentUiBlock? block,
        out string? error)
    {
        switch (toolId.Trim().ToLowerInvariant())
        {
            case AgentUiToolIds.SummaryCard:
                return TryRenderSummaryCard(args, out block, out error);
            case AgentUiToolIds.FormDraft:
                return TryRenderFormDraft(args, out block, out error);
            case AgentUiToolIds.ActionConfirmation:
                return TryRenderActionConfirmation(args, out block, out error);
            case AgentUiToolIds.TableView:
                return TryRenderTableView(args, out block, out error);
            case AgentUiToolIds.ChartView:
                return TryRenderChartView(args, out block, out error);
            default:
                block = null;
                error = $"Unknown generated UI tool '{toolId}'.";
                return false;
        }
    }

    private static bool TryRenderSummaryCard(
        IReadOnlyDictionary<string, object?> args,
        out AgentUiBlock? block,
        out string? error)
    {
        var title = ReadString(args, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            block = null;
            error = $"Tool '{AgentUiToolIds.SummaryCard}' requires non-empty 'title'.";
            return false;
        }

        var actionPrompt = ReadString(args, "actionPrompt");
        var actionLabel = ReadString(args, "actionLabel");
        var actionId = ReadString(args, "actionId");
        var actions = ReadActions(args).ToList();
        if (actions.Count == 0 && !string.IsNullOrWhiteSpace(actionPrompt))
        {
            actions.Add(new AgentUiAction
            {
                Id = string.IsNullOrWhiteSpace(actionId) ? "continue" : actionId!,
                Label = string.IsNullOrWhiteSpace(actionLabel) ? "Continue" : actionLabel!,
                Prompt = actionPrompt
            });
        }

        block = new AgentUiBlock
        {
            Id = ReadString(args, "blockId") ?? Slugify(title!, "summary-card"),
            Kind = AgentUiBlockKind.Card,
            Title = title,
            Description = ReadString(args, "description"),
            Actions = actions
        };
        error = null;
        return true;
    }

    private static bool TryRenderFormDraft(
        IReadOnlyDictionary<string, object?> args,
        out AgentUiBlock? block,
        out string? error)
    {
        var title = ReadString(args, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            block = null;
            error = $"Tool '{AgentUiToolIds.FormDraft}' requires non-empty 'title'.";
            return false;
        }

        var fields = ReadFields(args);
        if (fields.Count == 0)
        {
            block = null;
            error = $"Tool '{AgentUiToolIds.FormDraft}' requires at least one valid entry in 'fields'.";
            return false;
        }

        var actionPrompt = ReadString(args, "actionPrompt");
        var actionLabel = ReadString(args, "actionLabel");
        var actionId = ReadString(args, "actionId");
        var actions = ReadActions(args).ToList();
        if (!string.IsNullOrWhiteSpace(actionPrompt))
        {
            actions.Add(new AgentUiAction
            {
                Id = string.IsNullOrWhiteSpace(actionId) ? "applyDraft" : actionId!,
                Label = string.IsNullOrWhiteSpace(actionLabel) ? "Apply draft" : actionLabel!,
                Prompt = actionPrompt
            });
        }

        block = new AgentUiBlock
        {
            Id = ReadString(args, "blockId") ?? Slugify(title!, "form-draft"),
            Kind = AgentUiBlockKind.Form,
            Title = title,
            Description = ReadString(args, "description"),
            Fields = fields,
            Actions = actions
        };
        error = null;
        return true;
    }

    private static bool TryRenderActionConfirmation(
        IReadOnlyDictionary<string, object?> args,
        out AgentUiBlock? block,
        out string? error)
    {
        var title = ReadString(args, "title") ?? "Action Completed";
        var description = ReadString(args, "description") ?? "The requested action has been applied.";

        block = new AgentUiBlock
        {
            Id = ReadString(args, "blockId") ?? Slugify(title, "action-confirmation"),
            Kind = AgentUiBlockKind.Card,
            Title = title,
            Description = description,
            Actions = ReadActions(args)
        };
        error = null;
        return true;
    }

    private static bool TryRenderTableView(
        IReadOnlyDictionary<string, object?> args,
        out AgentUiBlock? block,
        out string? error)
    {
        var columns = ReadColumns(args);
        if (columns.Count == 0)
        {
            block = null;
            error = $"Tool '{AgentUiToolIds.TableView}' requires at least one valid entry in 'columns'.";
            return false;
        }

        var title = ReadString(args, "title") ?? "Data Table";
        block = new AgentUiBlock
        {
            Id = ReadString(args, "blockId") ?? Slugify(title, "table-view"),
            Kind = AgentUiBlockKind.Table,
            Title = title,
            Description = ReadString(args, "description"),
            Columns = columns,
            Rows = ReadRows(args),
            Actions = ReadActions(args)
        };
        error = null;
        return true;
    }

    private static bool TryRenderChartView(
        IReadOnlyDictionary<string, object?> args,
        out AgentUiBlock? block,
        out string? error)
    {
        var chartType = ReadChartType(args);
        var labels = ReadStringList(args, "labels");
        var series = ReadChartSeries(args);
        var dataSource = ReadString(args, "dataSource");
        var dataArguments = ReadObjectDictionary(args, "dataArguments");
        var hasInlineData = labels.Count > 0 || series.Count > 0;

        if (!hasInlineData && string.IsNullOrWhiteSpace(dataSource))
        {
            block = null;
            error = $"Tool '{AgentUiToolIds.ChartView}' requires inline chart data or non-empty 'dataSource'.";
            return false;
        }

        if (hasInlineData)
        {
            if (chartType is null)
            {
                block = null;
                error = $"Tool '{AgentUiToolIds.ChartView}' requires a valid 'chartType' (line, bar, pie) for inline data.";
                return false;
            }

            if (labels.Count == 0)
            {
                block = null;
                error = $"Tool '{AgentUiToolIds.ChartView}' requires at least one label in 'labels' for inline data.";
                return false;
            }

            if (series.Count == 0)
            {
                block = null;
                error = $"Tool '{AgentUiToolIds.ChartView}' requires at least one series in 'series' for inline data.";
                return false;
            }

            foreach (var entry in series)
            {
                if (entry.Data.Count != labels.Count)
                {
                    block = null;
                    error = $"Tool '{AgentUiToolIds.ChartView}' series '{entry.Name}' must have {labels.Count} data points.";
                    return false;
                }
            }
        }

        var title = ReadString(args, "title") ?? "Chart";
        block = new AgentUiBlock
        {
            Id = ReadString(args, "blockId") ?? Slugify(title, "chart-view"),
            Kind = AgentUiBlockKind.Chart,
            Title = title,
            Description = ReadString(args, "description"),
            ChartType = chartType,
            ChartLabels = labels,
            ChartSeries = series,
            ChartDataSource = dataSource,
            ChartDataArguments = dataArguments,
            Actions = ReadActions(args)
        };
        error = null;
        return true;
    }

    private static IReadOnlyList<AgentUiAction> ReadActions(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("actions", out var rawActions) || rawActions is not IEnumerable<object?> actionObjects)
        {
            return [];
        }

        var actions = new List<AgentUiAction>();
        foreach (var rawAction in actionObjects)
        {
            if (rawAction is not IReadOnlyDictionary<string, object?> action)
            {
                continue;
            }

            var id = ReadString(action, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var label = ReadString(action, "label") ?? id;
            var arguments = action.TryGetValue("arguments", out var rawArguments) &&
                            rawArguments is IReadOnlyDictionary<string, object?> dictionary
                ? dictionary
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            actions.Add(new AgentUiAction
            {
                Id = id,
                Label = label,
                Prompt = ReadString(action, "prompt"),
                Arguments = arguments
            });
        }

        return actions;
    }

    private static IReadOnlyList<AgentUiField> ReadFields(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("fields", out var rawFields) || rawFields is not IEnumerable<object?> fieldObjects)
        {
            return [];
        }

        var fields = new List<AgentUiField>();
        foreach (var rawField in fieldObjects)
        {
            if (rawField is not IReadOnlyDictionary<string, object?> field)
            {
                continue;
            }

            var name = ReadString(field, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.Add(new AgentUiField
            {
                Name = name,
                Label = ReadString(field, "label") ?? name,
                Type = ReadString(field, "type") ?? "text",
                Placeholder = ReadString(field, "placeholder"),
                Value = ReadString(field, "value"),
                Required = ReadBool(field, "required")
            });
        }

        return fields;
    }

    private static IReadOnlyList<AgentUiTableColumn> ReadColumns(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("columns", out var rawColumns) || rawColumns is not IEnumerable<object?> columnObjects)
        {
            return [];
        }

        var columns = new List<AgentUiTableColumn>();
        foreach (var rawColumn in columnObjects)
        {
            if (rawColumn is not IReadOnlyDictionary<string, object?> column)
            {
                continue;
            }

            var key = ReadString(column, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            columns.Add(new AgentUiTableColumn
            {
                Key = key,
                Header = ReadString(column, "header") ?? key
            });
        }

        return columns;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadRows(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("rows", out var rawRows) || rawRows is not IEnumerable<object?> rowObjects)
        {
            return [];
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var rawRow in rowObjects)
        {
            if (rawRow is not IReadOnlyDictionary<string, object?> row)
            {
                continue;
            }

            rows.Add(new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase));
        }

        return rows;
    }

    private static IReadOnlyList<string> ReadStringList(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var rawValues) || rawValues is not IEnumerable<object?> values)
        {
            return [];
        }

        var results = new List<string>();
        foreach (var value in values)
        {
            var text = value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(text);
            }
        }

        return results;
    }

    private static IReadOnlyList<double> ReadNumberList(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var rawValues) || rawValues is not IEnumerable<object?> values)
        {
            return [];
        }

        var results = new List<double>();
        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            if (value is double number)
            {
                results.Add(number);
                continue;
            }

            if (value is float floatValue)
            {
                results.Add(floatValue);
                continue;
            }

            if (value is int intValue)
            {
                results.Add(intValue);
                continue;
            }

            if (value is long longValue)
            {
                results.Add(longValue);
                continue;
            }

            if (value is decimal decimalValue)
            {
                results.Add((double)decimalValue);
                continue;
            }

            if (value is string text && double.TryParse(text, out var parsed))
            {
                results.Add(parsed);
            }
        }

        return results;
    }

    private static IReadOnlyDictionary<string, object?> ReadObjectDictionary(
        IReadOnlyDictionary<string, object?> args,
        string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (raw is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly;
        }

        if (raw is IDictionary<string, object?> mutable)
        {
            return new Dictionary<string, object?>(mutable, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AgentUiChartSeries> ReadChartSeries(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("series", out var rawSeries) || rawSeries is not IEnumerable<object?> seriesValues)
        {
            return [];
        }

        var results = new List<AgentUiChartSeries>();
        foreach (var rawSeriesEntry in seriesValues)
        {
            if (rawSeriesEntry is not IReadOnlyDictionary<string, object?> series)
            {
                continue;
            }

            var name = ReadString(series, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var data = ReadNumberList(series, "data");
            if (data.Count == 0)
            {
                continue;
            }

            results.Add(new AgentUiChartSeries
            {
                Name = name,
                Data = data
            });
        }

        return results;
    }

    private static AgentUiChartType? ReadChartType(IReadOnlyDictionary<string, object?> args)
    {
        var raw = ReadString(args, "chartType");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "line" => AgentUiChartType.Line,
            "bar" => AgentUiChartType.Bar,
            "pie" => AgentUiChartType.Pie,
            _ => null
        };
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        return raw switch
        {
            bool b => b,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        var text = raw.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static AgentUiBlock EnsureUniqueBlockId(AgentUiBlock block, IReadOnlyList<AgentUiBlock> blocks)
    {
        if (!blocks.Any(existing =>
                string.Equals(existing.Id, block.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return block;
        }

        var suffix = 2;
        var candidate = $"{block.Id}-{suffix}";
        while (blocks.Any(existing =>
                   string.Equals(existing.Id, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            suffix++;
            candidate = $"{block.Id}-{suffix}";
        }

        return block with { Id = candidate };
    }

    private static IReadOnlyDictionary<string, object?> NormalizeArguments(IReadOnlyDictionary<string, object?> args)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in args)
        {
            normalized[pair.Key] = Unwrap(pair.Value);
        }

        return normalized;
    }

    private static object? Unwrap(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            JsonElement element => UnwrapJsonElement(element),
            IReadOnlyDictionary<string, object?> dictionary => dictionary.ToDictionary(
                static pair => pair.Key,
                static pair => Unwrap(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                static pair => pair.Key,
                static pair => Unwrap(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            IEnumerable<object?> sequence => sequence.Select(Unwrap).ToArray(),
            _ => raw
        };
    }

    private static object? UnwrapJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var int64) => int64,
            JsonValueKind.Number when element.TryGetDouble(out var @double) => @double,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(UnwrapJsonElement).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => UnwrapJsonElement(property.Value),
                StringComparer.OrdinalIgnoreCase),
            _ => null
        };
    }

    private static string Slugify(string source, string fallback)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return fallback;
        }

        var chars = source
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }
}
