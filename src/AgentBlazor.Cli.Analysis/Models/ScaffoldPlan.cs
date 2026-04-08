namespace AgentBlazor.Cli.Analysis.Models;

public sealed record ScaffoldPlan
{
    public string InputPath { get; init; } = string.Empty;

    public string HostProjectName { get; init; } = string.Empty;

    public string HostProjectPath { get; init; } = string.Empty;

    public InstallReadinessReport Readiness { get; init; } = new();

    public IReadOnlyList<ScaffoldPlanItem> Items { get; init; } = [];

    public bool HasChanges => Items.Count > 0;
}

public sealed record ScaffoldPlanItem
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public ScaffoldPlanAction Action { get; init; }

    public string TargetPath { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}

public enum ScaffoldPlanAction
{
    Create,
    Update,
    ManualReview
}
