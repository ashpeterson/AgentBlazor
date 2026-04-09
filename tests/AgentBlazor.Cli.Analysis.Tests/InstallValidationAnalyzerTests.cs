using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class InstallValidationAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public InstallValidationAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-validate-{Guid.NewGuid():N}");
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
    public async Task AnalyzeAsync_WhenScaffoldWasApplied_ReportsManifestAndTrackedFilesAsValid()
    {
        var projectPath = CreateProject(
            projectName: "ValidateApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorComponents();

                var app = builder.Build();
                app.MapRazorComponents<App>();
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();
        await applier.ApplyAsync(plan);

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.False(report.HasBlockingIssues);
        Assert.Contains(report.Checks, check => check.Id == "scaffold-manifest" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "manifest-host-match" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "manifest-files" && check.Status == InstallReadinessStatus.Pass);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenInstallWasManual_WarnsThatManifestIsMissing()
    {
        var projectPath = CreateProject(
            projectName: "ManualInstallApp",
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
                <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
                <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
                <Routes />
                <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
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

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.False(report.HasBlockingIssues);
        Assert.Contains(report.Checks, check => check.Id == "scaffold-manifest" && check.Status == InstallReadinessStatus.Warning);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenManifestReferencesMissingFiles_ReportsBlockingIssue()
    {
        var projectPath = CreateProject(
            projectName: "BrokenManifestApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorComponents();

                var app = builder.Build();
                app.MapRazorComponents<App>();
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();
        await applier.ApplyAsync(plan);
        File.Delete(Path.Combine(Path.GetDirectoryName(projectPath)!, "Workflows", "AppCapabilities.cs"));

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.True(report.HasBlockingIssues);
        Assert.Contains(report.Checks, check => check.Id == "manifest-files" && check.Status == InstallReadinessStatus.Missing);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenLegacyReviewItemsRemainIncomplete_ReportsManualReviewChecks()
    {
        var projectPath = CreateProject(
            projectName: "LegacyReviewApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddServerSideBlazor();

                var app = builder.Build();
                app.MapBlazorHub();
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(projectPath)!, "Startup.cs"),
            "public sealed class Startup { }");
        var pagesDirectory = Path.Combine(Path.GetDirectoryName(projectPath)!, "Pages");
        Directory.CreateDirectory(pagesDirectory);
        File.WriteAllText(Path.Combine(pagesDirectory, "_Host.cshtml"), "<html></html>");

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.True(report.HasBlockingIssues);
        Assert.Contains(report.Checks, check =>
            check.Id == "manual-review:mud-services" &&
            check.Status == InstallReadinessStatus.Missing &&
            check.FilePath is not null &&
            check.FilePath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(report.Checks, check =>
            check.Id == "manual-review:shell-assets" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("Pages", "_Host.cshtml"), StringComparison.Ordinal) &&
            check.SuggestedFix is not null &&
            check.SuggestedFix.Contains("_Host.cshtml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenLegacyHostShellAssetsAreCompleted_PassesThatManualReviewCheck()
    {
        var projectPath = CreateProject(
            projectName: "LegacyPartialApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddServerSideBlazor();

                var app = builder.Build();
                app.MapBlazorHub();
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(projectPath)!, "Startup.cs"),
            "public sealed class Startup { }");
        var pagesDirectory = Path.Combine(Path.GetDirectoryName(projectPath)!, "Pages");
        Directory.CreateDirectory(pagesDirectory);
        File.WriteAllText(
            Path.Combine(pagesDirectory, "_Host.cshtml"),
            """
            <html>
            <head>
                <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
                <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
            </head>
            <body>
                <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
                <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
            </body>
            </html>
            """);

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Contains(report.Readiness.Checks, check => check.Id == "shell-assets" && check.Status == InstallReadinessStatus.Pass);
        Assert.DoesNotContain(report.Checks, check => check.Id == "manual-review:shell-assets");
    }

    [Fact]
    public async Task AnalyzeAsync_WhenHostedWebAssemblyReviewItemsRemainIncomplete_UsesHostedClientGuidance()
    {
        var projectPath = CreateProject(
            projectName: "HostedWasmValidateApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\HostedWasmValidateClient\HostedWasmValidateClient.csproj" />
                  </ItemGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorPages();

                var app = builder.Build();
                app.UseBlazorFrameworkFiles();
                app.UseStaticFiles();
                app.MapFallbackToFile("index.html");
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        CreateHostedWasmClientProject("HostedWasmValidateClient");

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.True(report.HasBlockingIssues);
        Assert.DoesNotContain(report.Checks, check => check.Id.StartsWith("manual-review:", StringComparison.Ordinal));
        Assert.Contains(report.Readiness.Checks, check =>
            check.Id == "mud-services" &&
            check.Status == InstallReadinessStatus.Missing &&
            check.FilePath is not null &&
            check.FilePath.EndsWith("Program.cs", StringComparison.Ordinal));
        Assert.Contains(report.Readiness.Checks, check =>
            check.Id == "endpoint-mapping" &&
            check.Status == InstallReadinessStatus.Missing &&
            check.SuggestedFix is not null &&
            check.SuggestedFix.Contains("MapFallbackToFile(\"index.html\")", StringComparison.Ordinal));
        Assert.Contains(report.Readiness.Checks, check =>
            check.Id == "shell-assets" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("wwwroot", "index.html"), StringComparison.Ordinal));
        Assert.Contains(report.Readiness.Checks, check =>
            check.Id == "mud-providers" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("Shared", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.Contains(report.Readiness.Checks, check =>
            check.Id == "chat-surface" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("Pages", "Index.razor"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenHostedWebAssemblyScaffoldWasApplied_ReportsNoBlockingIssues()
    {
        var projectPath = CreateProject(
            projectName: "HostedWasmAppliedApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\HostedWasmAppliedClient\HostedWasmAppliedClient.csproj" />
                  </ItemGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorPages();

                var app = builder.Build();
                app.UseBlazorFrameworkFiles();
                app.UseStaticFiles();
                app.MapFallbackToFile("index.html");
                app.Run();
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        CreateHostedWasmClientProject("HostedWasmAppliedClient");

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();
        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);
        await applier.ApplyAsync(plan, preview, provider: ScaffoldProvider.OpenAI);

        var analyzer = new InstallValidationAnalyzer();
        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.False(report.HasBlockingIssues);
        Assert.Contains(report.Checks, check => check.Id == "scaffold-manifest" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "manifest-host-match" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "manifest-files" && check.Status == InstallReadinessStatus.Pass);
        Assert.DoesNotContain(report.Checks, check => check.Id.StartsWith("manual-review:", StringComparison.Ordinal));
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

    private string CreateHostedWasmClientProject(string projectName)
    {
        var projectDirectory = Path.Combine(_tempDir, projectName);
        var sharedDirectory = Path.Combine(projectDirectory, "Shared");
        var pagesDirectory = Path.Combine(projectDirectory, "Pages");
        var wwwrootDirectory = Path.Combine(projectDirectory, "wwwroot");

        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(sharedDirectory);
        Directory.CreateDirectory(pagesDirectory);
        Directory.CreateDirectory(wwwrootDirectory);

        File.WriteAllText(
            Path.Combine(projectDirectory, $"{projectName}.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.0-preview.1.25125.3" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(wwwrootDirectory, "index.html"),
            """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <title>Hosted Client</title>
            </head>
            <body>
                <div id="app">Loading...</div>
            </body>
            </html>
            """);
        File.WriteAllText(Path.Combine(sharedDirectory, "MainLayout.razor"), "@Body");
        File.WriteAllText(Path.Combine(pagesDirectory, "Index.razor"), "@page \"/\"\n<h1>Hello client</h1>");

        return Path.Combine(projectDirectory, $"{projectName}.csproj");
    }
}
