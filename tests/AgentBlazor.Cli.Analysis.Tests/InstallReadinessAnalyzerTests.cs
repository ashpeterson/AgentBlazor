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

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.True(report.IsReady);
        Assert.Equal(0, report.MissingCount);
        Assert.All(report.Checks.Where(check => check.Id is not "chat-surface" and not "shell-assets" and not "mud-providers"),
            check => Assert.Equal(InstallReadinessStatus.Pass, check.Status));
        Assert.Contains(report.Checks, check => check.Id == "host-shape" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "chat-surface" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "shell-assets" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "mud-providers" && check.Status == InstallReadinessStatus.Pass);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenOqtaneStyleHostIsDetected_ReturnsAdvancedReviewShape()
    {
        var projectPath = CreateProject(
            projectName: "OqtaneHostApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Oqtane.Client" Version="6.0.0" />
                  </ItemGroup>
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

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        var hostShape = Assert.Single(report.Checks, check => check.Id == "host-shape");
        Assert.Equal(InstallReadinessStatus.Warning, hostShape.Status);
        Assert.Equal(HostShapeKind.AdvancedReview, report.HostShape.Kind);
        Assert.Contains("advanced Blazor host with Oqtane-style signals", hostShape.Message, StringComparison.Ordinal);
        Assert.NotNull(hostShape.SuggestedFix);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenGenericLegacyHostIsDetected_ReturnsAdvancedReviewShape()
    {
        var projectPath = CreateProject(
            projectName: "LegacyHostApp",
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

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Equal(HostShapeKind.AdvancedReview, report.HostShape.Kind);
        Assert.Contains("legacy or custom Blazor host", report.HostShape.Message, StringComparison.Ordinal);
        Assert.Contains(report.Checks, check =>
            check.Id == "mud-services" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(report.Checks, check =>
            check.Id == "endpoint-mapping" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(report.Checks, check =>
            check.Id == "shell-assets" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("Pages", "_Host.cshtml"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenHostedWebAssemblyServerIsDetected_ReturnsAdvancedReviewShape()
    {
        var projectPath = CreateProject(
            projectName: "HostedWasmServerApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\HostedWasmClientApp\HostedWasmClientApp.csproj" />
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

        var clientProjectPath = CreateHostedWasmClientProject("HostedWasmClientApp");

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Equal(HostShapeKind.AdvancedReview, report.HostShape.Kind);
        Assert.Equal(HostFamily.HostedWebAssembly, report.HostShape.Family);
        Assert.Equal("HostedWasmClientApp", report.UiProjectName);
        Assert.Equal(clientProjectPath, report.UiProjectPath);
        Assert.Contains("hosted WebAssembly-style Blazor server host", report.HostShape.Message, StringComparison.Ordinal);
        Assert.Contains("browser-client layout, provider, asset, and chat edits remain review-first", report.HostShape.Message, StringComparison.Ordinal);
        Assert.Contains("remote/server-backed WebAssembly client chat path", report.HostShape.Message, StringComparison.Ordinal);
        Assert.Contains(report.Checks, check =>
            check.Id == "shell-assets" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("wwwroot", "index.html"), StringComparison.Ordinal));
        Assert.Contains(report.Checks, check =>
            check.Id == "mud-providers" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("Shared", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.Contains(report.Checks, check =>
            check.Id == "chat-surface" &&
            check.FilePath is not null &&
            check.FilePath.EndsWith(Path.Combine("Pages", "Index.razor"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenHostCannotBeClassified_ReturnsUnsupportedShape()
    {
        var projectPath = CreateProject(
            projectName: "UnknownHostApp",
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
                builder.Services.AddControllers();

                var app = builder.Build();
                app.MapControllers();
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

        var analyzer = new InstallReadinessAnalyzer();

        var report = await analyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Equal(HostShapeKind.Unsupported, report.HostShape.Kind);
        Assert.Contains("could not classify this host", report.HostShape.Message, StringComparison.Ordinal);
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
