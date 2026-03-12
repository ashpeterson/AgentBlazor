using System.Text.Json;

namespace AgentBlazor.Core.Components;

/// <summary>
/// Imports declarative UI payloads from external schemas into the native
/// <see cref="AgentUiDocument"/> model used by AgentBlazor renderers.
/// </summary>
public static class AgentUiInterchangeAdapters
{
    public static AgentUiDocument? FromA2UiJsonLines(
        string jsonLines,
        out IReadOnlyList<string> diagnostics)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(jsonLines))
        {
            diagnostics = ["A2UI payload is empty."];
            return null;
        }

        var surfaces = new Dictionary<string, SurfaceState>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var rawLine in jsonLines.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (TryGetProperty(root, "surfaceUpdate", out var surfaceUpdate))
                {
                    var surfaceId = ReadString(surfaceUpdate, "surfaceId") ?? $"surface-{surfaces.Count + 1}";
                    var state = GetOrCreateSurface(surfaces, surfaceId);
                    if (TryGetProperty(surfaceUpdate, "components", out var components) &&
                        components.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            state.Components.Add(component.Clone());
                        }
                    }

                    continue;
                }

                if (TryGetProperty(root, "dataModelUpdate", out var dataModelUpdate))
                {
                    var surfaceId = ReadString(dataModelUpdate, "surfaceId") ?? $"surface-{surfaces.Count + 1}";
                    var state = GetOrCreateSurface(surfaces, surfaceId);
                    if (TryGetProperty(dataModelUpdate, "contents", out var contents))
                    {
                        state.DataModel = contents.Clone();
                    }

                    continue;
                }

                if (TryGetProperty(root, "beginRendering", out var beginRendering))
                {
                    var surfaceId = ReadString(beginRendering, "surfaceId") ?? $"surface-{surfaces.Count + 1}";
                    var state = GetOrCreateSurface(surfaces, surfaceId);
                    state.RootId = ReadString(beginRendering, "root");
                    continue;
                }

                messages.Add($"A2UI line {lineNumber} did not contain a supported envelope.");
            }
            catch (JsonException ex)
            {
                messages.Add($"A2UI line {lineNumber} is not valid JSON: {ex.Message}");
            }
        }

        var blocks = new List<AgentUiBlock>();
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var surface in surfaces.Values)
        {
            foreach (var component in OrderComponents(surface.Components, surface.RootId))
            {
                var block = MapComponentToBlock(component, surface.DataModel, messages, "A2UI");
                if (block is null)
                {
                    continue;
                }

                blocks.Add(block with { Id = EnsureUniqueId(block.Id, existingIds) });
            }
        }

        var document = BuildDocument(blocks, messages);
        diagnostics = messages;
        return document;
    }

    public static AgentUiDocument? FromOpenJsonUi(
        string json,
        out IReadOnlyList<string> diagnostics)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics = ["Open-JSON-UI payload is empty."];
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var components = ResolveOpenJsonUiComponents(root, messages);
            var blocks = new List<AgentUiBlock>();
            var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var component in components)
            {
                var block = MapComponentToBlock(component, null, messages, "Open-JSON-UI");
                if (block is null)
                {
                    continue;
                }

                blocks.Add(block with { Id = EnsureUniqueId(block.Id, existingIds) });
            }

            var document = BuildDocument(blocks, messages);
            diagnostics = messages;
            return document;
        }
        catch (JsonException ex)
        {
            diagnostics = [$"Open-JSON-UI payload is not valid JSON: {ex.Message}"];
            return null;
        }
    }

    private static SurfaceState GetOrCreateSurface(
        IDictionary<string, SurfaceState> surfaces,
        string surfaceId)
    {
        if (!surfaces.TryGetValue(surfaceId, out var state))
        {
            state = new SurfaceState();
            surfaces[surfaceId] = state;
        }

        return state;
    }

    private static IReadOnlyList<JsonElement> ResolveOpenJsonUiComponents(JsonElement root, List<string> messages)
    {
        if (TryGetProperty(root, "spec", out var spec) &&
            TryGetProperty(spec, "components", out var nestedComponents) &&
            nestedComponents.ValueKind == JsonValueKind.Array)
        {
            return [.. nestedComponents.EnumerateArray().Select(static component => component.Clone())];
        }

        if (TryGetProperty(root, "components", out var components) &&
            components.ValueKind == JsonValueKind.Array)
        {
            return [.. components.EnumerateArray().Select(static component => component.Clone())];
        }

        messages.Add("Open-JSON-UI payload did not contain a components array.");
        return [];
    }

    private static IReadOnlyList<JsonElement> OrderComponents(
        IReadOnlyList<JsonElement> components,
        string? rootId)
    {
        if (string.IsNullOrWhiteSpace(rootId))
        {
            return components;
        }

        var ordered = new List<JsonElement>(components.Count);
        foreach (var component in components)
        {
            var componentId = ReadString(component, "id") ??
                              ReadString(component, "componentId");
            if (string.Equals(componentId, rootId, StringComparison.OrdinalIgnoreCase))
            {
                ordered.Add(component);
            }
        }

        foreach (var component in components)
        {
            var componentId = ReadString(component, "id") ??
                              ReadString(component, "componentId");
            if (!string.Equals(componentId, rootId, StringComparison.OrdinalIgnoreCase))
            {
                ordered.Add(component);
            }
        }

        return ordered;
    }

    private static AgentUiDocument? BuildDocument(
        IReadOnlyList<AgentUiBlock> blocks,
        List<string> messages)
    {
        if (blocks.Count == 0)
        {
            if (messages.Count == 0)
            {
                messages.Add("No supported UI blocks were found in the payload.");
            }

            return null;
        }

        var document = new AgentUiDocument { Blocks = blocks };
        if (!document.TryValidate(out var validationError))
        {
            messages.Add(validationError ?? "Generated document failed validation.");
            return null;
        }

        return document;
    }

    private static AgentUiBlock? MapComponentToBlock(
        JsonElement component,
        JsonElement? dataModel,
        List<string> messages,
        string sourceName)
    {
        var type = ReadString(component, "type") ??
                   ReadString(component, "componentType") ??
                   ReadString(GetPropertiesNode(component), "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            messages.Add($"{sourceName} component is missing a type.");
            return null;
        }

        var normalizedType = NormalizeToken(type);
        var properties = GetPropertiesNode(component);
        var id = ReadString(component, "id") ??
                 ReadString(component, "componentId") ??
                 ReadString(properties, "id") ??
                 Slugify(ReadString(component, "title") ??
                         ReadString(properties, "title") ??
                         type);
        var title = ReadString(component, "title") ??
                    ReadString(properties, "title") ??
                    ReadString(component, "label") ??
                    ReadString(properties, "label");
        var description = ReadString(component, "description") ??
                          ReadString(properties, "description") ??
                          ReadString(component, "subtitle") ??
                          ReadString(properties, "subtitle") ??
                          ReadString(component, "content") ??
                          ReadString(properties, "content");

        if (LooksLikeForm(normalizedType, component, properties))
        {
            var fields = ParseFields(component, properties, dataModel);
            if (fields.Count == 0)
            {
                messages.Add($"{sourceName} form component '{id}' did not expose any fields.");
                return null;
            }

            return new AgentUiBlock
            {
                Id = id,
                Kind = AgentUiBlockKind.Form,
                Title = title,
                Description = description,
                Fields = fields,
                Actions = ParseActions(component, properties)
            };
        }

        if (LooksLikeTable(normalizedType, component, properties))
        {
            var columns = ParseColumns(component, properties);
            var rows = ParseRows(component, properties, dataModel);
            if (columns.Count == 0)
            {
                messages.Add($"{sourceName} table component '{id}' did not expose any columns.");
                return null;
            }

            return new AgentUiBlock
            {
                Id = id,
                Kind = AgentUiBlockKind.Table,
                Title = title,
                Description = description,
                Columns = columns,
                Rows = rows,
                Actions = ParseActions(component, properties)
            };
        }

        if (LooksLikeChart(normalizedType, component, properties))
        {
            var chartType = ParseChartType(
                ReadString(component, "chartType") ??
                ReadString(properties, "chartType") ??
                ReadString(component, "variant") ??
                ReadString(properties, "variant"));
            var labels = ReadStringArray(component, "labels");
            if (labels.Count == 0)
            {
                labels = ReadStringArray(properties, "labels");
            }

            var series = ParseChartSeries(component);
            if (series.Count == 0)
            {
                series = ParseChartSeries(properties);
            }

            var dataSource = ReadString(component, "dataSource") ??
                             ReadString(properties, "dataSource");
            var dataArguments = ReadObject(component, "dataArguments") ??
                                ReadObject(properties, "dataArguments") ??
                                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            return new AgentUiBlock
            {
                Id = id,
                Kind = AgentUiBlockKind.Chart,
                Title = title,
                Description = description,
                ChartType = chartType,
                ChartLabels = labels,
                ChartSeries = series,
                ChartDataSource = dataSource,
                ChartDataArguments = dataArguments,
                Actions = ParseActions(component, properties)
            };
        }

        if (LooksLikeCard(normalizedType, component, properties))
        {
            return new AgentUiBlock
            {
                Id = id,
                Kind = AgentUiBlockKind.Card,
                Title = title,
                Description = description,
                Actions = ParseActions(component, properties)
            };
        }

        messages.Add($"{sourceName} component type '{type}' is not supported yet.");
        return null;
    }

    private static JsonElement GetPropertiesNode(JsonElement element)
    {
        if (TryGetProperty(element, "properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            return properties;
        }

        if (TryGetProperty(element, "props", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            return props;
        }

        return element;
    }

    private static bool LooksLikeCard(string normalizedType, JsonElement component, JsonElement properties)
    {
        return normalizedType.Contains("card", StringComparison.Ordinal) ||
               normalizedType.Contains("summary", StringComparison.Ordinal) ||
               normalizedType.Contains("panel", StringComparison.Ordinal) ||
               (TryGetProperty(component, "actions", out _) && !LooksLikeForm(normalizedType, component, properties));
    }

    private static bool LooksLikeForm(string normalizedType, JsonElement component, JsonElement properties)
    {
        return normalizedType.Contains("form", StringComparison.Ordinal) ||
               normalizedType.Contains("input", StringComparison.Ordinal) ||
               TryGetProperty(component, "fields", out _) ||
               TryGetProperty(properties, "fields", out _) ||
               HasChildFieldComponents(component);
    }

    private static bool LooksLikeTable(string normalizedType, JsonElement component, JsonElement properties)
    {
        return normalizedType.Contains("table", StringComparison.Ordinal) ||
               normalizedType.Contains("grid", StringComparison.Ordinal) ||
               TryGetProperty(component, "columns", out _) ||
               TryGetProperty(properties, "columns", out _);
    }

    private static bool LooksLikeChart(string normalizedType, JsonElement component, JsonElement properties)
    {
        return normalizedType.Contains("chart", StringComparison.Ordinal) ||
               normalizedType.Contains("graph", StringComparison.Ordinal) ||
               TryGetProperty(component, "series", out _) ||
               TryGetProperty(properties, "series", out _) ||
               TryGetProperty(component, "dataSource", out _) ||
               TryGetProperty(properties, "dataSource", out _);
    }

    private static bool HasChildFieldComponents(JsonElement component)
    {
        if (!TryGetProperty(component, "children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in children.EnumerateArray())
        {
            var type = NormalizeToken(ReadString(child, "type") ?? string.Empty);
            if (type.Contains("field", StringComparison.Ordinal) ||
                type.Contains("input", StringComparison.Ordinal) ||
                type.Contains("select", StringComparison.Ordinal) ||
                type.Contains("checkbox", StringComparison.Ordinal) ||
                type.Contains("textarea", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<AgentUiField> ParseFields(
        JsonElement component,
        JsonElement properties,
        JsonElement? dataModel)
    {
        var overlay = ExtractDataModelValues(dataModel);
        if (TryGetProperty(component, "fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            return ParseFieldArray(fields, overlay);
        }

        if (TryGetProperty(properties, "fields", out fields) && fields.ValueKind == JsonValueKind.Array)
        {
            return ParseFieldArray(fields, overlay);
        }

        if (TryGetProperty(component, "children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            return ParseFieldChildren(children, overlay);
        }

        return [];
    }

    private static IReadOnlyList<AgentUiField> ParseFieldArray(
        JsonElement fields,
        IReadOnlyDictionary<string, string?> overlay)
    {
        var result = new List<AgentUiField>();
        foreach (var field in fields.EnumerateArray())
        {
            var name = ReadString(field, "name") ??
                       ReadString(field, "id") ??
                       Slugify(ReadString(field, "label") ?? "field");
            var label = ReadString(field, "label") ?? name;
            var value = ReadString(field, "value");
            if (value is null)
            {
                overlay.TryGetValue(name, out value);
                value ??= overlay.TryGetValue(label, out var byLabel) ? byLabel : null;
            }

            result.Add(new AgentUiField
            {
                Name = name,
                Label = label,
                Type = ReadString(field, "type") ?? "text",
                Placeholder = ReadString(field, "placeholder"),
                Value = value,
                Required = ReadBool(field, "required")
            });
        }

        return result;
    }

    private static IReadOnlyList<AgentUiField> ParseFieldChildren(
        JsonElement children,
        IReadOnlyDictionary<string, string?> overlay)
    {
        var result = new List<AgentUiField>();
        foreach (var child in children.EnumerateArray())
        {
            var type = NormalizeToken(ReadString(child, "type") ?? string.Empty);
            if (!(type.Contains("field", StringComparison.Ordinal) ||
                  type.Contains("input", StringComparison.Ordinal) ||
                  type.Contains("select", StringComparison.Ordinal) ||
                  type.Contains("checkbox", StringComparison.Ordinal) ||
                  type.Contains("textarea", StringComparison.Ordinal)))
            {
                continue;
            }

            var props = GetPropertiesNode(child);
            var name = ReadString(child, "name") ??
                       ReadString(child, "id") ??
                       ReadString(props, "name") ??
                       Slugify(ReadString(child, "label") ??
                               ReadString(props, "label") ??
                               "field");
            var label = ReadString(child, "label") ??
                        ReadString(props, "label") ??
                        name;
            overlay.TryGetValue(name, out var value);
            value ??= overlay.TryGetValue(label, out var byLabel) ? byLabel : null;

            result.Add(new AgentUiField
            {
                Name = name,
                Label = label,
                Type = ReadString(child, "type") ??
                       ReadString(props, "type") ??
                       "text",
                Placeholder = ReadString(child, "placeholder") ??
                              ReadString(props, "placeholder"),
                Value = value,
                Required = ReadBool(child, "required") || ReadBool(props, "required")
            });
        }

        return result;
    }

    private static IReadOnlyList<AgentUiAction> ParseActions(JsonElement component, JsonElement properties)
    {
        var result = new List<AgentUiAction>();
        if (TryGetProperty(component, "actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(ParseActionArray(actions));
        }

        if (TryGetProperty(properties, "actions", out actions) && actions.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(ParseActionArray(actions));
        }

        if (TryGetProperty(component, "primaryAction", out var primaryAction) &&
            primaryAction.ValueKind == JsonValueKind.Object)
        {
            result.Add(ParseAction(primaryAction));
        }

        return result
            .Where(static action => !string.IsNullOrWhiteSpace(action.Id))
            .GroupBy(static action => action.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static IEnumerable<AgentUiAction> ParseActionArray(JsonElement actions)
    {
        foreach (var action in actions.EnumerateArray())
        {
            if (action.ValueKind == JsonValueKind.Object)
            {
                yield return ParseAction(action);
            }
        }
    }

    private static AgentUiAction ParseAction(JsonElement action)
    {
        var id = ReadString(action, "id") ??
                 ReadString(action, "actionId") ??
                 Slugify(ReadString(action, "label") ?? "action");
        var args = ReadObject(action, "arguments") ??
                   ReadObject(action, "args") ??
                   new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new AgentUiAction
        {
            Id = id,
            Label = ReadString(action, "label") ?? id,
            Prompt = ReadString(action, "prompt"),
            Arguments = args
        };
    }

    private static IReadOnlyList<AgentUiTableColumn> ParseColumns(JsonElement component, JsonElement properties)
    {
        if (TryGetProperty(component, "columns", out var columns) && columns.ValueKind == JsonValueKind.Array)
        {
            return ParseColumnArray(columns);
        }

        if (TryGetProperty(properties, "columns", out columns) && columns.ValueKind == JsonValueKind.Array)
        {
            return ParseColumnArray(columns);
        }

        return [];
    }

    private static IReadOnlyList<AgentUiTableColumn> ParseColumnArray(JsonElement columns)
    {
        var result = new List<AgentUiTableColumn>();
        foreach (var column in columns.EnumerateArray())
        {
            var key = ReadString(column, "key") ??
                      ReadString(column, "name") ??
                      ReadString(column, "id");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result.Add(new AgentUiTableColumn
            {
                Key = key,
                Header = ReadString(column, "header") ??
                         ReadString(column, "label") ??
                         key
            });
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParseRows(
        JsonElement component,
        JsonElement properties,
        JsonElement? dataModel)
    {
        if (TryGetProperty(component, "rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            return ParseRowArray(rows);
        }

        if (TryGetProperty(properties, "rows", out rows) && rows.ValueKind == JsonValueKind.Array)
        {
            return ParseRowArray(rows);
        }

        if (dataModel is { ValueKind: JsonValueKind.Array } arrayData)
        {
            return ParseRowArray(arrayData);
        }

        return [];
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParseRowArray(JsonElement rows)
    {
        var result = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(ToObject(row));
        }

        return result;
    }

    private static IReadOnlyList<AgentUiChartSeries> ParseChartSeries(JsonElement element)
    {
        if (!TryGetProperty(element, "series", out var series) ||
            series.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<AgentUiChartSeries>();
        foreach (var item in series.EnumerateArray())
        {
            var name = ReadString(item, "name") ?? "Series";
            var data = new List<double>();
            if (TryGetProperty(item, "data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var point in dataElement.EnumerateArray())
                {
                    if (TryReadDouble(point, out var value))
                    {
                        data.Add(value);
                    }
                }
            }

            if (data.Count > 0)
            {
                result.Add(new AgentUiChartSeries { Name = name, Data = data });
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string?> ExtractDataModelValues(JsonElement? dataModel)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (dataModel is null)
        {
            return result;
        }

        if (dataModel.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in dataModel.Value.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.GetRawText();
            }

            return result;
        }

        if (dataModel.Value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in dataModel.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = ReadString(item, "name") ??
                      ReadString(item, "id") ??
                      ReadString(item, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                var path = ReadString(item, "path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    key = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (TryGetProperty(item, "value", out var valueElement))
            {
                result[key] = valueElement.ValueKind == JsonValueKind.String
                    ? valueElement.GetString()
                    : valueElement.GetRawText();
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. values.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()!)
            .Where(static value => !string.IsNullOrWhiteSpace(value))];
    }

    private static IReadOnlyDictionary<string, object?>? ReadObject(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var objectElement) ||
            objectElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ToObject(objectElement);
    }

    private static Dictionary<string, object?> ToObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ToClrValue(property.Value);
        }

        return result;
    }

    private static object? ToClrValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var whole) => whole,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ToObject(value),
            JsonValueKind.Array => value.EnumerateArray().Select(ToClrValue).ToArray(),
            _ => value.GetRawText()
        };
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static bool TryReadDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static AgentUiChartType? ParseChartType(string? value)
    {
        return NormalizeToken(value) switch
        {
            "line" => AgentUiChartType.Line,
            "bar" => AgentUiChartType.Bar,
            "pie" => AgentUiChartType.Pie,
            _ => null
        };
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string EnsureUniqueId(string id, ISet<string> existingIds)
    {
        var candidate = string.IsNullOrWhiteSpace(id) ? "block" : id.Trim();
        if (existingIds.Add(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (!existingIds.Add($"{candidate}-{suffix}"))
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }

    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "block";
        }

        var chars = value.Trim()
            .Select(static c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray();
        var slug = string.Join(string.Empty, chars)
            .Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "block" : slug;
    }

    private sealed class SurfaceState
    {
        public List<JsonElement> Components { get; } = [];

        public JsonElement? DataModel { get; set; }

        public string? RootId { get; set; }
    }
}
