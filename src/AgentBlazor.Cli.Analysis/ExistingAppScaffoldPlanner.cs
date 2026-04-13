using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class ExistingAppScaffoldPlanner
{
    private readonly InstallReadinessAnalyzer _readinessAnalyzer = new();

    public async Task<ScaffoldPlan> PlanAsync(
        string solutionOrProjectPath,
        string? hostProjectName,
        CancellationToken ct = default)
    {
        var readiness = await _readinessAnalyzer.AnalyzeAsync(solutionOrProjectPath, hostProjectName, ct).ConfigureAwait(false);
        var targetFiles = ResolveTargetFiles(readiness);
        var items = new List<ScaffoldPlanItem>();
        var hostShape = readiness.HostShape;
        var usesWebAssemblyClientUi = UsesWebAssemblyClientUi(readiness);
        var canAutoPatchUi = !usesWebAssemblyClientUi;
        var canAutoPatchAdvancedUi = hostShape.Family == HostFamily.HostedWebAssembly && !string.IsNullOrWhiteSpace(readiness.UiProjectPath) && canAutoPatchUi;
        var canAutoPatchAdvancedStartup = hostShape.Family == HostFamily.HostedWebAssembly;

        if (hostShape.Kind == HostShapeKind.Unsupported)
        {
            return new ScaffoldPlan
            {
                InputPath = solutionOrProjectPath,
                HostProjectName = readiness.HostProjectName,
                HostProjectPath = readiness.HostProjectPath,
                Readiness = readiness,
                IsBlocked = true,
                BlockTitle = hostShape.Title,
                BlockReason = hostShape.Message,
                BlockSuggestedFix = hostShape.SuggestedFix
            };
        }

        AddIfMissingOrWarning(readiness, "package-references", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "package-references",
                Title = "Add package references",
                Action = ScaffoldPlanAction.Update,
                TargetPath = readiness.HostProjectPath,
                Summary = "Add missing AgentBlazor and/or MudBlazor package references to the host project.",
                Reason = "The host project must reference the baseline runtime and UI packages before startup wiring can compile."
            });
        });

        AddIfMissing(readiness, "mud-services", () =>
        {
            items.Add(CreateProgramUpdate("mud-services", targetFiles.ProgramPath,
                "Add builder.Services.AddMudServices().",
                "MudBlazor services must be registered before layout providers and AgentBlazor components can function.",
                hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedStartup,
                BuildGuidance(hostShape.Family, "mud-services")));
        });

        AddIfMissing(readiness, "agentblazor-services", () =>
        {
            items.Add(CreateProgramUpdate("agentblazor-services", targetFiles.ProgramPath,
                "Add builder.Services.AddAgentBlazor(...) with provider and builder configuration.",
                "AgentBlazor runtime wiring starts in the host startup path.",
                hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedStartup,
                BuildGuidance(hostShape.Family, "agentblazor-services")));
        });

        AddIfMissing(readiness, "workflow-registration", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "workflow-file",
                Title = "Create starter workflow capability",
                Action = ScaffoldPlanAction.Create,
                TargetPath = targetFiles.WorkflowPath,
                Summary = "Create a starter capability/workflow class for the app.",
                Reason = "The runtime needs at least one workflow registration to expose meaningful agent behavior."
            });

            items.Add(CreateProgramUpdate("workflow-registration", targetFiles.ProgramPath,
                "Register the starter workflow with AddWorkflow<T>().",
                "The workflow class must be registered so the agent is active on at least one route.",
                hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedStartup,
                BuildGuidance(hostShape.Family, "workflow-registration")));
        });

        AddIfMissing(readiness, "endpoint-mapping", () =>
        {
            items.Add(CreateProgramUpdate("endpoint-mapping", targetFiles.ProgramPath,
                "Add app.MapAgentBlazorEndpoints() after the Razor components route mapping.",
                "Without endpoint mapping, chat UI can render but the runtime endpoint is unavailable.",
                hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedStartup,
                BuildGuidance(hostShape.Family, "endpoint-mapping")));
        });

        if (ShouldAddUiImports(readiness))
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "ui-imports",
                Title = "Patch UI imports",
                Action = !canAutoPatchUi || targetFiles.ImportsPath is null ? ScaffoldPlanAction.ManualReview : File.Exists(targetFiles.ImportsPath)
                    ? ScaffoldPlanAction.Update
                    : ScaffoldPlanAction.Create,
                TargetPath = targetFiles.ImportsPath ?? targetFiles.ProjectDirectory,
                Summary = "Add AgentBlazor component imports to the client UI project.",
                Reason = "Client-side AgentBlazor surfaces need the AgentBlazor component namespace available without adding broad UI-library imports globally.",
                Guidance = BuildUiGuidance(hostShape.Family, "ui-imports", usesWebAssemblyClientUi)
            });
        }

        AddIfMissingOrWarning(readiness, "shell-assets", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "shell-assets",
                Title = "Patch host shell assets",
                Action = (hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedUi) || targetFiles.AppShellPath is null
                    ? ScaffoldPlanAction.ManualReview
                    : ScaffoldPlanAction.Update,
                TargetPath = targetFiles.AppShellPath ?? targetFiles.ProjectDirectory,
                Summary = "Add AgentBlazor CSS/JS assets to App.razor or the equivalent host shell.",
                Reason = "AgentBlazor and MudBlazor UI assets need to be present in the host shell for chat and component surfaces.",
                Guidance = BuildGuidance(hostShape.Family, "shell-assets")
            });
        });

        AddIfMissingOrWarning(readiness, "mud-providers", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "mud-providers",
                Title = "Patch layout providers",
                Action = !canAutoPatchUi || (hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedUi) || targetFiles.MainLayoutPath is null
                    ? ScaffoldPlanAction.ManualReview
                    : ScaffoldPlanAction.Update,
                TargetPath = targetFiles.MainLayoutPath ?? targetFiles.ProjectDirectory,
                Summary = "Ensure the main layout includes the standard MudBlazor provider set.",
                Reason = "MudBlazor-backed AgentBlazor surfaces rely on the theme, popover, dialog, and snackbar providers.",
                Guidance = BuildUiGuidance(hostShape.Family, "mud-providers", usesWebAssemblyClientUi)
            });
        });

        AddIfMissingOrWarning(readiness, "chat-surface", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "chat-surface",
                Title = "Add chat entry point",
                Action = !canAutoPatchUi || hostShape.Kind == HostShapeKind.AdvancedReview && !canAutoPatchAdvancedUi
                    ? ScaffoldPlanAction.ManualReview
                    : targetFiles.ChatPagePath is null
                        ? ScaffoldPlanAction.Create
                        : ScaffoldPlanAction.Update,
                TargetPath = targetFiles.ChatPagePath ?? targetFiles.FallbackChatPagePath,
                Summary = "Mount AgentChatWidget on a standard page so the runtime is reachable in the UI.",
                Reason = "An installed runtime is difficult to validate if the app has no visible chat surface.",
                Guidance = BuildUiGuidance(hostShape.Family, "chat-surface", usesWebAssemblyClientUi)
            });
        });

        return new ScaffoldPlan
        {
            InputPath = solutionOrProjectPath,
            HostProjectName = readiness.HostProjectName,
            HostProjectPath = readiness.HostProjectPath,
            Readiness = readiness,
            BlockTitle = hostShape.Kind == HostShapeKind.AdvancedReview ? hostShape.Title : null,
            BlockReason = hostShape.Kind == HostShapeKind.AdvancedReview ? hostShape.Message : null,
            BlockSuggestedFix = hostShape.Kind == HostShapeKind.AdvancedReview ? hostShape.SuggestedFix : null,
            Items = items
        };
    }

    private static void AddIfMissing(InstallReadinessReport readiness, string checkId, Action addAction)
    {
        var check = readiness.Checks.FirstOrDefault(item => item.Id == checkId);
        if (check?.Status == InstallReadinessStatus.Missing)
        {
            addAction();
        }
    }

    private static void AddIfMissingOrWarning(InstallReadinessReport readiness, string checkId, Action addAction)
    {
        var check = readiness.Checks.FirstOrDefault(item => item.Id == checkId);
        if (check is not null && check.Status != InstallReadinessStatus.Pass)
        {
            addAction();
        }
    }

    private static ScaffoldPlanItem CreateProgramUpdate(
        string id,
        string? programPath,
        string summary,
        string reason,
        bool manualReviewOnly = false,
        string? guidance = null) =>
        new()
        {
            Id = id,
            Title = "Patch Program.cs",
            Action = manualReviewOnly || programPath is null ? ScaffoldPlanAction.ManualReview : ScaffoldPlanAction.Update,
            TargetPath = programPath ?? "(manual startup review required)",
            Summary = summary,
            Reason = reason,
            Guidance = guidance
        };

    private static string? BuildGuidance(HostFamily family, string itemId)
        => (family, itemId) switch
        {
            (HostFamily.StandardWebApp, _) => null,
            (HostFamily.LegacyServer, "mud-services") =>
                "Add MudBlazor services in the legacy startup path, typically `Program.cs` or `Startup.ConfigureServices`, alongside `AddServerSideBlazor()` and `AddRazorPages()`.",
            (HostFamily.LegacyServer, "agentblazor-services") =>
                "Register `AddAgentBlazor(...)` in the legacy server startup path, usually in `Program.cs` or `Startup.ConfigureServices`, rather than assuming modern `AddRazorComponents()` wiring.",
            (HostFamily.LegacyServer, "workflow-registration") =>
                "Keep the workflow class file, then wire `AddWorkflow<T>()` inside the legacy server `AddAgentBlazor(...)` registration path.",
            (HostFamily.LegacyServer, "endpoint-mapping") =>
                "Map AgentBlazor endpoints in the legacy app pipeline near `MapBlazorHub()` / `MapFallbackToPage(\"/_Host\")`, not in the modern `MapRazorComponents()` chain.",
            (HostFamily.LegacyServer, "shell-assets") =>
                "Add the AgentBlazor and MudBlazor CSS/JS asset references in the host page that serves the app shell, commonly `Pages/_Host.cshtml`.",
            (HostFamily.LegacyServer, "mud-providers") =>
                "Ensure the layout or root component rendered from the legacy host page includes the Mud theme, popover, dialog, and snackbar providers.",
            (HostFamily.LegacyServer, "chat-surface") =>
                "Mount `AgentChatWidget` or `AgentChatSurface` on a page/component that is reachable from the legacy `_Host`-based app shell.",
            (HostFamily.HostedWebAssembly, "ui-imports") =>
                "Review the hosted WebAssembly client `_Imports.razor` manually. The current source package boundary is server-first, so client-side AgentBlazor UI edits need a browser-safe package split or a remote-client integration path before auto-scaffold can write them safely.",
            (HostFamily.HostedWebAssembly, "mud-services") =>
                "Add MudBlazor services in the hosted WebAssembly server startup path, typically `Program.cs`, alongside the API/static file pipeline that serves the client app.",
            (HostFamily.HostedWebAssembly, "agentblazor-services") =>
                "Register `AddAgentBlazor(...)` in the hosted WebAssembly server startup path, not in the client WebAssembly bootstrap code.",
            (HostFamily.HostedWebAssembly, "workflow-registration") =>
                "Keep the workflow class file in the server project, then wire `AddWorkflow<T>()` inside the server-side `AddAgentBlazor(...)` registration path.",
            (HostFamily.HostedWebAssembly, "endpoint-mapping") =>
                "Map AgentBlazor endpoints in the hosted WebAssembly server pipeline before the `MapFallbackToFile(\"index.html\")` client fallback.",
            (HostFamily.HostedWebAssembly, "shell-assets") =>
                "Add the AgentBlazor and MudBlazor CSS/JS asset references in the hosted WebAssembly client shell, commonly `wwwroot/index.html` in the client project.",
            (HostFamily.HostedWebAssembly, "mud-providers") =>
                "Review the hosted WebAssembly client layout manually before adding Mud providers; client-side provider edits also require the client project to reference browser-compatible UI dependencies.",
            (HostFamily.HostedWebAssembly, "chat-surface") =>
                "Use a remote/server-backed chat integration for hosted WebAssembly clients. Do not auto-mount the current server-first AgentBlazor chat surface in a browser-only client project.",
            (HostFamily.OqtaneStyle, "mud-services") =>
                "Add MudBlazor services in the Oqtane host startup path where platform service registration is composed, not by assuming a standard standalone Blazor `Program.cs` layout.",
            (HostFamily.OqtaneStyle, "agentblazor-services") =>
                "Register `AddAgentBlazor(...)` in the Oqtane host/service composition layer that owns app services and module wiring.",
            (HostFamily.OqtaneStyle, "workflow-registration") =>
                "Keep the scaffolded workflow file, then wire `AddWorkflow<T>()` into the Oqtane host's AgentBlazor registration path once you identify the correct startup module/service extension point.",
            (HostFamily.OqtaneStyle, "endpoint-mapping") =>
                "Map AgentBlazor endpoints in the Oqtane app pipeline only after confirming the correct host-level routing extension point.",
            (HostFamily.OqtaneStyle, "shell-assets") =>
                "Add AgentBlazor and MudBlazor assets in the Oqtane host shell or theme layer that renders the final page shell, not by assuming `Components/App.razor` is authoritative.",
            (HostFamily.OqtaneStyle, "mud-providers") =>
                "Place the MudBlazor providers in the layout or shell component that actually wraps rendered module content in the Oqtane host.",
            (HostFamily.OqtaneStyle, "chat-surface") =>
                "Mount the chat surface in the Oqtane page/module route where you want AgentBlazor reachable, after confirming how the host composes page content.",
            _ => null
        };

    private static string? BuildUiGuidance(HostFamily family, string itemId, bool usesWebAssemblyClientUi)
        => usesWebAssemblyClientUi
            ? itemId switch
            {
                "ui-imports" =>
                    "Review the WebAssembly client `_Imports.razor` manually. The current AgentBlazor source package boundary is server-first, so client-side AgentBlazor UI edits need a browser-safe package split or remote-client integration path before auto-scaffold can write them safely.",
                "mud-providers" =>
                    "Review the WebAssembly client layout manually before adding Mud providers; client-side provider edits also require the client project to reference browser-compatible UI dependencies.",
                "chat-surface" =>
                    "Use a remote/server-backed chat integration for WebAssembly clients. Do not auto-mount the current server-first AgentBlazor chat surface in a browser-only client project.",
                _ => BuildGuidance(family, itemId)
            }
            : BuildGuidance(family, itemId);

    private static ScaffoldTargetFiles ResolveTargetFiles(InstallReadinessReport readiness)
    {
        var hostProjectDirectory = Path.GetDirectoryName(readiness.HostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the project directory for '{readiness.HostProjectPath}'.");
        var uiProjectPath = readiness.UiProjectPath ?? readiness.HostProjectPath;
        var uiProjectDirectory = Path.GetDirectoryName(uiProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the project directory for '{uiProjectPath}'.");

        var startupPath = ResolveStartupPath(hostProjectDirectory, readiness.HostShape.Family);
        var shellProjectDirectory = readiness.HostShape.Family == HostFamily.HostedWebAssembly
            ? uiProjectDirectory
            : hostProjectDirectory;
        var appShellPath = ResolveAppShellPath(shellProjectDirectory, readiness.HostShape.Family);
        var mainLayoutPath = ResolveFirstExistingPath(
            uiProjectDirectory,
            Path.Combine("Shared", "MainLayout.razor"),
            Path.Combine("Components", "Layout", "MainLayout.razor"),
            Path.Combine("Layout", "MainLayout.razor"));
        var chatPagePath = ResolveChatSurfacePath(uiProjectDirectory);

        return new ScaffoldTargetFiles(
            ProjectDirectory: hostProjectDirectory,
            ProgramPath: startupPath,
            ImportsPath: ResolveImportsPath(uiProjectDirectory),
            AppShellPath: appShellPath,
            MainLayoutPath: mainLayoutPath,
            ChatPagePath: chatPagePath,
            FallbackChatPagePath: Path.Combine(uiProjectDirectory, "Components", "Pages", "Home.razor"),
            WorkflowPath: Path.Combine(hostProjectDirectory, "Workflows", "AppCapabilities.cs"));
    }

    private static string? ResolveStartupPath(string projectDirectory, HostFamily family)
        => family switch
        {
            HostFamily.LegacyServer => ResolveFirstExistingPath(projectDirectory, "Startup.cs", "Program.cs"),
            _ => ResolveFirstExistingPath(projectDirectory, "Program.cs", "Startup.cs")
        };

    private static string? ResolveAppShellPath(string projectDirectory, HostFamily family)
        => family switch
        {
            HostFamily.LegacyServer => ResolveFirstExistingPath(
                projectDirectory,
                Path.Combine("Pages", "_Host.cshtml"),
                Path.Combine("Components", "App.razor"),
                "App.razor"),
            HostFamily.HostedWebAssembly => ResolveFirstExistingPath(
                projectDirectory,
                Path.Combine("wwwroot", "index.html"),
                "App.razor",
                Path.Combine("Components", "App.razor")),
            _ => ResolveFirstExistingPath(
                projectDirectory,
                Path.Combine("Components", "App.razor"),
                "App.razor",
                Path.Combine("Pages", "_Host.cshtml"))
        };

    private static string ResolveImportsPath(string projectDirectory)
        => ResolveFirstExistingPath(
            projectDirectory,
            "_Imports.razor",
            Path.Combine("Components", "_Imports.razor"))
            ?? Path.Combine(projectDirectory, "_Imports.razor");

    private static bool ShouldAddUiImports(InstallReadinessReport readiness)
        => UsesCompanionUiProject(readiness) &&
           readiness.Checks.Any(check =>
               check.Id is "mud-providers" or "chat-surface" &&
               check.Status != InstallReadinessStatus.Pass);

    private static bool UsesWebAssemblyClientUi(InstallReadinessReport readiness)
        => UsesCompanionUiProject(readiness) &&
           readiness.HostShape.Family is HostFamily.HostedWebAssembly or HostFamily.StandardWebApp;

    private static bool UsesCompanionUiProject(InstallReadinessReport readiness)
        => !string.IsNullOrWhiteSpace(readiness.UiProjectPath) &&
           !string.Equals(readiness.UiProjectPath, readiness.HostProjectPath, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveFirstExistingPath(string projectDirectory, params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var candidate = Path.Combine(projectDirectory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveChatSurfacePath(string projectDirectory)
    {
        var knownPagePaths = new[]
        {
            Path.Combine("Pages", "Index.razor"),
            Path.Combine("Pages", "Home.razor"),
            Path.Combine("Pages", "Public", "Index.razor"),
            Path.Combine("Pages", "Dashboard", "Dashboard.razor"),
            Path.Combine("Components", "Pages", "Home.razor"),
            Path.Combine("Components", "Pages", "Index.razor"),
            Path.Combine("Shared", "Index.razor")
        };

        return ResolveFirstExistingPathMatching(projectDirectory, IsRootRazorPage, knownPagePaths)
            ?? ResolveFirstRootRazorPagePath(projectDirectory)
            ?? ResolveFirstExistingPath(projectDirectory, knownPagePaths);
    }

    private static string? ResolveFirstExistingPathMatching(string projectDirectory, Func<string, bool> predicate, params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var candidate = Path.Combine(projectDirectory, relativePath);
            if (File.Exists(candidate) && predicate(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveFirstRootRazorPagePath(string projectDirectory)
        => Directory
            .EnumerateFiles(projectDirectory, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(IsRootRazorPage);

    private static bool IsRootRazorPage(string path)
        => File.ReadAllText(path).Contains("@page \"/\"", StringComparison.Ordinal);

    private sealed record ScaffoldTargetFiles(
        string ProjectDirectory,
        string? ProgramPath,
        string? ImportsPath,
        string? AppShellPath,
        string? MainLayoutPath,
        string? ChatPagePath,
        string FallbackChatPagePath,
        string WorkflowPath);
}
