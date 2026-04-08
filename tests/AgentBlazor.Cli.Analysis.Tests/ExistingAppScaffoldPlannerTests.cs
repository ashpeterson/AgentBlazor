using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;
using System.Text.Json;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class ExistingAppScaffoldPlannerTests : IDisposable
{
    private readonly string _tempDir;

    public ExistingAppScaffoldPlannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-scaffold-{Guid.NewGuid():N}");
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
    public async Task PlanAsync_WhenProjectIsMissingBaselineWiring_ProposesCoreScaffoldChanges()
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

        Assert.True(plan.HasChanges);
        Assert.Contains(plan.Items, item => item.Id == "package-references");
        Assert.Contains(plan.Items, item => item.Id == "agentblazor-services");
        Assert.Contains(plan.Items, item => item.Id == "workflow-file");
        Assert.Contains(plan.Items, item => item.Id == "endpoint-mapping");
        Assert.Contains(plan.Items, item => item.Id == "shell-assets");
        Assert.Contains(plan.Items, item => item.Id == "mud-providers");
        Assert.Contains(plan.Items, item => item.Id == "chat-surface");
    }

    [Fact]
    public async Task PlanAsync_WhenProjectIsAlreadyReady_ReturnsNoProposedChanges()
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

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public async Task ApplyAsync_WhenProjectIsMissingBaselineWiring_MakesProjectReady()
    {
        var projectPath = CreateProject(
            projectName: "InstallableApp",
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
                builder.Services.AddRazorComponents()
                    .AddInteractiveServerComponents();

                var app = builder.Build();
                app.MapRazorComponents<App>()
                    .AddInteractiveServerRenderMode();
                app.Run();
                """,
            appRazorBody: """
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <base href="/" />
                </head>
                <body>
                    <Routes />
                    <script src="@Assets["_framework/blazor.web.js"]"></script>
                </body>
                </html>
                """,
            mainLayoutBody: """
                @inherits LayoutComponentBase

                <main>
                    @Body
                </main>
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();
        var preview = await applier.PreviewAsync(plan);

        var result = await applier.ApplyAsync(plan, preview);
        var readinessAnalyzer = new InstallReadinessAnalyzer();
        var report = await readinessAnalyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.True(preview.HasChanges);
        Assert.Contains(preview.Changes, change =>
            change.Path.EndsWith("Program.cs", StringComparison.Ordinal) &&
            change.UpdatedContent.Contains("AddAgentBlazor(", StringComparison.Ordinal));
        Assert.True(result.ChangedFileCount > 0);
        Assert.NotNull(result.ManifestPath);
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(report.IsReady);
        Assert.Equal(0, report.MissingCount);
        Assert.All(report.Checks.Where(check => check.Status != InstallReadinessStatus.Warning),
            check => Assert.Equal(InstallReadinessStatus.Pass, check.Status));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath!));
        var root = document.RootElement;
        Assert.Equal("InstallableApp", root.GetProperty("hostProjectName").GetString());
        Assert.True(root.GetProperty("changedFiles").GetArrayLength() >= result.ChangedFileCount);
        Assert.Contains(root.GetProperty("changedFiles").EnumerateArray(),
            file => string.Equals(file.GetProperty("relativePath").GetString(), "Program.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewAsync_WhenLocalSourceRootIsProvided_UsesProjectReferences()
    {
        var projectPath = CreateProject(
            projectName: "LocalSourceApp",
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
        var sourceRoot = CreateAgentBlazorSourceRoot();
        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();

        var preview = await applier.PreviewAsync(plan, sourceRoot);
        var projectChange = Assert.Single(
            preview.Changes,
            change => change.Path.EndsWith("LocalSourceApp.csproj", StringComparison.Ordinal));

        Assert.Contains("ProjectReference", projectChange.UpdatedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"AgentBlazor\"", projectChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("AgentBlazor.Core/AgentBlazor.Core.csproj", projectChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("AgentBlazor.Hosting/AgentBlazor.Hosting.csproj", projectChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("AgentBlazor.Components/AgentBlazor.Components.csproj", projectChange.UpdatedContent, StringComparison.Ordinal);
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

    private string CreateAgentBlazorSourceRoot()
    {
        var sourceRoot = Path.Combine(_tempDir, "agentblazor-source");
        var coreDirectory = Path.Combine(sourceRoot, "src", "AgentBlazor.Core");
        var hostingDirectory = Path.Combine(sourceRoot, "src", "AgentBlazor.Hosting");
        var componentsDirectory = Path.Combine(sourceRoot, "src", "AgentBlazor.Components");

        Directory.CreateDirectory(coreDirectory);
        Directory.CreateDirectory(hostingDirectory);
        Directory.CreateDirectory(componentsDirectory);

        File.WriteAllText(Path.Combine(coreDirectory, "AgentBlazor.Core.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(hostingDirectory, "AgentBlazor.Hosting.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(componentsDirectory, "AgentBlazor.Components.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        return sourceRoot;
    }
}
