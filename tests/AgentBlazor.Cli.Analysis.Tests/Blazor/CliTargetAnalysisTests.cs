using AgentBlazor.Cli.Analysis.Blazor;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests.Blazor;

public sealed class CliTargetAnalysisTests
{
    [Theory]
    [InlineData("simple-blazor-app/SimpleBlazorApp.csproj", 2, "CustomerService")]
    [InlineData("realistic-blazor-app/RealisticBlazorApp.csproj", 3, "OrderService")]
    [InlineData("hosted-wasm-app/HostedWasmApp.Server/HostedWasmApp.Server.csproj", 1, "AuditBundleService")]
    public async Task BlazorAnalyzer_AnalyzesSyntheticTargetApps(
        string relativeProjectPath,
        int minimumRouteCount,
        string expectedService)
    {
        var projectPath = Path.Combine(FindRepoRoot(), "tests", "cli-targets", relativeProjectPath);
        var analyzer = new BlazorProjectAnalyzer();

        var analysis = await analyzer.AnalyzeAsync(
            projectPath,
            hostProjectName: null,
            description: "Synthetic CLI target",
            includeReadiness: true);

        Assert.Equal("blazor", analysis.Framework);
        Assert.True(analysis.Model.Routes.Count >= minimumRouteCount);
        Assert.Contains(analysis.Model.Services, service => service.TypeName == expectedService);
        Assert.NotNull(analysis.Readiness);
        Assert.Equal(projectPath, analysis.Context.HostProjectPath);
    }

    [Fact]
    public async Task BlazorAnalyzer_RealisticTarget_DiscoversConfirmedCapabilitiesAndReadiness()
    {
        var projectPath = Path.Combine(FindRepoRoot(), "tests", "cli-targets", "realistic-blazor-app", "RealisticBlazorApp.csproj");
        var analyzer = new BlazorProjectAnalyzer();

        var analysis = await analyzer.AnalyzeAsync(
            projectPath,
            hostProjectName: null,
            description: "Synthetic realistic app",
            includeReadiness: true);

        Assert.Contains(analysis.Model.Actions, action =>
            action.SourceService == "OrderOperationsCapabilities" &&
            action.MethodName == "ReleaseOrderHoldAsync" &&
            action.ExposureMode == ActionExposureMode.Confirmed &&
            action.RequiresApproval);
        Assert.True(analysis.Context.HasAgentBlazorServices);
        Assert.True(analysis.Context.HasWorkflowRegistration);
        Assert.True(analysis.Context.HasEndpointMapping);
        Assert.True(analysis.Context.HasChatSurface);
    }

    [Fact]
    public async Task BlazorAnalyzer_SolutionScanScope_IncludesSiblingTenantProjects()
    {
        var previousStaticWorkspace = Environment.GetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE");
        Environment.SetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE", "1");

        try
        {
            var solutionPath = Path.Combine(FindRepoRoot(), "tests", "cli-targets", "multitenant-solution", "MultiTenantApp.slnx");
            var analyzer = new BlazorProjectAnalyzer();

            var referencesOnly = await analyzer.AnalyzeAsync(
                solutionPath,
                hostProjectName: "Host",
                description: "Multi-tenant solution",
                includeReadiness: false);

            var fullSolution = await analyzer.AnalyzeAsync(
                solutionPath,
                hostProjectName: "Host",
                description: "Multi-tenant solution",
                includeReadiness: false,
                scanScope: AnalysisScanScope.Solution);

            Assert.DoesNotContain(referencesOnly.Model.Routes, route => route.Template == "/tenant-a/dashboard");
            Assert.DoesNotContain(referencesOnly.Model.Services, service => service.TypeName == "TenantBillingService");
            Assert.Contains(fullSolution.Model.Routes, route => route.Template == "/tenant-a/dashboard");
            Assert.Contains(fullSolution.Model.Services, service => service.TypeName == "TenantBillingService");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE", previousStaticWorkspace);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "AgentBlazor.slnx")) &&
                Directory.Exists(Path.Combine(directory, "tests", "cli-targets")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate AgentBlazor repository root.");
    }
}
