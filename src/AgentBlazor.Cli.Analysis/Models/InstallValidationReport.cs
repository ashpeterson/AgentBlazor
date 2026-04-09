namespace AgentBlazor.Cli.Analysis.Models;

public sealed record InstallValidationReport
{
    public InstallReadinessReport Readiness { get; init; } = new();

    public IReadOnlyList<InstallReadinessCheck> Checks { get; init; } = [];

    public int PassCount => Checks.Count(check => check.Status == InstallReadinessStatus.Pass);

    public int WarningCount => Checks.Count(check => check.Status == InstallReadinessStatus.Warning);

    public int MissingCount => Checks.Count(check => check.Status == InstallReadinessStatus.Missing);

    public bool HasBlockingIssues => Readiness.MissingCount > 0 || MissingCount > 0;
}
