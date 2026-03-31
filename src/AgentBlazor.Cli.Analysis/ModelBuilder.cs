using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Orchestrates the analysis pipeline to build a complete ProjectModel.
/// </summary>
public sealed class ModelBuilder
{
    private readonly SolutionLoader _solutionLoader = new();
    private readonly RazorAnalyzer _razorAnalyzer = new();
    private readonly ServiceAnalyzer _serviceAnalyzer = new();
    private readonly ActionScorer _actionScorer = new();
    private readonly PageActionLinker _pageLinker = new();
    private readonly AttributeAnalyzer _attributeAnalyzer = new();
    private readonly DiRegistrationAnalyzer _diAnalyzer = new();
    private readonly RecommendationEngine _recommendationEngine = new();

    public event Action<string>? OnProgress;
    public event Action<string>? OnWarning;

    public async Task<ProjectModel> BuildModelAsync(
        string solutionOrProjectPath,
        string? hostProjectName,
        string appDescription,
        AgentBlazorConfig? config = null,
        CancellationToken ct = default)
    {
        // Apply config to analyzers
        if (config != null)
        {
            _actionScorer.Configure(config);
            _serviceAnalyzer.Configure(config);
        }
        // Determine if this is a solution or project
        var isSolution = solutionOrProjectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                        solutionOrProjectPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        Solution? solution = null;
        Project? hostProject = null;
        IReadOnlyList<Project> projects = [];

        if (isSolution)
        {
            OnProgress?.Invoke("Loading solution...");
            solution = await _solutionLoader.LoadSolutionAsync(solutionOrProjectPath, ct);
            ReportDiagnostics(_solutionLoader.Diagnostics);
            projects = solution.Projects.ToList();

            // Find the Blazor host project
            var blazorProjects = SolutionLoader.FindBlazorProjects(solution);

            if (!string.IsNullOrEmpty(hostProjectName))
            {
                hostProject = blazorProjects.FirstOrDefault(p =>
                    p.Name.Equals(hostProjectName, StringComparison.OrdinalIgnoreCase));
            }
            else if (blazorProjects.Count == 1)
            {
                hostProject = blazorProjects[0];
            }
            else if (blazorProjects.Count > 1)
            {
                // Try to find the "main" one (often has .Demo, .Web, or .Server suffix)
                hostProject = blazorProjects.FirstOrDefault(p =>
                    p.Name.EndsWith(".Demo", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.EndsWith(".Web", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.EndsWith(".Server", StringComparison.OrdinalIgnoreCase)) ??
                    blazorProjects[0];
            }
        }
        else
        {
            OnProgress?.Invoke("Loading project...");
            hostProject = await _solutionLoader.LoadProjectAsync(solutionOrProjectPath, ct);
            ReportDiagnostics(_solutionLoader.Diagnostics);
            projects = [hostProject];
        }

        if (hostProject == null)
        {
            var blazorProjects = isSolution ? SolutionLoader.FindBlazorProjects(solution!) : [];
            var errorMessage = BuildHostProjectErrorMessage(
                solutionOrProjectPath,
                hostProjectName,
                projects.Count,
                blazorProjects.Count,
                blazorProjects.Select(p => p.Name).ToList());
            throw new InvalidOperationException(errorMessage);
        }

        OnProgress?.Invoke($"Analyzing Blazor host: {hostProject.Name}");

        // Get the project directory for Razor analysis
        var projectDir = Path.GetDirectoryName(hostProject.FilePath)!;

        // 1. Analyze Razor files
        OnProgress?.Invoke("Scanning Razor components...");
        var razorAnalyses = await _razorAnalyzer.AnalyzeDirectoryAsync(projectDir, ct);

        // 2. Extract routes
        OnProgress?.Invoke("Extracting routes...");
        var routes = razorAnalyses
            .SelectMany(r => r.Routes)
            .Select(r => new RouteModel
            {
                Id = GenerateRouteId(r.Template),
                Template = r.Template,
                ComponentName = r.ComponentName,
                ComponentFile = MakeRelativePath(projectDir, r.FilePath),
                Parameters = r.Parameters
            })
            .ToList();

        // 3. Build component models
        OnProgress?.Invoke("Building component models...");
        var components = razorAnalyses.Select(r => new ComponentModel
        {
            Id = GenerateId(r.ComponentName),
            Name = r.ComponentName,
            FilePath = MakeRelativePath(projectDir, r.FilePath),
            Namespace = r.Namespace,
            IsPage = r.IsPage,
            Routes = r.Routes.Select(rt => rt.Template).ToList(),
            InjectedServices = r.InjectedServices,
            ChildComponents = r.ChildComponents,
            Layout = r.Layout
        }).ToList();

        // 4. Find [AgentCapability] classes
        OnProgress?.Invoke("Detecting AgentBlazor attributes...");
        var capabilities = await _attributeAnalyzer.FindCapabilityClassesAsync(hostProject, ct);

        // Also check referenced projects
        if (solution != null)
        {
            var referencedProjects = GetReferencedProjects(solution, hostProject);
            foreach (var refProject in referencedProjects)
            {
                var refCapabilities = await _attributeAnalyzer.FindCapabilityClassesAsync(refProject, ct);
                capabilities = capabilities.Concat(refCapabilities).ToList();
            }
        }

        OnProgress?.Invoke($"Found {capabilities.Count} [[AgentCapability]] classes");

        // Build lookup of attribute-marked methods
        var attributeMarkedActions = capabilities
            .SelectMany(c => c.Actions.Select(a => new
            {
                Key = $"{c.ClassName}.{a.MethodName}",
                Action = a,
                Capability = c
            }))
            .ToDictionary(x => x.Key, x => (x.Action, x.Capability));

        // 5. Find DI registrations
        OnProgress?.Invoke("Analyzing DI registrations...");
        var diRegistrations = await _diAnalyzer.FindRegistrationsAsync(hostProject, ct);

        // Also check referenced projects
        if (solution != null)
        {
            var referencedProjects = GetReferencedProjects(solution, hostProject);
            foreach (var refProject in referencedProjects)
            {
                var refDiRegs = await _diAnalyzer.FindRegistrationsAsync(refProject, ct);
                diRegistrations = diRegistrations.Concat(refDiRegs).ToList();
            }
        }

        OnProgress?.Invoke($"Found {diRegistrations.Count} DI registrations");

        // Build set of registered service types for enhanced discovery
        var registeredTypes = diRegistrations
            .Select(r => r.ImplementationType)
            .Concat(diRegistrations.Select(r => r.ServiceType))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 6. Find and analyze services
        OnProgress?.Invoke("Analyzing services...");
        var services = await _serviceAnalyzer.FindServiceClassesAsync(hostProject, ct);

        // Also check referenced projects
        if (solution != null)
        {
            var referencedProjects = GetReferencedProjects(solution, hostProject);
            foreach (var refProject in referencedProjects)
            {
                var refServices = await _serviceAnalyzer.FindServiceClassesAsync(refProject, ct);
                services = services.Concat(refServices).ToList();
            }
        }

        // Enhance services with DI registration info
        services = services.Select(s =>
        {
            var diReg = diRegistrations.FirstOrDefault(r =>
                r.ImplementationType.Equals(s.TypeName, StringComparison.OrdinalIgnoreCase) ||
                r.ImplementationType.EndsWith("." + s.TypeName, StringComparison.OrdinalIgnoreCase));

            if (diReg != null)
            {
                return s with { Lifetime = diReg.Lifetime.ToString() };
            }
            return s;
        }).ToList();

        OnProgress?.Invoke($"Found {services.Count} service classes");

        // 6. Score and create action models
        OnProgress?.Invoke("Scoring candidate actions...");
        var actions = new List<ActionModel>();
        foreach (var service in services)
        {
            foreach (var method in service.Methods)
            {
                var score = _actionScorer.ScoreMethod(method, service.TypeName);
                var action = _actionScorer.ToActionModel(method, service, score);

                // Check if this method has [AgentAction] attribute
                var key = $"{service.TypeName}.{method.Name}";
                if (attributeMarkedActions.TryGetValue(key, out var attrInfo))
                {
                    // Override with attribute information
                    action = action with
                    {
                        Score = 1.0, // Max score for attributed methods
                        ExposureMode = ActionExposureMode.Confirmed, // Attribute = confirmed
                        RequiresApproval = attrInfo.Action.RequiresApproval || action.RequiresApproval,
                        Summary = attrInfo.Action.Description ?? action.Summary,
                        Classification = action.IsMutationLikely
                            ? ActionClassification.Workflow
                            : ActionClassification.Query
                    };
                }

                actions.Add(action);
            }
        }

        // Add any [AgentAction] methods from capabilities not found via service scanning
        foreach (var capability in capabilities)
        {
            foreach (var actionInfo in capability.Actions)
            {
                var key = $"{capability.ClassName}.{actionInfo.MethodName}";
                var exists = actions.Any(a =>
                    a.SourceService == capability.ClassName &&
                    a.MethodName == actionInfo.MethodName);

                if (!exists)
                {
                    actions.Add(new ActionModel
                    {
                        Id = GenerateActionId(capability.ClassName, actionInfo.MethodName),
                        Name = GenerateActionName(actionInfo.MethodName),
                        SourceService = capability.ClassName,
                        MethodName = actionInfo.MethodName,
                        FilePath = MakeRelativePath(projectDir, capability.FilePath),
                        IsMutationLikely = actionInfo.RequiresApproval,
                        RequiresApproval = actionInfo.RequiresApproval,
                        Classification = ActionClassification.Workflow,
                        Score = 1.0,
                        Parameters = [],
                        Summary = actionInfo.Description ?? $"Action from {capability.ClassName}",
                        ExposureMode = ActionExposureMode.Confirmed
                    });
                }
            }
        }

        var suggestedActions = actions.Where(a =>
            a.ExposureMode == ActionExposureMode.Suggested ||
            a.ExposureMode == ActionExposureMode.Confirmed).ToList();
        OnProgress?.Invoke($"Found {suggestedActions.Count} candidate actions (of {actions.Count} total methods)");

        // 7. Link pages to actions
        OnProgress?.Invoke("Linking pages to actions...");
        var pageLinks = await _pageLinker.LinkAllPagesAsync(razorAnalyses, services, actions, ct);

        // Update actions with route links
        actions = _pageLinker.UpdateActionsWithRoutes(actions, pageLinks).ToList();

        // 8. Build page models
        var pages = razorAnalyses
            .Where(r => r.IsPage)
            .Select(r =>
            {
                var route = r.Routes.FirstOrDefault()?.Template ?? "";
                var link = pageLinks.FirstOrDefault(l => l.PageFile == r.FilePath);

                return new PageModel
                {
                    Id = GenerateRouteId(route),
                    Route = route,
                    ComponentName = r.ComponentName,
                    FilePath = MakeRelativePath(projectDir, r.FilePath),
                    VisibleComponents = r.ChildComponents,
                    InjectedServices = r.InjectedServices.Select(i => i.TypeName).ToList(),
                    SuggestedActions = link?.LinkedActions ?? [],
                    DetectedUiElements = r.MudBlazorComponents,
                    Summary = ""
                };
            })
            .ToList();

        // 9. Build project nodes
        var projectNodes = projects.Select(p => new ProjectNode
        {
            Name = p.Name,
            Path = MakeRelativePath(Path.GetDirectoryName(solutionOrProjectPath)!, p.FilePath ?? ""),
            TargetFramework = p.CompilationOptions?.Platform.ToString() ?? "",
            IsBlazorProject = SolutionLoader.IsBlazorProject(p),
            IsHostProject = p.Name == hostProject.Name,
            ProjectReferences = p.ProjectReferences.Select(r => r.ProjectId.Id.ToString()).ToList()
        }).ToList();

        // 10. Calculate file hashes for incremental updates
        OnProgress?.Invoke("Computing file hashes...");
        var fileHashes = await ComputeFileHashesAsync(projectDir, ct);

        // 11. Build initial model for recommendations analysis
        var initialModel = new ProjectModel
        {
            SchemaVersion = "1",
            SolutionPath = solutionOrProjectPath,
            AppName = hostProject.Name,
            Description = appDescription,
            BlazorHostProject = hostProject.Name,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Projects = projectNodes,
            Routes = routes,
            Components = components,
            Services = services,
            Actions = actions,
            Pages = pages,
            DiRegistrations = diRegistrations,
            FileHashes = fileHashes
        };

        // 12. Generate recommendations and coverage stats
        OnProgress?.Invoke("Generating recommendations...");
        var (recommendations, coverage) = _recommendationEngine.Analyze(initialModel);

        return initialModel with
        {
            Recommendations = recommendations,
            Coverage = coverage
        };
    }

    private void ReportDiagnostics(IReadOnlyList<string> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            // Filter out common noise that doesn't affect analysis
            if (diagnostic.Contains("could not find", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.Contains("analyzer", StringComparison.OrdinalIgnoreCase))
                continue;

            // Report as warning
            OnWarning?.Invoke(diagnostic);
        }
    }

    private static IReadOnlyList<Project> GetReferencedProjects(Solution solution, Project project)
    {
        var referenced = new List<Project>();
        foreach (var projectRef in project.ProjectReferences)
        {
            var refProject = solution.GetProject(projectRef.ProjectId);
            if (refProject != null)
            {
                referenced.Add(refProject);
            }
        }
        return referenced;
    }

    private static string GenerateId(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    private static string GenerateRouteId(string route)
    {
        // Convert /demo/workflows/supplier-compliance to demo_workflows_supplier_compliance
        var id = route.Trim('/')
            .Replace("/", "_")
            .Replace("-", "_")
            .Replace("{", "")
            .Replace("}", "")
            .Replace(":", "_")
            .ToLowerInvariant();

        // Handle root route "/" which becomes empty after trimming
        return string.IsNullOrEmpty(id) ? "home" : id;
    }

    private static string GenerateActionId(string serviceName, string methodName)
    {
        var cleanService = serviceName
            .Replace("Service", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Capabilities", "", StringComparison.OrdinalIgnoreCase);

        var cleanMethod = methodName;
        if (cleanMethod.EndsWith("Async", StringComparison.Ordinal))
            cleanMethod = cleanMethod[..^5];

        return ToSnakeCase(cleanService) + "." + ToSnakeCase(cleanMethod);
    }

    private static string GenerateActionName(string methodName)
    {
        var name = methodName;
        if (name.EndsWith("Async", StringComparison.Ordinal))
            name = name[..^5];

        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }

    private static string ToSnakeCase(string input)
    {
        return string.Concat(input.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    private static string BuildHostProjectErrorMessage(
        string solutionPath,
        string? specifiedHostName,
        int totalProjects,
        int blazorProjectCount,
        IReadOnlyList<string> blazorProjectNames)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Could not identify Blazor host project.");
        sb.AppendLine();

        if (totalProjects == 0)
        {
            sb.AppendLine("REASON: No projects found in the solution.");
            sb.AppendLine();
            sb.AppendLine("SUGGESTIONS:");
            sb.AppendLine("  - Ensure the solution file is valid and contains project references");
            sb.AppendLine("  - Try running 'dotnet restore' first");
            sb.AppendLine("  - Check that the .NET SDK is properly installed");
        }
        else if (blazorProjectCount == 0)
        {
            sb.AppendLine($"REASON: Found {totalProjects} project(s), but none appear to be Blazor projects.");
            sb.AppendLine();
            sb.AppendLine("SUGGESTIONS:");
            sb.AppendLine("  - Blazor projects must reference Microsoft.AspNetCore.Components");
            sb.AppendLine("  - Blazor projects should contain .razor files");
            sb.AppendLine("  - If using a non-standard project structure, specify the host project explicitly:");
            sb.AppendLine($"    agentblazor init \"{solutionPath}\" --host <ProjectName>");
        }
        else if (!string.IsNullOrEmpty(specifiedHostName))
        {
            sb.AppendLine($"REASON: Specified host project '{specifiedHostName}' was not found.");
            sb.AppendLine();
            sb.AppendLine($"Available Blazor projects ({blazorProjectCount}):");
            foreach (var name in blazorProjectNames)
            {
                sb.AppendLine($"  - {name}");
            }
            sb.AppendLine();
            sb.AppendLine("SUGGESTION: Use one of the available project names with --host:");
            sb.AppendLine($"  agentblazor init \"{solutionPath}\" --host {blazorProjectNames.FirstOrDefault() ?? "ProjectName"}");
        }
        else
        {
            sb.AppendLine($"REASON: Found {blazorProjectCount} Blazor projects but could not auto-select one.");
            sb.AppendLine();
            sb.AppendLine("Available Blazor projects:");
            foreach (var name in blazorProjectNames)
            {
                sb.AppendLine($"  - {name}");
            }
            sb.AppendLine();
            sb.AppendLine("SUGGESTION: Specify the host project explicitly:");
            sb.AppendLine($"  agentblazor init \"{solutionPath}\" --host {blazorProjectNames.FirstOrDefault() ?? "ProjectName"}");
        }

        return sb.ToString();
    }

    private static string MakeRelativePath(string basePath, string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return "";

        try
        {
            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);
            return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            return fullPath;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ComputeFileHashesAsync(
        string directory,
        CancellationToken ct)
    {
        var hashes = new Dictionary<string, string>();

        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = MakeRelativePath(directory, file);
            var content = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(content));
            hashes[relativePath] = hash;
        }

        return hashes;
    }
}
