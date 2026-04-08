using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class InstallReadinessAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public InstallReadinessAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-doctor-{Guid.NewGuid():N}");
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
    public async Task AnalyzeAsync_WhenAgentBlazorIsNotInstalled_ReportsMissingBaselineChecks()
    {
        var projectPath = CreateProject(
            projectName: "PlainApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="MudBlazor" Version="8.15.0" />
                  </ItemGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorComponents();
                builder.Services.AddMudServices();

                var app = builder.Build();
                app.MapRazorComponents<App>();
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                <MudThemeProvider />
                <MudPopoverProvider />
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Equal("PlainApp", report.HostProjectName);
        Assert.True(report.MissingCount >= 4);
        Assert.Contains(report.Checks, check => check.Id == "package-references" && check.Status == InstallReadinessStatus.Missing);
        Assert.Contains(report.Checks, check => check.Id == "agentblazor-services" && check.Status == InstallReadinessStatus.Missing);
        Assert.Contains(report.Checks, check => check.Id == "workflow-registration" && check.Status == InstallReadinessStatus.Missing);
        Assert.Contains(report.Checks, check => check.Id == "endpoint-mapping" && check.Status == InstallReadinessStatus.Missing);
        Assert.Contains(report.Checks, check => check.Id == "chat-surface" && check.Status == InstallReadinessStatus.Warning);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenBaselineWiringExists_ReportsProjectAsReady()
    {
        var projectPath = CreateProject(
            projectName: "ReadyApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="AgentBlazor" Version="0.0.0-local" />
                    <PackageReference Include="MudBlazor" Version="8.15.0" />
                  </ItemGroup>
                </Project>
                """,
            programBody: """
                using AgentBlazor;
                using MudBlazor.Services;

                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorComponents();
                builder.Services.AddMudServices();
                builder.Services.AddAgentBlazor(options =>
                {
                    options.ConfigureBuilder(agentBuilder =>
                    {
                        agentBuilder.AddWorkflow<MyCapabilities>("assistant");
                    });
                });

                var app = builder.Build();
                app.MapRazorComponents<App>();
                app.MapAgentBlazorEndpoints();
                app.Run();

                public sealed class MyCapabilities
                {
                }
                """,
            appRazorBody: """
                <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
                <Routes />
                <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
                """,
            mainLayoutBody: """
                <MudThemeProvider />
                <MudPopoverProvider />
                <MudDialogProvider />
                <MudSnackbarProvider />
                @Body
                """,
            homeBody: """
                @page "/"
                <AgentChatWidget Title="Assistant" />
                """);

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.True(report.IsReady);
        Assert.Equal(0, report.MissingCount);
        Assert.All(report.Checks.Where(check => check.Id is not "chat-surface" and not "shell-assets" and not "mud-providers"),
            check => Assert.Equal(InstallReadinessStatus.Pass, check.Status));
        Assert.Contains(report.Checks, check => check.Id == "chat-surface" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "shell-assets" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "mud-providers" && check.Status == InstallReadinessStatus.Pass);
    }

    private string CreateProject(
        string projectName,
        string csprojBody,
        string programBody,
        string appRazorBody,
        string mainLayoutBody,
        string homeBody)
    {
        var projectDirectory = Path.Combine(_tempDir, projectName);
        var componentsDirectory = Path.Combine(projectDirectory, "Components");
        var layoutDirectory = Path.Combine(componentsDirectory, "Layout");
        var pagesDirectory = Path.Combine(componentsDirectory, "Pages");

        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(componentsDirectory);
        Directory.CreateDirectory(layoutDirectory);
        Directory.CreateDirectory(pagesDirectory);

        File.WriteAllText(Path.Combine(projectDirectory, $"{projectName}.csproj"), csprojBody);
        File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), programBody);
        File.WriteAllText(Path.Combine(componentsDirectory, "App.razor"), appRazorBody);
        File.WriteAllText(Path.Combine(layoutDirectory, "MainLayout.razor"), mainLayoutBody);
        File.WriteAllText(Path.Combine(pagesDirectory, "Home.razor"), homeBody);

        return Path.Combine(projectDirectory, $"{projectName}.csproj");
    }
}
