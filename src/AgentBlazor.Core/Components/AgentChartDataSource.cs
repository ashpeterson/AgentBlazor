namespace AgentBlazor.Core.Components;

public sealed record AgentChartDataRequest(
    string DataSource,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed record AgentChartDataResult
{
    public AgentUiChartType ChartType { get; init; } = AgentUiChartType.Line;

    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<AgentUiChartSeries> Series { get; init; } = [];

    public string? Title { get; init; }

    public string? Description { get; init; }
}

public delegate ValueTask<AgentChartDataResult?> AgentChartDataResolver(
    AgentChartDataRequest request,
    CancellationToken cancellationToken);
