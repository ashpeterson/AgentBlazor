using System.Xml.Linq;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class InstallReadinessAnalyzer
{
    private readonly SolutionLoader _solutionLoader = new();

    public async Task<InstallReadinessReport> AnalyzeAsync(
        string solutionOrProjectPath,
        string? hostProjectName,
        CancellationToken ct = default)
    {
        var hostProject = await ResolveHostProjectAsync(solutionOrProjectPath, hostProjectName, ct);
        var hostProjectPath = hostProject.ProjectPath;
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the host project directory for '{hostProject.Name}'.");

        var csprojText = await File.ReadAllTextAsync(hostProjectPath, ct).ConfigureAwait(false);
        var csharpContents = await ReadProjectFilesAsync(hostProjectDirectory, ".cs", ct).ConfigureAwait(false);
        var razorContents = await ReadProjectFilesAsync(hostProjectDirectory, ".razor", ct).ConfigureAwait(false);

        var checks = new List<InstallReadinessCheck>
        {
            BuildPackageCheck(hostProjectPath, csprojText),
            BuildMudServiceCheck(csharpContents),
            BuildAddAgentBlazorCheck(csharpContents),
            BuildWorkflowCheck(csharpContents),
            BuildEndpointCheck(csharpContents),
            BuildShellAssetsCheck(razorContents),
            BuildMudProvidersCheck(razorContents),
            BuildChatSurfaceCheck(razorContents)
        };

        return new InstallReadinessReport
        {
            InputPath = solutionOrProjectPath,
            HostProjectName = hostProject.Name,
            HostProjectPath = hostProjectPath,
            Checks = checks
        };
    }

    private async Task<ResolvedHostProject> ResolveHostProjectAsync(string solutionOrProjectPath, string? hostProjectName, CancellationToken ct)
    {
        var isSolution = solutionOrProjectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || solutionOrProjectPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        if (!isSolution)
        {
            return new ResolvedHostProject(
                Name: Path.GetFileNameWithoutExtension(solutionOrProjectPath),
                ProjectPath: solutionOrProjectPath);
        }

        var solution = await _solutionLoader.LoadSolutionAsync(solutionOrProjectPath, ct).ConfigureAwait(false);
        var blazorProjects = SolutionLoader.FindBlazorProjects(solution);

        if (!string.IsNullOrWhiteSpace(hostProjectName))
        {
            var explicitHost = blazorProjects.FirstOrDefault(project =>
                project.Name.Equals(hostProjectName, StringComparison.OrdinalIgnoreCase));

            if (explicitHost is not null)
            {
                return new ResolvedHostProject(
                    explicitHost.Name,
                    explicitHost.FilePath ?? throw new InvalidOperationException($"Could not determine the host project file for '{explicitHost.Name}'."));
            }

            throw new InvalidOperationException(
                $"Could not find a Blazor host project named '{hostProjectName}'. Found: {string.Join(", ", blazorProjects.Select(project => project.Name))}");
        }

        if (blazorProjects.Count == 1)
        {
            return new ResolvedHostProject(
                blazorProjects[0].Name,
                blazorProjects[0].FilePath ?? throw new InvalidOperationException($"Could not determine the host project file for '{blazorProjects[0].Name}'."));
        }

        if (blazorProjects.Count > 1)
        {
            var inferredHost = blazorProjects.FirstOrDefault(project =>
                project.Name.EndsWith(".Demo", StringComparison.OrdinalIgnoreCase) ||
                project.Name.EndsWith(".Web", StringComparison.OrdinalIgnoreCase) ||
                project.Name.EndsWith(".Server", StringComparison.OrdinalIgnoreCase)) ?? blazorProjects[0];

            return new ResolvedHostProject(
                inferredHost.Name,
                inferredHost.FilePath ?? throw new InvalidOperationException($"Could not determine the host project file for '{inferredHost.Name}'."));
        }

        throw new InvalidOperationException("No Blazor host project could be detected in the supplied solution.");
    }

    private static InstallReadinessCheck BuildPackageCheck(string projectPath, string csprojText)
    {
        var hasAgentBlazorReference = HasProjectOrPackageReference(csprojText, "AgentBlazor");
        var hasMudBlazorReference = HasProjectOrPackageReference(csprojText, "MudBlazor");

        if (hasAgentBlazorReference && hasMudBlazorReference)
        {
            return Pass(
                "package-references",
                "Package references",
                $"Found AgentBlazor and MudBlazor references in {Path.GetFileName(projectPath)}.",
                projectPath);
        }

        if (hasAgentBlazorReference)
        {
            return Warning(
                "package-references",
                "Package references",
                "Found AgentBlazor, but MudBlazor is missing from the host project.",
                projectPath,
                "Add the MudBlazor package before scaffolding shell providers.");
        }

        return Missing(
            "package-references",
            "Package references",
            "The host project does not reference AgentBlazor yet.",
            projectPath,
            "Add the AgentBlazor package before wiring services and components.");
    }

    private static InstallReadinessCheck BuildMudServiceCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents)
    {
        var match = FindFirstContaining(csharpContents, "AddMudServices(");
        return match is null
            ? Missing(
                "mud-services",
                "MudBlazor service registration",
                "Could not find builder.Services.AddMudServices().",
                null,
                "Add builder.Services.AddMudServices() in the host startup path.")
            : Pass(
                "mud-services",
                "MudBlazor service registration",
                $"Found AddMudServices() in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildAddAgentBlazorCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents)
    {
        var match = FindFirstContaining(csharpContents, "AddAgentBlazor(");
        return match is null
            ? Missing(
                "agentblazor-services",
                "AgentBlazor service registration",
                "Could not find builder.Services.AddAgentBlazor(...).",
                null,
                "Add builder.Services.AddAgentBlazor(...) in the host startup path.")
            : Pass(
                "agentblazor-services",
                "AgentBlazor service registration",
                $"Found AddAgentBlazor(...) in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildWorkflowCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents)
    {
        var match = FindFirstContaining(csharpContents, "AddWorkflow<") ?? FindFirstContaining(csharpContents, ".AddWorkflow(");
        return match is null
            ? Missing(
                "workflow-registration",
                "Workflow registration",
                "Could not find AddWorkflow<T>() registration.",
                null,
                "Register at least one workflow inside options.ConfigureBuilder(...).")
            : Pass(
                "workflow-registration",
                "Workflow registration",
                $"Found workflow registration in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildEndpointCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents)
    {
        var match = FindFirstContaining(csharpContents, "MapAgentBlazorEndpoints(");
        return match is null
            ? Missing(
                "endpoint-mapping",
                "Endpoint mapping",
                "Could not find app.MapAgentBlazorEndpoints().",
                null,
                "Map AgentBlazor endpoints after the Razor components route setup.")
            : Pass(
                "endpoint-mapping",
                "Endpoint mapping",
                $"Found MapAgentBlazorEndpoints() in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildShellAssetsCheck(IReadOnlyList<ProjectFileSnapshot> razorContents)
    {
        var cssMatch = FindFirstContaining(razorContents, "AgentBlazorAssetPaths.Css");
        var jsMatch = FindFirstContaining(razorContents, "AgentBlazorAssetPaths.Js");

        if (cssMatch is not null && jsMatch is not null)
        {
            return Pass(
                "shell-assets",
                "Shell assets",
                "Found AgentBlazor CSS and JS asset references.",
                cssMatch.Path);
        }

        return Warning(
            "shell-assets",
            "Shell assets",
            "Could not confirm both AgentBlazor CSS and JS asset references in the app shell.",
            cssMatch?.Path ?? jsMatch?.Path,
            "Add AgentBlazor CSS and JS asset references to App.razor or the equivalent host shell.");
    }

    private static InstallReadinessCheck BuildMudProvidersCheck(IReadOnlyList<ProjectFileSnapshot> razorContents)
    {
        var requiredTokens = new[]
        {
            "<MudThemeProvider",
            "<MudPopoverProvider",
            "<MudDialogProvider",
            "<MudSnackbarProvider"
        };

        var presentCount = requiredTokens.Count(token => FindFirstContaining(razorContents, token) is not null);
        if (presentCount == requiredTokens.Length)
        {
            var match = FindFirstContaining(razorContents, "<MudThemeProvider");
            return Pass(
                "mud-providers",
                "MudBlazor providers",
                "Found the standard MudBlazor provider set.",
                match?.Path);
        }

        return Warning(
            "mud-providers",
            "MudBlazor providers",
            $"Found {presentCount} of {requiredTokens.Length} expected MudBlazor providers.",
            null,
            "Ensure the main layout includes the Mud theme, popover, dialog, and snackbar providers.");
    }

    private static InstallReadinessCheck BuildChatSurfaceCheck(IReadOnlyList<ProjectFileSnapshot> razorContents)
    {
        var match = FindFirstContaining(razorContents, "<AgentChatWidget")
            ?? FindFirstContaining(razorContents, "<AgentChatSurface")
            ?? FindFirstContaining(razorContents, "<AgentChatPanel");

        return match is null
            ? Warning(
                "chat-surface",
                "Chat surface",
                "Could not find an AgentBlazor chat surface in the host project.",
                null,
                "Add AgentChatWidget, AgentChatSurface, or AgentChatPanel to a layout or page.")
            : Pass(
                "chat-surface",
                "Chat surface",
                $"Found a chat surface in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static async Task<IReadOnlyList<ProjectFileSnapshot>> ReadProjectFilesAsync(
        string projectDirectory,
        string extension,
        CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(projectDirectory, $"*{extension}", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var snapshots = new List<ProjectFileSnapshot>(files.Count);
        foreach (var file in files)
        {
            snapshots.Add(new ProjectFileSnapshot(file, await File.ReadAllTextAsync(file, ct).ConfigureAwait(false)));
        }

        return snapshots;
    }

    private static bool HasProjectOrPackageReference(string csprojText, string referenceName)
    {
        try
        {
            var document = XDocument.Parse(csprojText);
            return document.Descendants()
                .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
                .Any(element =>
                {
                    var include = element.Attribute("Include")?.Value ?? string.Empty;
                    return include.Contains(referenceName, StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            return csprojText.Contains(referenceName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ProjectFileSnapshot? FindFirstContaining(IEnumerable<ProjectFileSnapshot> files, string token) =>
        files.FirstOrDefault(file => file.Content.Contains(token, StringComparison.Ordinal));

    private static InstallReadinessCheck Pass(string id, string title, string message, string? filePath) =>
        new()
        {
            Id = id,
            Title = title,
            Status = InstallReadinessStatus.Pass,
            Message = message,
            FilePath = filePath
        };

    private static InstallReadinessCheck Warning(string id, string title, string message, string? filePath, string? suggestedFix) =>
        new()
        {
            Id = id,
            Title = title,
            Status = InstallReadinessStatus.Warning,
            Message = message,
            FilePath = filePath,
            SuggestedFix = suggestedFix
        };

    private static InstallReadinessCheck Missing(string id, string title, string message, string? filePath, string? suggestedFix) =>
        new()
        {
            Id = id,
            Title = title,
            Status = InstallReadinessStatus.Missing,
            Message = message,
            FilePath = filePath,
            SuggestedFix = suggestedFix
        };

    private sealed record ProjectFileSnapshot(string Path, string Content);

    private sealed record ResolvedHostProject(string Name, string ProjectPath);
}
