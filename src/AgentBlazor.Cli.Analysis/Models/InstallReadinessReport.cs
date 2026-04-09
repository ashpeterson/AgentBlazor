namespace AgentBlazor.Cli.Analysis.Models;

public sealed record InstallReadinessReport
{
    public string InputPath { get; init; } = string.Empty;

    public string HostProjectName { get; init; } = string.Empty;

    public string HostProjectPath { get; init; } = string.Empty;

    public string? UiProjectName { get; init; }

    public string? UiProjectPath { get; init; }

    public HostShapeAssessment HostShape { get; init; } = new();

    public IReadOnlyList<InstallReadinessCheck> Checks { get; init; } = [];

    public int PassCount => Checks.Count(check => check.Status == InstallReadinessStatus.Pass);

    public int WarningCount => Checks.Count(check => check.Status == InstallReadinessStatus.Warning);

    public int MissingCount => Checks.Count(check => check.Status == InstallReadinessStatus.Missing);

    public bool IsReady => MissingCount == 0;

    public InstallReadinessCheck? HostShapeCheck => Checks.FirstOrDefault(check => check.Id == "host-shape");
}

public sealed record InstallReadinessCheck
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public InstallReadinessStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public string? SuggestedFix { get; init; }
}

public enum InstallReadinessStatus
{
    Pass,
    Warning,
    Missing
}
