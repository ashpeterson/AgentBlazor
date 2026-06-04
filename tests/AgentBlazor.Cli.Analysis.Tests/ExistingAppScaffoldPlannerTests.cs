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

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public async Task ApplyAsync_WhenAgentBlazorPackageExistsButMudBlazorPackageIsMissing_AddsMudBlazorReference()
    {
        var projectPath = CreateProject(
            projectName: "PackageFirstApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="AgentBlazor" Version="0.2.0" />
                  </ItemGroup>
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
        var preview = await applier.PreviewAsync(plan);

        await applier.ApplyAsync(plan, preview);
        var projectText = await File.ReadAllTextAsync(projectPath);
        var report = await new InstallReadinessAnalyzer().AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Contains(plan.Items, item => item.Id == "package-references" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains("""<PackageReference Include="AgentBlazor" Version="0.2.0" />""", projectText, StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="MudBlazor" Version="9.0.0" />""", projectText, StringComparison.Ordinal);
        Assert.Contains(report.Checks, check => check.Id == "package-references" && check.Status == InstallReadinessStatus.Pass);
    }

    [Fact]
    public async Task ApplyAsync_WhenProjectUsesCentralPackageManagement_AddsUnversionedReferencesAndPackageVersions()
    {
        var projectPath = CreateProject(
            projectName: "CentralPackageApp",
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
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var centralPackagesPath = Path.Combine(projectDirectory, "Directory.Packages.props");
        await File.WriteAllTextAsync(
            centralPackagesPath,
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();
        var preview = await applier.PreviewAsync(plan);

        await applier.ApplyAsync(plan, preview);
        var projectText = await File.ReadAllTextAsync(projectPath);
        var centralPackagesText = await File.ReadAllTextAsync(centralPackagesPath);

        Assert.Contains("""<PackageReference Include="AgentBlazor" />""", projectText, StringComparison.Ordinal);
        Assert.Contains("""<PackageReference Include="MudBlazor" />""", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"AgentBlazor\" Version=", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"MudBlazor\" Version=", projectText, StringComparison.Ordinal);
        Assert.Contains("""<PackageVersion Include="AgentBlazor" Version="0.2.16" />""", centralPackagesText, StringComparison.Ordinal);
        Assert.Contains("""<PackageVersion Include="MudBlazor" Version="9.0.0" />""", centralPackagesText, StringComparison.Ordinal);
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

        Assert.True(
            preview.HasChanges,
            string.Join(Environment.NewLine, plan.Items.Select(item => $"{item.Id}: {item.Action} -> {item.TargetPath}")));
        Assert.Contains(preview.Changes, change =>
            change.Path.EndsWith("Program.cs", StringComparison.Ordinal) &&
            change.UpdatedContent.Contains("AddAgentBlazor(", StringComparison.Ordinal));
        Assert.True(result.ChangedFileCount > 0);
        Assert.NotNull(result.ManifestPath);
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(report.IsReady, string.Join(Environment.NewLine, report.Checks.Select(check => $"{check.Id}: {check.Status} - {check.Message}")));
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
    public async Task PreviewAsync_WhenShellUsesCspNonces_CopiesNonceToInsertedAssets()
    {
        var projectPath = CreateProject(
            projectName: "NonceApp",
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
                    <link rel="stylesheet" href="@Assets["app.css"]" nonce="@Nonce" />
                </head>
                <body>
                    <Routes />
                    <script src="@Assets["_framework/blazor.web.js"]" nonce="@Nonce"></script>
                </body>
                </html>
                @code
                {
                    public string? Nonce => HttpContextAccessor?.HttpContext?.GetNonce();
                }
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

        var appShellChange = Assert.Single(
            preview.Changes,
            change => change.Path.EndsWith(Path.Combine("Components", "App.razor"), StringComparison.Ordinal));
        Assert.Contains("""<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" nonce="@Nonce" />""", appShellChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("""<link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" nonce="@Nonce" />""", appShellChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("""<script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]" nonce="@Nonce"></script>""", appShellChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("""<script src="@Assets[AgentBlazorAssetPaths.Js]" nonce="@Nonce"></script>""", appShellChange.UpdatedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WhenProjectUsesQuickGrid_DoesNotAddGlobalMudBlazorImport()
    {
        var projectPath = CreateProject(
            projectName: "QuickGridApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.AspNetCore.Components.QuickGrid" Version="10.0.0" />
                  </ItemGroup>
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

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        var importsChange = Assert.Single(preview.Changes, change => change.Path.EndsWith(Path.Combine("Components", "_Imports.razor"), StringComparison.Ordinal));
        var layoutChange = Assert.Single(preview.Changes, change => change.Path.EndsWith(Path.Combine("Components", "Layout", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.Contains("@using static Microsoft.AspNetCore.Components.Web.RenderMode", importsChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("@using AgentBlazor", importsChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("@using AgentBlazor.Components", importsChange.UpdatedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("@using MudBlazor", importsChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("@using MudBlazor", layoutChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("""<AgentChatWidget @rendermode="InteractiveServer" Title="Assistant" />""", layoutChange.UpdatedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WhenProjectUsesRunAsyncAndNonstandardRootPage_MapsEndpointAndMountsExistingRootPage()
    {
        var projectPath = CreateProject(
            projectName: "RunAsyncRootPageApp",
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
                await app.RunAsync().ConfigureAwait(false);
                """,
            appRazorBody: """
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/old-home"
                <h1>Old home</h1>
                """);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var rootPageDirectory = Path.Combine(projectDirectory, "Pages", "Admin");
        Directory.CreateDirectory(rootPageDirectory);
        File.WriteAllText(
            Path.Combine(rootPageDirectory, "Landing.razor"),
            """
            @page "/"
            <h1>Dashboard</h1>
            """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        var programChange = Assert.Single(preview.Changes, change => change.Path.EndsWith("Program.cs", StringComparison.Ordinal));
        Assert.Contains("app.MapAgentBlazorEndpoints();\nawait app.RunAsync().ConfigureAwait(false);", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains(preview.Changes, change =>
            change.Path.EndsWith(Path.Combine("Components", "Layout", "MainLayout.razor"), StringComparison.Ordinal) &&
            change.UpdatedContent.Contains("""<AgentChatWidget @rendermode="InteractiveServer" Title="Assistant" />""", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change =>
            change.Path.EndsWith(Path.Combine("Components", "Pages", "Home.razor"), StringComparison.Ordinal) &&
            change.UpdatedContent.Contains("<AgentChatWidget", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewAsync_WhenProjectUsesSharedLayoutFolder_MountsChatInLayout()
    {
        var projectPath = CreateProject(
            projectName: "SharedLayoutApp",
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
                <Routes />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello</h1>
                """);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        File.Delete(Path.Combine(projectDirectory, "Components", "Layout", "MainLayout.razor"));
        var sharedLayoutDirectory = Path.Combine(projectDirectory, "Components", "Shared", "Layout");
        Directory.CreateDirectory(sharedLayoutDirectory);
        File.WriteAllText(
            Path.Combine(sharedLayoutDirectory, "MainLayout.razor"),
            """
            <MudThemeProvider />
            <MudPopoverProvider />
            <MudDialogProvider />
            <MudSnackbarProvider />
            @Body

            @code
            {
            }
            """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        var layoutChange = Assert.Single(preview.Changes, change =>
            change.Path.EndsWith(Path.Combine("Components", "Shared", "Layout", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.Contains("@Body\n<AgentChatWidget @rendermode=\"InteractiveServer\" Title=\"Assistant\" />", layoutChange.UpdatedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(preview.Changes, change =>
            change.Path.EndsWith(Path.Combine("Components", "Pages", "Home.razor"), StringComparison.Ordinal) &&
            change.UpdatedContent.Contains("<AgentChatWidget", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewAsync_WhenMudServicesAreRegisteredOutsideProgram_DoesNotDuplicateMudRegistration()
    {
        var projectPath = CreateProject(
            projectName: "ExternalMudServicesApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="MudBlazor" Version="9.0.0" />
                  </ItemGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services
                    .AddApplication()
                    .AddInfrastructure(builder.Configuration)
                    .AddServerUI(builder.Configuration);

                var app = builder.Build();
                app.MapRazorComponents<App>()
                    .AddInteractiveServerRenderMode();
                app.Run();
                """,
            appRazorBody: """
                <Routes />
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
                <h1>Hello</h1>
                """);
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(projectPath)!, "ServiceRegistration.cs"),
            """
            using Microsoft.Extensions.DependencyInjection;
            using MudBlazor.Services;

            namespace ExternalMudServicesApp;

            public static class ServiceRegistration
            {
                public static IServiceCollection AddServerUI(this IServiceCollection services, IConfiguration config)
                {
                    services.AddRazorComponents()
                        .AddInteractiveServerComponents();
                    services.AddMudServices(config => { });
                    return services;
                }
            }
            """);

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        Assert.DoesNotContain(plan.Items, item => item.Id == "mud-services");
        var programChange = Assert.Single(preview.Changes, change => change.Path.EndsWith("Program.cs", StringComparison.Ordinal));
        Assert.DoesNotContain("builder.Services.AddMudServices();", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("using MudBlazor.Services;", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("builder.Services.AddAgentBlazor(", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.True(
            programChange.UpdatedContent.IndexOf("builder.Services.AddAgentBlazor(", StringComparison.Ordinal) >
            programChange.UpdatedContent.IndexOf(".AddServerUI(builder.Configuration);", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewAsync_WhenLocalSourceRootIsProvided_UsesProjectReferences()
    {
        var projectPath = CreateProject(
            projectName: "LocalSourceApp",
            csprojBody: """
                <?xml version="1.0" encoding="utf-8"?>
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <Target Name="CopyGeneratedFiles">
                    <Copy SourceFiles="@(GeneratedFiles)" DestinationFiles="@(GeneratedFiles->'$(OutputPath)%(RecursiveDir)%(Filename)%(Extension)')" />
                  </Target>
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
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", projectChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("GeneratedFiles->'$(OutputPath)%(RecursiveDir)%(Filename)%(Extension)'", projectChange.UpdatedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedFiles-&gt;", projectChange.UpdatedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WhenProviderIsSelected_ScaffoldsConcreteProviderRegistration()
    {
        var projectPath = CreateProject(
            projectName: "ProviderApp",
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

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);
        var programChange = Assert.Single(
            preview.Changes,
            change => change.Path.EndsWith("Program.cs", StringComparison.Ordinal));

        Assert.Contains(
            "options.UseOpenAI(\n        apiKey: builder.Configuration[\"OpenAI:ApiKey\"]!,\n        model: builder.Configuration[\"OpenAI:Model\"] ?? \"gpt-4o-mini\");",
            programChange.UpdatedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("// Recommended first path:", programChange.UpdatedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WhenAzureOpenAIProviderIsSelected_ScaffoldsConcreteProviderRegistration()
    {
        var projectPath = CreateProject(
            projectName: "AzureProviderApp",
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

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.AzureOpenAI);
        var programChange = Assert.Single(
            preview.Changes,
            change => change.Path.EndsWith("Program.cs", StringComparison.Ordinal));

        Assert.Contains(
            "options.UseAzureOpenAI(\n        endpoint: builder.Configuration[\"AzureOpenAI:Endpoint\"]!,\n        deploymentName: builder.Configuration[\"AzureOpenAI:DeploymentName\"]!,\n        apiKey: builder.Configuration[\"AzureOpenAI:ApiKey\"]);",
            programChange.UpdatedContent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("// Recommended first path:", programChange.UpdatedContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WhenProviderIsNotSelected_AddsExplicitProviderGuidanceComments()
    {
        var projectPath = CreateProject(
            projectName: "GuidanceApp",
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

        var preview = await applier.PreviewAsync(plan);
        var programChange = Assert.Single(
            preview.Changes,
            change => change.Path.EndsWith("Program.cs", StringComparison.Ordinal));

        Assert.Contains("// Recommended first path:", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("// options.UseOpenAI(", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("// options.UseAzureOpenAI(", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.Contains("// options.UseOllama(", programChange.UpdatedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\n    options.UseOpenAI(\n",
            programChange.UpdatedContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_WhenOqtaneStyleHostIsDetected_DowngradesRiskyEditsToManualReview()
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

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.False(plan.IsBlocked);
        Assert.True(plan.HasChanges);
        Assert.Contains(plan.Items, item => item.Id == "package-references" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "workflow-file" && item.Action == ScaffoldPlanAction.Create);
        Assert.Contains(plan.Items, item => item.Id == "agentblazor-services" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-services" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "endpoint-mapping" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-providers" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains("advanced Blazor host with Oqtane-style signals", plan.BlockReason, StringComparison.Ordinal);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Guidance is not null && item.Guidance.Contains("Oqtane host shell", StringComparison.Ordinal));
        Assert.NotNull(plan.BlockSuggestedFix);
    }

    [Fact]
    public async Task PreviewAsync_WhenOqtaneStyleHostIsDetected_ShowsOnlySafeFileEdits()
    {
        var projectPath = CreateProject(
            projectName: "OqtanePreviewApp",
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

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        Assert.True(preview.HasChanges);
        Assert.Contains(preview.Changes, change => change.Path.EndsWith("OqtanePreviewApp.csproj", StringComparison.Ordinal));
        Assert.Contains(preview.Changes, change => change.Path.EndsWith(Path.Combine("Workflows", "AppCapabilities.cs"), StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith("Program.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith("App.razor", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith("MainLayout.razor", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith("Home.razor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanAsync_WhenGenericLegacyHostIsDetected_DowngradesRiskyEditsToManualReview()
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

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.False(plan.IsBlocked);
        Assert.True(plan.HasChanges);
        Assert.Contains(plan.Items, item => item.Id == "package-references" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "workflow-file" && item.Action == ScaffoldPlanAction.Create);
        Assert.Contains(plan.Items, item => item.Id == "agentblazor-services" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-services" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "endpoint-mapping" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-providers" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains("legacy or custom Blazor host", plan.BlockReason, StringComparison.Ordinal);
        Assert.Contains(plan.Items, item => item.Id == "mud-services" && item.TargetPath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "agentblazor-services" && item.TargetPath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "workflow-registration" && item.TargetPath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "endpoint-mapping" && item.TargetPath.EndsWith("Startup.cs", StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.TargetPath.EndsWith(Path.Combine("Pages", "_Host.cshtml"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Guidance is not null && item.Guidance.Contains("_Host.cshtml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanAsync_WhenHostedWebAssemblyServerIsDetected_UsesReviewFirstGuidance()
    {
        var projectPath = CreateProject(
            projectName: "HostedWasmReviewApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\HostedWasmReviewClient\HostedWasmReviewClient.csproj" />
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

        var clientProjectPath = CreateHostedWasmClientProject("HostedWasmReviewClient");

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.False(plan.IsBlocked);
        Assert.True(plan.HasChanges);
        Assert.Equal(HostFamily.HostedWebAssembly, plan.Readiness.HostShape.Family);
        Assert.Equal(clientProjectPath, plan.Readiness.UiProjectPath);
        Assert.Contains(plan.Items, item => item.Id == "package-references" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "workflow-file" && item.Action == ScaffoldPlanAction.Create);
        Assert.Contains(plan.Items, item => item.Id == "ui-imports" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "ui-package-references" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "ui-imports" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-services" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "agentblazor-services" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "workflow-registration" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "endpoint-mapping" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-providers" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.TargetPath.EndsWith(Path.Combine("wwwroot", "index.html"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "mud-providers" && item.TargetPath.EndsWith(Path.Combine("Shared", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.TargetPath.EndsWith(Path.Combine("Pages", "Index.razor"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Guidance is not null && item.Guidance.Contains("remote components avoid the server-first", StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "ui-imports" && item.Guidance is not null && item.Guidance.Contains("AgentBlazor.Client.Chat", StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.Guidance is not null && item.Guidance.Contains("AgentRemoteChatWidget", StringComparison.Ordinal));
        Assert.NotNull(plan.BlockReason);
        Assert.Contains("hosted WebAssembly-style Blazor server host", plan.BlockReason, StringComparison.Ordinal);
        Assert.Contains("browser-client layout, provider, asset, and chat edits remain review-first", plan.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WhenHostedWebAssemblyServerIsDetected_LeavesClientUiEditsForManualReview()
    {
        var projectPath = CreateProject(
            projectName: "HostedWasmPreviewApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\HostedWasmPreviewClient\HostedWasmPreviewClient.csproj" />
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

        CreateHostedWasmClientProject("HostedWasmPreviewClient");

        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();

        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        Assert.Contains(preview.Changes, change => change.Path.EndsWith("HostedWasmPreviewApp.csproj", StringComparison.Ordinal));
        Assert.Contains(preview.Changes, change => change.Path.EndsWith("HostedWasmPreviewClient.csproj", StringComparison.Ordinal));
        Assert.Contains(preview.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmPreviewApp", "Program.cs"), StringComparison.Ordinal));
        Assert.Contains(preview.Changes, change => change.Path.EndsWith(Path.Combine("Workflows", "AppCapabilities.cs"), StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmPreviewClient", "_Imports.razor"), StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmPreviewClient", "wwwroot", "index.html"), StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmPreviewClient", "Shared", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmPreviewClient", "Pages", "Index.razor"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.Action == ScaffoldPlanAction.ManualReview);
    }

    [Fact]
    public async Task PlanAsync_WhenStandardWebAppUsesCompanionWebAssemblyClient_LeavesClientUiForManualReview()
    {
        var projectPath = CreateProject(
            projectName: "InteractiveWasmWebApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\InteractiveWasmWebApp.Client\InteractiveWasmWebApp.Client.csproj" />
                  </ItemGroup>
                </Project>
                """,
            programBody: """
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddRazorComponents()
                    .AddInteractiveWebAssemblyComponents();

                var app = builder.Build();
                app.MapRazorComponents<App>()
                    .AddInteractiveWebAssemblyRenderMode();
                app.Run();
                """,
            appRazorBody: """
                <Routes @rendermode="InteractiveWebAssembly" />
                """,
            mainLayoutBody: """
                @Body
                """,
            homeBody: """
                @page "/"
                <h1>Hello server</h1>
                """);

        var clientProjectPath = CreateHostedWasmClientProject("InteractiveWasmWebApp.Client");

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.Equal(HostFamily.StandardWebApp, plan.Readiness.HostShape.Family);
        Assert.Equal(clientProjectPath, plan.Readiness.UiProjectPath);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.Action == ScaffoldPlanAction.Update);
        Assert.Contains(plan.Items, item => item.Id == "shell-assets" && item.TargetPath.EndsWith(Path.Combine("InteractiveWasmWebApp", "Components", "App.razor"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "mud-providers" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.Action == ScaffoldPlanAction.ManualReview);
        Assert.Contains(plan.Items, item => item.Id == "mud-providers" && item.TargetPath.EndsWith(Path.Combine("InteractiveWasmWebApp.Client", "Shared", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.Contains(plan.Items, item => item.Id == "chat-surface" && item.TargetPath.EndsWith(Path.Combine("InteractiveWasmWebApp.Client", "Pages", "Index.razor"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyAsync_WhenHostedWebAssemblyServerIsDetected_LeavesClientUiReviewItems()
    {
        var projectPath = CreateProject(
            projectName: "HostedWasmApplyApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\HostedWasmApplyClient\HostedWasmApplyClient.csproj" />
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

        var clientProjectPath = CreateHostedWasmClientProject("HostedWasmApplyClient");
        var planner = new ExistingAppScaffoldPlanner();
        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);
        var applier = new ExistingAppScaffoldApplier();
        var preview = await applier.PreviewAsync(plan, provider: ScaffoldProvider.OpenAI);

        var result = await applier.ApplyAsync(plan, preview, provider: ScaffoldProvider.OpenAI);
        var readinessAnalyzer = new InstallReadinessAnalyzer();
        var report = await readinessAnalyzer.AnalyzeAsync(projectPath, hostProjectName: null);

        Assert.Contains(result.Changes, change => change.Path.EndsWith("HostedWasmApplyClient.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmApplyClient", "_Imports.razor"), StringComparison.Ordinal));
        Assert.DoesNotContain(result.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmApplyClient", "wwwroot", "index.html"), StringComparison.Ordinal));
        Assert.DoesNotContain(result.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmApplyClient", "Shared", "MainLayout.razor"), StringComparison.Ordinal));
        Assert.DoesNotContain(result.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmApplyClient", "Pages", "Index.razor"), StringComparison.Ordinal));
        Assert.Contains(result.Changes, change => change.Path.EndsWith(Path.Combine("HostedWasmApplyApp", "Program.cs"), StringComparison.Ordinal));
        Assert.Equal(clientProjectPath, report.UiProjectPath);
        Assert.Contains(report.Checks, check => check.Id == "mud-services" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "agentblazor-services" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "workflow-registration" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "endpoint-mapping" && check.Status == InstallReadinessStatus.Pass);
        Assert.Contains(report.Checks, check => check.Id == "shell-assets" && check.Status == InstallReadinessStatus.Warning);
        Assert.Contains(report.Checks, check => check.Id == "mud-providers" && check.Status == InstallReadinessStatus.Warning);
        Assert.Contains(report.Checks, check => check.Id == "chat-surface" && check.Status == InstallReadinessStatus.Warning);
        Assert.True(report.IsReady);
    }

    [Fact]
    public async Task PlanAsync_WhenHostCannotBeClassified_RemainsBlocked()
    {
        var projectPath = CreateProject(
            projectName: "BlockedHostApp",
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

        var planner = new ExistingAppScaffoldPlanner();

        var plan = await planner.PlanAsync(projectPath, hostProjectName: null);

        Assert.True(plan.IsBlocked);
        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Items);
        Assert.Contains("could not classify this host", plan.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanAsync_WhenTargetFrameworkIsUnsupported_BlocksBeforeScaffolding()
    {
        var projectPath = CreateProject(
            projectName: "UnsupportedFrameworkHostApp",
            csprojBody: """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net7.0</TargetFramework>
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

        Assert.True(plan.IsBlocked);
        Assert.Empty(plan.Items);
        Assert.Equal("Target framework support", plan.BlockTitle);
        Assert.Contains("net7.0", plan.BlockReason, StringComparison.Ordinal);
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
