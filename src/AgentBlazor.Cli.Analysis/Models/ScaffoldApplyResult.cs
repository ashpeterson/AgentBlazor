namespace AgentBlazor.Cli.Analysis.Models;

public sealed record ScaffoldApplyResult
{
    public IReadOnlyList<ScaffoldAppliedChange> Changes { get; init; } = [];

    public string? ManifestPath { get; init; }

    public int ChangedFileCount => Changes.Select(change => change.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}

public sealed record ScaffoldAppliedChange
{
    public string Path { get; init; } = string.Empty;

    public ScaffoldPreviewChangeKind ChangeKind { get; init; }

    public string Summary { get; init; } = string.Empty;
}
