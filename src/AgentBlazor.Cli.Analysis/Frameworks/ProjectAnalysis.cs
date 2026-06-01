using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Frameworks;

public sealed record ProjectAnalysis<TFrameworkContext>
{
    public string Framework { get; init; } = string.Empty;

    public ProjectModel Model { get; init; } = new();

    public TFrameworkContext Context { get; init; } = default!;

    public InstallReadinessReport? Readiness { get; init; }
}

public interface IProjectAnalyzer<TFrameworkContext>
{
    string Framework { get; }

    bool CanAnalyze(string solutionOrProjectPath);

    Task<ProjectAnalysis<TFrameworkContext>> AnalyzeAsync(
        string solutionOrProjectPath,
        string? hostProjectName,
        string description,
        AgentBlazorConfig? config = null,
        bool includeReadiness = true,
        AnalysisScanScope scanScope = AnalysisScanScope.References,
        CancellationToken ct = default);
}
