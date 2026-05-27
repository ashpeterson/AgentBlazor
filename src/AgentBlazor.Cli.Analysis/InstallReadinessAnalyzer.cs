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
        var hostTargetFrameworks = await TargetFrameworkSupport.ReadTargetFrameworksAsync(hostProjectPath, ct).ConfigureAwait(false);
        var csharpContents = await ReadProjectFilesAsync(hostProjectDirectory, ".cs", ct).ConfigureAwait(false);
        var hostShape = AnalyzeHostShape(hostProjectPath, hostProjectDirectory, csprojText, csharpContents);
        var uiProject = await ResolveUiProjectAsync(hostProjectPath, hostProject.Name, csprojText, hostShape, ct).ConfigureAwait(false);
        var uiProjectPath = uiProject?.ProjectPath;
        var uiTargetFrameworks = uiProjectPath is null
            ? Array.Empty<string>()
            : (await TargetFrameworkSupport.ReadTargetFrameworksAsync(uiProjectPath, ct).ConfigureAwait(false)).ToArray();
        var uiProjectDirectory = Path.GetDirectoryName(uiProjectPath ?? hostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the UI project directory for '{uiProjectPath ?? hostProjectPath}'.");
        var shellProjectDirectory = hostShape.Family == HostFamily.HostedWebAssembly
            ? uiProjectDirectory
            : hostProjectDirectory;
        var razorContents = await ReadProjectFilesAsync(uiProjectDirectory, ".razor", ct).ConfigureAwait(false);
        var shellRazorContents = string.Equals(shellProjectDirectory, uiProjectDirectory, StringComparison.OrdinalIgnoreCase)
            ? razorContents
            : await ReadProjectFilesAsync(shellProjectDirectory, ".razor", ct).ConfigureAwait(false);
        var shellCshtmlContents = await ReadProjectFilesAsync(shellProjectDirectory, ".cshtml", ct).ConfigureAwait(false);
        var shellHtmlContents = await ReadProjectFilesAsync(shellProjectDirectory, ".html", ct).ConfigureAwait(false);
        var shellMarkupContents = shellRazorContents.Concat(shellCshtmlContents).Concat(shellHtmlContents).ToArray();
        var startupPath = ResolveStartupPath(hostProjectDirectory, hostShape.Family);
        var shellPath = ResolveShellPath(shellProjectDirectory, hostShape.Family);
        var layoutPath = ResolveLayoutPath(uiProjectDirectory);
        var chatSurfacePath = ResolveChatSurfacePath(uiProjectDirectory);

        var checks = new List<InstallReadinessCheck>
        {
            BuildTargetFrameworkSupportCheck(hostProjectPath, hostTargetFrameworks, uiProjectPath, uiTargetFrameworks),
            BuildHostShapeCheck(hostShape),
            BuildPackageCheck(hostProjectPath, csprojText),
            BuildMudServiceCheck(csharpContents, startupPath, hostShape.Family),
            BuildAddAgentBlazorCheck(csharpContents, startupPath, hostShape.Family),
            BuildWorkflowCheck(csharpContents, startupPath, hostShape.Family),
            BuildEndpointCheck(csharpContents, startupPath, hostShape.Family),
            BuildShellAssetsCheck(shellMarkupContents, shellPath),
            BuildMudProvidersCheck(razorContents, layoutPath),
            BuildChatSurfaceCheck(razorContents, chatSurfacePath)
        };

        return new InstallReadinessReport
        {
            InputPath = solutionOrProjectPath,
            HostProjectName = hostProject.Name,
            HostProjectPath = hostProjectPath,
            UiProjectName = uiProject?.Name,
            UiProjectPath = uiProjectPath,
            HostShape = hostShape,
            Checks = checks
        };
    }

    private static HostShapeAssessment AnalyzeHostShape(
        string hostProjectPath,
        string hostProjectDirectory,
        string csprojText,
        IReadOnlyList<ProjectFileSnapshot> csharpContents)
    {
        var reasons = new List<string>();
        string? evidencePath = hostProjectPath;
        var hasOqtaneSignals = false;
        var hasLegacyServerSignals = false;
        var hasHostedWebAssemblySignals = false;

        if (ContainsToken(csprojText, "Oqtane") || csharpContents.Any(file => ContainsToken(file.Content, "Oqtane")))
        {
            hasOqtaneSignals = true;
            reasons.Add("Detected Oqtane-specific references.");
            evidencePath = csharpContents.FirstOrDefault(file => ContainsToken(file.Content, "Oqtane"))?.Path ?? hostProjectPath;
        }

        var startupPath = Path.Combine(hostProjectDirectory, "Startup.cs");
        if (File.Exists(startupPath))
        {
            hasLegacyServerSignals = true;
            reasons.Add("Detected Startup.cs in the host project.");
            evidencePath = startupPath;
        }

        var programPath = Path.Combine(hostProjectDirectory, "Program.cs");
        if (!File.Exists(programPath))
        {
            reasons.Add("Could not find Program.cs in the host project root.");
        }

        var hasAddRazorComponents = csharpContents.Any(file => file.Content.Contains("AddRazorComponents(", StringComparison.Ordinal));
        var hasMapRazorComponents = csharpContents.Any(file => file.Content.Contains("MapRazorComponents<", StringComparison.Ordinal));
        var hasAddServerSideBlazor = csharpContents.Any(file => file.Content.Contains("AddServerSideBlazor(", StringComparison.Ordinal));
        var hasMapBlazorHub = csharpContents.Any(file => file.Content.Contains("MapBlazorHub(", StringComparison.Ordinal));
        var hasAddRazorPages = csharpContents.Any(file => file.Content.Contains("AddRazorPages(", StringComparison.Ordinal));
        var hasMapFallbackToHost = csharpContents.Any(file => file.Content.Contains("MapFallbackToPage(\"/_Host\")", StringComparison.Ordinal));
        var hostedWasmSignalFile = csharpContents.FirstOrDefault(file =>
            file.Content.Contains("UseBlazorFrameworkFiles(", StringComparison.Ordinal) ||
            file.Content.Contains("MapFallbackToFile(\"index.html\")", StringComparison.Ordinal));
        if (hostedWasmSignalFile is not null)
        {
            hasHostedWebAssemblySignals = true;
            reasons.Add("Detected hosted WebAssembly server routing such as UseBlazorFrameworkFiles()/MapFallbackToFile(\"index.html\").");
            evidencePath = hostedWasmSignalFile.Path;
        }

        if (hasAddServerSideBlazor || hasMapBlazorHub || hasAddRazorPages || hasMapFallbackToHost)
        {
            hasLegacyServerSignals = true;
        }

        if (!hasAddRazorComponents || !hasMapRazorComponents)
        {
            reasons.Add("Could not confirm the standard AddRazorComponents()/MapRazorComponents<App>() startup pattern.");
        }

        var hostPagePath = Path.Combine(hostProjectDirectory, "Pages", "_Host.cshtml");
        if (File.Exists(hostPagePath))
        {
            hasLegacyServerSignals = true;
            reasons.Add("Detected legacy _Host.cshtml host pages.");
            evidencePath = hostPagePath;
        }

        if (reasons.Count == 0)
        {
            return new HostShapeAssessment
            {
                Kind = HostShapeKind.Standard,
                Family = HostFamily.StandardWebApp,
                Title = "Standard Blazor Web App",
                Message = "Detected a standard Program.cs-based Blazor Web App host shape for scaffold.",
                FilePath = programPath
            };
        }

        if (hasOqtaneSignals)
        {
            return new HostShapeAssessment
            {
                Kind = HostShapeKind.AdvancedReview,
                Family = HostFamily.OqtaneStyle,
                Title = "Oqtane-style Blazor host",
                Message = $"Detected an advanced Blazor host with Oqtane-style signals. Scaffold will keep safe file additions in preview/apply and downgrade startup, shell, layout, and route wiring to manual review. {string.Join(" ", reasons)}",
                FilePath = evidencePath,
                SuggestedFix = "Run scaffold preview to review the safe edits and the manual-review items, then patch the host-specific startup and shell files manually."
            };
        }

        if (hasHostedWebAssemblySignals)
        {
            return new HostShapeAssessment
            {
                Kind = HostShapeKind.AdvancedReview,
                Family = HostFamily.HostedWebAssembly,
                Title = "Hosted Blazor WebAssembly",
                Message = $"Detected a hosted WebAssembly-style Blazor server host. Scaffold can patch the standard server startup path and create server-side AgentBlazor workflow wiring, but browser-client layout, provider, asset, and chat edits remain review-first until a browser-safe or remote/server-backed WebAssembly client chat path is selected. {string.Join(" ", reasons)}",
                FilePath = evidencePath,
                SuggestedFix = "Run scaffold preview to review the safe server edits and the explicit client manual-review items, then apply the server path and complete the WebAssembly client integration manually."
            };
        }

        if (hasLegacyServerSignals)
        {
            return new HostShapeAssessment
            {
                Kind = HostShapeKind.AdvancedReview,
                Family = HostFamily.LegacyServer,
                Title = "Legacy Blazor Server host",
                Message = $"Detected a legacy or custom Blazor host that does not match the standard Program.cs-based Blazor Web App pattern. Scaffold will keep safe file additions in preview/apply and downgrade startup, shell, layout, and route wiring to manual review. {string.Join(" ", reasons)}",
                FilePath = evidencePath,
                SuggestedFix = "Run scaffold preview to review the safe edits and the manual-review items, then patch the legacy startup and shell files manually."
            };
        }

        return new HostShapeAssessment
        {
            Kind = HostShapeKind.Unsupported,
            Family = HostFamily.Unknown,
            Title = "Unsupported Blazor host",
            Message = $"Scaffold preview/apply could not classify this host into a supported Blazor scaffold path. {string.Join(" ", reasons)}",
            FilePath = evidencePath,
            SuggestedFix = "Run `agentblazor doctor` for a full gap report, then patch this host manually until advanced-host scaffold support is added."
        };
    }

    private static InstallReadinessCheck BuildHostShapeCheck(HostShapeAssessment hostShape) =>
        hostShape.Kind switch
        {
            HostShapeKind.Standard => Pass(
                "host-shape",
                hostShape.Title,
                hostShape.Message,
                hostShape.FilePath),
            HostShapeKind.AdvancedReview => Warning(
                "host-shape",
                hostShape.Title,
                hostShape.Message,
                hostShape.FilePath,
                hostShape.SuggestedFix),
            HostShapeKind.Unsupported => Warning(
                "host-shape",
                hostShape.Title,
                hostShape.Message,
                hostShape.FilePath,
                hostShape.SuggestedFix),
            _ => throw new ArgumentOutOfRangeException(nameof(hostShape.Kind), hostShape.Kind, null)
        };

    private static async Task<ResolvedProject?> ResolveUiProjectAsync(
        string hostProjectPath,
        string hostProjectName,
        string hostCsprojText,
        HostShapeAssessment hostShape,
        CancellationToken ct)
    {
        if (hostShape.Family is not (HostFamily.HostedWebAssembly or HostFamily.StandardWebApp))
        {
            return null;
        }

        foreach (var referencedProjectPath in EnumerateProjectReferences(hostProjectPath, hostCsprojText))
        {
            if (!File.Exists(referencedProjectPath))
            {
                continue;
            }

            var referencedProjectText = await File.ReadAllTextAsync(referencedProjectPath, ct).ConfigureAwait(false);
            if (!LooksLikeHostedWebAssemblyClient(referencedProjectPath, referencedProjectText))
            {
                continue;
            }

            return new ResolvedProject(
                Name: Path.GetFileNameWithoutExtension(referencedProjectPath),
                ProjectPath: referencedProjectPath);
        }

        foreach (var inferredProjectPath in EnumerateHostedClientCandidates(hostProjectPath, hostProjectName))
        {
            if (!File.Exists(inferredProjectPath))
            {
                continue;
            }

            var referencedProjectText = await File.ReadAllTextAsync(inferredProjectPath, ct).ConfigureAwait(false);
            if (!LooksLikeHostedWebAssemblyClient(inferredProjectPath, referencedProjectText))
            {
                continue;
            }

            return new ResolvedProject(
                Name: Path.GetFileNameWithoutExtension(inferredProjectPath),
                ProjectPath: inferredProjectPath);
        }

        return null;
    }

    private async Task<ResolvedProject> ResolveHostProjectAsync(string solutionOrProjectPath, string? hostProjectName, CancellationToken ct)
    {
        var isSolution = solutionOrProjectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || solutionOrProjectPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        if (!isSolution)
        {
            return new ResolvedProject(
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
                return new ResolvedProject(
                    explicitHost.Name,
                    explicitHost.FilePath ?? throw new InvalidOperationException($"Could not determine the host project file for '{explicitHost.Name}'."));
            }

            throw new InvalidOperationException(
                $"Could not find a Blazor host project named '{hostProjectName}'. Found: {string.Join(", ", blazorProjects.Select(project => project.Name))}");
        }

        if (blazorProjects.Count == 1)
        {
            return new ResolvedProject(
                blazorProjects[0].Name,
                blazorProjects[0].FilePath ?? throw new InvalidOperationException($"Could not determine the host project file for '{blazorProjects[0].Name}'."));
        }

        if (blazorProjects.Count > 1)
        {
            var inferredHost = blazorProjects.FirstOrDefault(project =>
                project.Name.EndsWith(".Demo", StringComparison.OrdinalIgnoreCase) ||
                project.Name.EndsWith(".Web", StringComparison.OrdinalIgnoreCase) ||
                project.Name.EndsWith(".Server", StringComparison.OrdinalIgnoreCase)) ?? blazorProjects[0];

            return new ResolvedProject(
                inferredHost.Name,
                inferredHost.FilePath ?? throw new InvalidOperationException($"Could not determine the host project file for '{inferredHost.Name}'."));
        }

        throw new InvalidOperationException("No Blazor host project could be detected in the supplied solution.");
    }

    private static InstallReadinessCheck BuildTargetFrameworkSupportCheck(
        string hostProjectPath,
        IReadOnlyList<string> hostTargetFrameworks,
        string? uiProjectPath,
        IReadOnlyList<string> uiTargetFrameworks)
    {
        var unsupportedHostFrameworks = hostTargetFrameworks
            .Where(targetFramework => !TargetFrameworkSupport.IsSupported(targetFramework))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unsupportedUiFrameworks = uiTargetFrameworks
            .Where(targetFramework => !TargetFrameworkSupport.IsSupported(targetFramework))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hostTargetFrameworks.Count == 0)
        {
            return Missing(
                "target-framework-support",
                "Target framework support",
                "Could not determine the host project's target framework.",
                hostProjectPath,
                $"Retarget the app to {TargetFrameworkSupport.DescribeSupportRange()} before installing AgentBlazor.");
        }

        if (unsupportedHostFrameworks.Length > 0)
        {
            return Missing(
                "target-framework-support",
                "Target framework support",
                $"The host project targets unsupported framework(s): {string.Join(", ", unsupportedHostFrameworks)}. AgentBlazor currently supports {TargetFrameworkSupport.DescribeSupportRange()}.",
                hostProjectPath,
                $"Retarget the host project to {TargetFrameworkSupport.DescribeSupportRange()} before running scaffold or install.");
        }

        if (uiProjectPath is not null && uiTargetFrameworks.Count == 0)
        {
            return Missing(
                "target-framework-support",
                "Target framework support",
                "Could not determine the hosted WebAssembly client project's target framework.",
                uiProjectPath,
                $"Retarget the client project to {TargetFrameworkSupport.DescribeSupportRange()} before installing AgentBlazor.");
        }

        if (unsupportedUiFrameworks.Length > 0)
        {
            return Missing(
                "target-framework-support",
                "Target framework support",
                $"The hosted WebAssembly client project targets unsupported framework(s): {string.Join(", ", unsupportedUiFrameworks)}. AgentBlazor currently supports {TargetFrameworkSupport.DescribeSupportRange()}.",
                uiProjectPath,
                $"Retarget the client project to {TargetFrameworkSupport.DescribeSupportRange()} before running scaffold or install.");
        }

        var message = uiProjectPath is null
            ? $"The host project targets supported framework(s): {string.Join(", ", hostTargetFrameworks)}."
            : $"The host and UI projects target supported framework(s): host {string.Join(", ", hostTargetFrameworks)}; UI {string.Join(", ", uiTargetFrameworks)}.";

        return Pass(
            "target-framework-support",
            "Target framework support",
            message,
            uiProjectPath ?? hostProjectPath);
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

    private static InstallReadinessCheck BuildMudServiceCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents, string? startupPath, HostFamily family)
    {
        var match = FindFirstContaining(csharpContents, "AddMudServices(");
        return match is null
            ? Missing(
                "mud-services",
                "MudBlazor service registration",
                "Could not find builder.Services.AddMudServices().",
                startupPath,
                GetMissingFix("mud-services", family))
            : Pass(
                "mud-services",
                "MudBlazor service registration",
                $"Found AddMudServices() in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildAddAgentBlazorCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents, string? startupPath, HostFamily family)
    {
        var match = FindFirstContaining(csharpContents, "AddAgentBlazor(");
        return match is null
            ? Missing(
                "agentblazor-services",
                "AgentBlazor service registration",
                "Could not find builder.Services.AddAgentBlazor(...).",
                startupPath,
                GetMissingFix("agentblazor-services", family))
            : Pass(
                "agentblazor-services",
                "AgentBlazor service registration",
                $"Found AddAgentBlazor(...) in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildWorkflowCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents, string? startupPath, HostFamily family)
    {
        var match = FindFirstContaining(csharpContents, "AddWorkflow<") ?? FindFirstContaining(csharpContents, ".AddWorkflow(");
        return match is null
            ? Missing(
                "workflow-registration",
                "Workflow registration",
                "Could not find AddWorkflow<T>() registration.",
                startupPath,
                GetMissingFix("workflow-registration", family))
            : Pass(
                "workflow-registration",
                "Workflow registration",
                $"Found workflow registration in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildEndpointCheck(IReadOnlyList<ProjectFileSnapshot> csharpContents, string? startupPath, HostFamily family)
    {
        var match = FindFirstContaining(csharpContents, "MapAgentBlazorEndpoints(");
        return match is null
            ? Missing(
                "endpoint-mapping",
                "Endpoint mapping",
                "Could not find app.MapAgentBlazorEndpoints().",
                startupPath,
                GetMissingFix("endpoint-mapping", family))
            : Pass(
                "endpoint-mapping",
                "Endpoint mapping",
                $"Found MapAgentBlazorEndpoints() in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static InstallReadinessCheck BuildShellAssetsCheck(IReadOnlyList<ProjectFileSnapshot> markupContents, string? shellPath)
    {
        var mudCssMatch = FindFirstContaining(markupContents, "_content/MudBlazor/MudBlazor.min.css");
        var agentCssMatch = FindFirstContaining(markupContents, "AgentBlazorAssetPaths.Css")
            ?? FindFirstContaining(markupContents, "_content/AgentBlazor/AgentBlazor.min.css");
        var mudJsMatch = FindFirstContaining(markupContents, "_content/MudBlazor/MudBlazor.min.js");
        var agentJsMatch = FindFirstContaining(markupContents, "AgentBlazorAssetPaths.Js")
            ?? FindFirstContaining(markupContents, "_content/AgentBlazor/AgentBlazor.min.js");

        if (mudCssMatch is not null && agentCssMatch is not null && mudJsMatch is not null && agentJsMatch is not null)
        {
            return Pass(
                "shell-assets",
                "Shell assets",
                "Found MudBlazor and AgentBlazor CSS/JS asset references in the host shell.",
                agentCssMatch.Path);
        }

        var presentCount = new[] { mudCssMatch, agentCssMatch, mudJsMatch, agentJsMatch }.Count(match => match is not null);

        return Warning(
            "shell-assets",
            "Shell assets",
            $"Found {presentCount} of 4 expected MudBlazor and AgentBlazor shell asset references.",
            mudCssMatch?.Path ?? agentCssMatch?.Path ?? mudJsMatch?.Path ?? agentJsMatch?.Path ?? shellPath,
            "Add MudBlazor and AgentBlazor CSS/JS asset references to App.razor, Pages/_Host.cshtml, or the equivalent host shell.");
    }

    private static InstallReadinessCheck BuildMudProvidersCheck(IReadOnlyList<ProjectFileSnapshot> razorContents, string? layoutPath)
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
            layoutPath,
            "Ensure the main layout includes the Mud theme, popover, dialog, and snackbar providers.");
    }

    private static InstallReadinessCheck BuildChatSurfaceCheck(IReadOnlyList<ProjectFileSnapshot> razorContents, string? chatSurfacePath)
    {
        var match = FindFirstContaining(razorContents, "<AgentChatWidget")
            ?? FindFirstContaining(razorContents, "<AgentChatSurface")
            ?? FindFirstContaining(razorContents, "<AgentChatPanel");

        return match is null
            ? Warning(
                "chat-surface",
                "Chat surface",
                "Could not find an AgentBlazor chat surface in the host project.",
                chatSurfacePath,
                "Add AgentChatWidget, AgentChatSurface, or AgentChatPanel to a layout or page.")
            : Pass(
                "chat-surface",
                "Chat surface",
                $"Found a chat surface in {Path.GetFileName(match.Path)}.",
                match.Path);
    }

    private static string? ResolveStartupPath(string projectDirectory, HostFamily family)
        => family switch
        {
            HostFamily.LegacyServer => ResolveFirstExistingPath(projectDirectory, "Startup.cs", "Program.cs"),
            _ => ResolveFirstExistingPath(projectDirectory, "Program.cs", "Startup.cs")
        };

    private static string? ResolveShellPath(string projectDirectory, HostFamily family)
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
                Path.Combine("Components", "App.razor"),
                "App.razor"),
            _ => ResolveFirstExistingPath(
                projectDirectory,
                Path.Combine("Components", "App.razor"),
                "App.razor",
                Path.Combine("Pages", "_Host.cshtml"))
        };

    private static string? ResolveLayoutPath(string projectDirectory)
        => ResolveFirstExistingPath(
            projectDirectory,
            Path.Combine("Shared", "MainLayout.razor"),
            Path.Combine("Layout", "MainLayout.razor"),
            Path.Combine("Components", "Layout", "MainLayout.razor"),
            Path.Combine("Pages", "MainLayout.razor"));

    private static string? ResolveChatSurfacePath(string projectDirectory)
    {
        var knownPagePaths = new[]
        {
            Path.Combine("Pages", "Index.razor"),
            Path.Combine("Pages", "Home.razor"),
            "App.razor",
            Path.Combine("Components", "Pages", "Home.razor"),
            Path.Combine("Components", "Pages", "Index.razor"),
            Path.Combine("Shared", "Index.razor")
        };

        return ResolveFirstExistingPathMatching(projectDirectory, IsRootRazorPage, knownPagePaths)
            ?? ResolveFirstRootRazorPagePath(projectDirectory)
            ?? ResolveFirstExistingPath(projectDirectory, knownPagePaths);
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

    private static bool ContainsToken(string content, string token)
        => content.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string GetMissingFix(string checkId, HostFamily family)
        => (family, checkId) switch
        {
            (HostFamily.HostedWebAssembly, "mud-services") =>
                "Add builder.Services.AddMudServices() in the hosted WebAssembly server Program.cs path, alongside AddRazorPages() and the static-file pipeline.",
            (HostFamily.HostedWebAssembly, "agentblazor-services") =>
                "Add builder.Services.AddAgentBlazor(...) in the hosted WebAssembly server Program.cs path, not in the client WebAssembly bootstrap.",
            (HostFamily.HostedWebAssembly, "workflow-registration") =>
                "Register at least one workflow inside the hosted WebAssembly server AddAgentBlazor(...).ConfigureBuilder(...) path.",
            (HostFamily.HostedWebAssembly, "endpoint-mapping") =>
                "Map AgentBlazor endpoints before MapFallbackToFile(\"index.html\") in the hosted WebAssembly server pipeline.",
            (_, "mud-services") =>
                "Add builder.Services.AddMudServices() in the host startup path.",
            (_, "agentblazor-services") =>
                "Add builder.Services.AddAgentBlazor(...) in the host startup path.",
            (_, "workflow-registration") =>
                "Register at least one workflow inside options.ConfigureBuilder(...).",
            (_, "endpoint-mapping") =>
                "Map AgentBlazor endpoints after the Razor components route setup.",
            _ => "Review and complete the missing AgentBlazor install step."
        };

    private static IEnumerable<string> EnumerateProjectReferences(string hostProjectPath, string csprojText)
    {
        try
        {
            var document = XDocument.Parse(csprojText);
            var projectDirectory = Path.GetDirectoryName(hostProjectPath)
                ?? throw new InvalidOperationException($"Could not determine the project directory for '{hostProjectPath}'.");

            return document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar))
                .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateHostedClientCandidates(string hostProjectPath, string hostProjectName)
    {
        var projectDirectory = Path.GetDirectoryName(hostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the project directory for '{hostProjectPath}'.");
        var solutionDirectory = Directory.GetParent(projectDirectory)?.FullName ?? projectDirectory;
        var candidates = new List<string>();

        if (hostProjectName.EndsWith(".Server", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = hostProjectName[..^".Server".Length];
            candidates.Add(Path.Combine(solutionDirectory, $"{baseName}.Client", $"{baseName}.Client.csproj"));
            candidates.Add(Path.Combine(solutionDirectory, "Client", "Client.csproj"));
        }

        candidates.Add(Path.Combine(solutionDirectory, $"{hostProjectName}.Client", $"{hostProjectName}.Client.csproj"));
        candidates.Add(Path.Combine(solutionDirectory, "Client", "Client.csproj"));

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHostedWebAssemblyClient(string projectPath, string projectText)
    {
        if (projectText.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (projectText.Contains("Microsoft.AspNetCore.Components.WebAssembly", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine the project directory for '{projectPath}'.");
        return File.Exists(Path.Combine(projectDirectory, "wwwroot", "index.html"));
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

    private sealed record ResolvedProject(string Name, string ProjectPath);
}
