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
        var targetFiles = ResolveTargetFiles(readiness.HostProjectPath);
        var items = new List<ScaffoldPlanItem>();

        AddIfMissing(readiness, "package-references", () =>
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
                "MudBlazor services must be registered before layout providers and AgentBlazor components can function."));
        });

        AddIfMissing(readiness, "agentblazor-services", () =>
        {
            items.Add(CreateProgramUpdate("agentblazor-services", targetFiles.ProgramPath,
                "Add builder.Services.AddAgentBlazor(...) with provider and builder configuration.",
                "AgentBlazor runtime wiring starts in the host startup path."));
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
                "The workflow class must be registered so the agent is active on at least one route."));
        });

        AddIfMissing(readiness, "endpoint-mapping", () =>
        {
            items.Add(CreateProgramUpdate("endpoint-mapping", targetFiles.ProgramPath,
                "Add app.MapAgentBlazorEndpoints() after the Razor components route mapping.",
                "Without endpoint mapping, chat UI can render but the runtime endpoint is unavailable."));
        });

        AddIfMissingOrWarning(readiness, "shell-assets", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "shell-assets",
                Title = "Patch host shell assets",
                Action = targetFiles.AppShellPath is null ? ScaffoldPlanAction.ManualReview : ScaffoldPlanAction.Update,
                TargetPath = targetFiles.AppShellPath ?? targetFiles.ProjectDirectory,
                Summary = "Add AgentBlazor CSS/JS assets to App.razor or the equivalent host shell.",
                Reason = "AgentBlazor and MudBlazor UI assets need to be present in the host shell for chat and component surfaces."
            });
        });

        AddIfMissingOrWarning(readiness, "mud-providers", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "mud-providers",
                Title = "Patch layout providers",
                Action = targetFiles.MainLayoutPath is null ? ScaffoldPlanAction.ManualReview : ScaffoldPlanAction.Update,
                TargetPath = targetFiles.MainLayoutPath ?? targetFiles.ProjectDirectory,
                Summary = "Ensure the main layout includes the standard MudBlazor provider set.",
                Reason = "MudBlazor-backed AgentBlazor surfaces rely on the theme, popover, dialog, and snackbar providers."
            });
        });

        AddIfMissingOrWarning(readiness, "chat-surface", () =>
        {
            items.Add(new ScaffoldPlanItem
            {
                Id = "chat-surface",
                Title = "Add chat entry point",
                Action = targetFiles.ChatPagePath is null ? ScaffoldPlanAction.Create : ScaffoldPlanAction.Update,
                TargetPath = targetFiles.ChatPagePath ?? targetFiles.FallbackChatPagePath,
                Summary = "Mount AgentChatWidget on a standard page so the runtime is reachable in the UI.",
                Reason = "An installed runtime is difficult to validate if the app has no visible chat surface."
            });
        });

        return new ScaffoldPlan
        {
            InputPath = solutionOrProjectPath,
            HostProjectName = readiness.HostProjectName,
            HostProjectPath = readiness.HostProjectPath,
            Readiness = readiness,
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

    private static ScaffoldPlanItem CreateProgramUpdate(string id, string? programPath, string summary, string reason) =>
        new()
        {
            Id = id,
            Title = "Patch Program.cs",
            Action = programPath is null ? ScaffoldPlanAction.ManualReview : ScaffoldPlanAction.Update,
            TargetPath = programPath ?? "(manual startup review required)",
            Summary = summary,
            Reason = reason
        };

    private static ScaffoldTargetFiles ResolveTargetFiles(string hostProjectPath)
    {
        var projectDirectory = Path.GetDirectoryName(hostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the project directory for '{hostProjectPath}'.");

        var programPath = ResolveFirstExistingPath(projectDirectory, "Program.cs");
        var appShellPath = ResolveFirstExistingPath(projectDirectory, Path.Combine("Components", "App.razor"), "App.razor");
        var mainLayoutPath = ResolveFirstExistingPath(
            projectDirectory,
            Path.Combine("Components", "Layout", "MainLayout.razor"),
            Path.Combine("Shared", "MainLayout.razor"),
            Path.Combine("Layout", "MainLayout.razor"));
        var chatPagePath = ResolveFirstExistingPath(
            projectDirectory,
            Path.Combine("Components", "Pages", "Home.razor"),
            Path.Combine("Components", "Pages", "Index.razor"),
            Path.Combine("Pages", "Index.razor"),
            Path.Combine("Pages", "Home.razor"));

        return new ScaffoldTargetFiles(
            ProjectDirectory: projectDirectory,
            ProgramPath: programPath,
            AppShellPath: appShellPath,
            MainLayoutPath: mainLayoutPath,
            ChatPagePath: chatPagePath,
            FallbackChatPagePath: Path.Combine(projectDirectory, "Components", "Pages", "Home.razor"),
            WorkflowPath: Path.Combine(projectDirectory, "Workflows", "AppCapabilities.cs"));
    }

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

    private sealed record ScaffoldTargetFiles(
        string ProjectDirectory,
        string? ProgramPath,
        string? AppShellPath,
        string? MainLayoutPath,
        string? ChatPagePath,
        string FallbackChatPagePath,
        string WorkflowPath);
}
