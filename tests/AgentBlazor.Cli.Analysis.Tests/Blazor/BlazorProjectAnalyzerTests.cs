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
