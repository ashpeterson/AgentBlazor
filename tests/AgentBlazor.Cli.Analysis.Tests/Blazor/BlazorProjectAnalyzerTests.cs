using AgentBlazor.Cli.Analysis.Blazor;
using AgentBlazor.Cli.Analysis.Frameworks;

namespace AgentBlazor.Cli.Analysis.Tests.Blazor;

public sealed class BlazorProjectAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public BlazorProjectAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-blazor-analyzer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Registry_ResolvesBlazorAnalyzer_ForBlazorProjectDirectory()
    {
        var projectPath = CreateMinimalBlazorProject("SimpleBlazor");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var registry = new ProjectAnalyzerRegistry();

        var analyzer = registry.ResolveBlazor("auto", projectDirectory);

        Assert.Equal("blazor", analyzer.Framework);
    }

    [Fact]
    public async Task AnalyzeAsync_ProducesBlazorContext_WithoutReadiness()
    {
        var projectPath = CreateMinimalBlazorProject("SimpleBlazor");
        var analyzer = new BlazorProjectAnalyzer();

        var analysis = await analyzer.AnalyzeAsync(
            projectPath,
            hostProjectName: null,
            description: "Simple app",
            includeReadiness: false);

        Assert.Equal("blazor", analysis.Framework);
        Assert.Equal("SimpleBlazor", analysis.Context.HostProjectName);
        Assert.Equal("SimpleBlazor", analysis.Model.BlazorHostProject);
        Assert.Contains(analysis.Model.Routes, route => route.Template == "/");
        Assert.Null(analysis.Readiness);
    }

    [Fact]
    public async Task AnalyzeAsync_StaticWorkspaceFallback_LoadsSolutionProjectsAndRoutes()
    {
        var projectPath = CreateMinimalBlazorProject("FallbackBlazor");
        var solutionPath = Path.Combine(_tempDir, "FallbackBlazor.sln");
        File.WriteAllText(
            solutionPath,
            $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "FallbackBlazor", "FallbackBlazor\FallbackBlazor.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Global
            EndGlobal
            """);

        var previous = Environment.GetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE");
        Environment.SetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE", "1");
        try
        {
            var analyzer = new BlazorProjectAnalyzer();

            var analysis = await analyzer.AnalyzeAsync(
                solutionPath,
                hostProjectName: "FallbackBlazor",
                description: "Fallback app",
                includeReadiness: true);

            Assert.Equal("FallbackBlazor", analysis.Model.BlazorHostProject);
            Assert.Contains(analysis.Model.Routes, route => route.Template == "/");
            Assert.Contains(analysis.Model.Projects, project => project.Name == "FallbackBlazor");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE", previous);
        }
    }

    private string CreateMinimalBlazorProject(string projectName)
    {
        var projectDirectory = Path.Combine(_tempDir, projectName);
        var pagesDirectory = Path.Combine(projectDirectory, "Components", "Pages");
        Directory.CreateDirectory(pagesDirectory);

        var projectPath = Path.Combine(projectDirectory, $"{projectName}.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(projectDirectory, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddRazorComponents();
            var app = builder.Build();
            app.MapRazorComponents<App>();
            app.Run();
            """);

        File.WriteAllText(
            Path.Combine(projectDirectory, "Components", "App.razor"),
            """
            <Routes />
            """);

        File.WriteAllText(
            Path.Combine(pagesDirectory, "Home.razor"),
            """
            @page "/"
            <h1>Hello</h1>
            """);

        return projectPath;
    }
}
