namespace AgentBlazor.Cli.Analysis.Models;

public sealed record ScaffoldPreviewResult
{
    public IReadOnlyList<ScaffoldPreviewFile> Changes { get; init; } = [];

    public int ChangedFileCount => Changes.Count;

    public bool HasChanges => Changes.Count > 0;
}

public sealed record ScaffoldPreviewFile
{
    public string Path { get; init; } = string.Empty;

    public ScaffoldPreviewChangeKind ChangeKind { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string OriginalContent { get; init; } = string.Empty;

    public string UpdatedContent { get; init; } = string.Empty;
}

public enum ScaffoldPreviewChangeKind
{
    Create,
    Update
}
