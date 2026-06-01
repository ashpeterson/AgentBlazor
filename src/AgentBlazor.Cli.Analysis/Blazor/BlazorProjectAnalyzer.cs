using AgentBlazor.Cli.Analysis.Frameworks;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Blazor;

public sealed class BlazorProjectAnalyzer : IProjectAnalyzer<BlazorFrameworkContext>
{
    public string Framework => "blazor";

    public bool CanAnalyze(string solutionOrProjectPath)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            return false;
        }

        if (File.Exists(solutionOrProjectPath))
        {
            var extension = Path.GetExtension(solutionOrProjectPath);
            return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase);
        }

        if (!Directory.Exists(solutionOrProjectPath))
        {
            return false;
        }

        return Directory.EnumerateFiles(solutionOrProjectPath, "*.sln").Any() ||
               Directory.EnumerateFiles(solutionOrProjectPath, "*.slnx").Any() ||
               Directory.EnumerateFiles(solutionOrProjectPath, "*.csproj").Any() ||
               Directory.EnumerateFiles(solutionOrProjectPath, "*.razor", SearchOption.AllDirectories).Any();
    }

    public async Task<ProjectAnalysis<BlazorFrameworkContext>> AnalyzeAsync(
        string solutionOrProjectPath,
        string? hostProjectName,
        string description,
        AgentBlazorConfig? config = null,
        bool includeReadiness = true,
        AnalysisScanScope scanScope = AnalysisScanScope.References,
        CancellationToken ct = default)
    {
        var modelBuilder = new ModelBuilder();
        var model = await modelBuilder.BuildModelAsync(solutionOrProjectPath, hostProjectName, description, config, scanScope, ct)
            .ConfigureAwait(false);

        InstallReadinessReport? readiness = null;
        if (includeReadiness)
        {
            var readinessAnalyzer = new InstallReadinessAnalyzer();
            readiness = await readinessAnalyzer.AnalyzeAsync(solutionOrProjectPath, model.BlazorHostProject, ct)
                .ConfigureAwait(false);
        }

        return new ProjectAnalysis<BlazorFrameworkContext>
        {
            Framework = Framework,
            Model = model,
            Readiness = readiness,
            Context = BuildContext(model, readiness)
        };
    }

    private static BlazorFrameworkContext BuildContext(ProjectModel model, InstallReadinessReport? readiness)
    {
        if (readiness is null)
        {
            return new BlazorFrameworkContext
            {
                HostProjectName = model.BlazorHostProject
            };
        }

        return new BlazorFrameworkContext
        {
            HostProjectName = readiness.HostProjectName,
            HostProjectPath = readiness.HostProjectPath,
            UiProjectName = readiness.UiProjectName,
            UiProjectPath = readiness.UiProjectPath,
            HostShape = readiness.HostShape,
            HasAgentBlazorServices = HasPassed(readiness, "agentblazor-services"),
            HasWorkflowRegistration = HasPassed(readiness, "workflow-registration"),
            HasEndpointMapping = HasPassed(readiness, "endpoint-mapping"),
            HasChatSurface = HasPassed(readiness, "chat-surface")
        };
    }

    private static bool HasPassed(InstallReadinessReport readiness, string checkId)
        => readiness.Checks.Any(check =>
            check.Id.Equals(checkId, StringComparison.OrdinalIgnoreCase) &&
            check.Status == InstallReadinessStatus.Pass);
}
