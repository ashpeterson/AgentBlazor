using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentBlazor.Core.Components;

public static class AgentGenerativeUiSpec
{
    public const string V0 = "agentblazor.ui.v0";
    public const string GenerateUiContextKey = "agentblazor.ui.generate";
    public const string BlockIdContextKey = "agentblazor.ui.block_id";
    public const string ActionIdContextKey = "agentblazor.ui.action_id";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentUiBlockKind
{
    Card,
    Form,
    Table,
    Chart
}

public sealed record AgentUiDocument
{
    public string SpecVersion { get; init; } = AgentGenerativeUiSpec.V0;

    public IReadOnlyList<AgentUiBlock> Blocks { get; init; } = [];

    public bool TryValidate(out string? error)
    {
        if (!string.Equals(SpecVersion, AgentGenerativeUiSpec.V0, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported spec version '{SpecVersion}'. Expected '{AgentGenerativeUiSpec.V0}'.";
            return false;
        }

        if (Blocks.Count == 0)
        {
            error = "Document must contain at least one UI block.";
            return false;
        }

        var seenBlockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Id))
            {
                error = "Each block must include a non-empty id.";
                return false;
            }

            if (!seenBlockIds.Add(block.Id))
            {
                error = $"Duplicate block id '{block.Id}'.";
                return false;
            }

            if (!block.TryValidate(out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }
}

public sealed record AgentUiBlock
{
    public string Id { get; init; } = string.Empty;

    public AgentUiBlockKind Kind { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<AgentUiAction> Actions { get; init; } = [];

    public IReadOnlyList<AgentUiField> Fields { get; init; } = [];

    public IReadOnlyList<AgentUiTableColumn> Columns { get; init; } = [];

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; } = [];

    public AgentUiChartType? ChartType { get; init; }

    public IReadOnlyList<string> ChartLabels { get; init; } = [];

    public IReadOnlyList<AgentUiChartSeries> ChartSeries { get; init; } = [];

    public string? ChartDataSource { get; init; }

    public IReadOnlyDictionary<string, object?> ChartDataArguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public bool TryValidate(out string? error)
    {
        var seenActionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id))
            {
                error = $"Block '{Id}' has an action with an empty id.";
                return false;
            }

            if (!seenActionIds.Add(action.Id))
            {
                error = $"Block '{Id}' has duplicate action id '{action.Id}'.";
                return false;
            }
        }

        if (Kind == AgentUiBlockKind.Form && Fields.Count == 0)
        {
            error = $"Form block '{Id}' must define at least one field.";
            return false;
        }

        if (Kind == AgentUiBlockKind.Table)
        {
            if (Columns.Count == 0)
            {
                error = $"Table block '{Id}' must define at least one column.";
                return false;
            }

            foreach (var column in Columns)
            {
                if (string.IsNullOrWhiteSpace(column.Key))
                {
                    error = $"Table block '{Id}' has a column with an empty key.";
                    return false;
                }
            }
        }

        if (Kind == AgentUiBlockKind.Chart)
        {
            var hasInlineData = ChartLabels.Count > 0 || ChartSeries.Count > 0;
            var hasDataSource = !string.IsNullOrWhiteSpace(ChartDataSource);

            if (!hasInlineData && !hasDataSource)
            {
                error = $"Chart block '{Id}' must define inline data or a chart data source.";
                return false;
            }

            if (!hasInlineData)
            {
                error = null;
                return true;
            }

            if (ChartType is null)
            {
                error = $"Chart block '{Id}' must include a chart type.";
                return false;
            }

            if (ChartLabels.Count == 0)
            {
                error = $"Chart block '{Id}' must define at least one label.";
                return false;
            }

            if (ChartSeries.Count == 0)
            {
                error = $"Chart block '{Id}' must define at least one series.";
                return false;
            }

            foreach (var series in ChartSeries)
            {
                if (string.IsNullOrWhiteSpace(series.Name))
                {
                    error = $"Chart block '{Id}' has a series with an empty name.";
                    return false;
                }

                if (series.Data.Count != ChartLabels.Count)
                {
                    error = $"Chart block '{Id}' series '{series.Name}' must have {ChartLabels.Count} data points.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }
}

public sealed record AgentUiAction
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Optional prompt sent to <see cref="IAgentRuntime"/> when action forwarding is enabled.
    /// </summary>
    public string? Prompt { get; init; }

    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AgentUiField
{
    public string Name { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Type { get; init; } = "text";

    public string? Placeholder { get; init; }

    public string? Value { get; init; }

    public bool Required { get; init; }
}

public sealed record AgentUiTableColumn
{
    public string Key { get; init; } = string.Empty;

    public string Header { get; init; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentUiChartType
{
    Line,
    Bar,
    Pie
}

public sealed record AgentUiChartSeries
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<double> Data { get; init; } = [];
}

public sealed record AgentUiActionInvocation(
    string BlockId,
    AgentUiAction Action,
    IReadOnlyDictionary<string, object?> Payload);
