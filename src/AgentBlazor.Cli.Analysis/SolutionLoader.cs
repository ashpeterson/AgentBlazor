using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Xml.Linq;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Loads .NET solutions and projects using MSBuild workspace.
/// </summary>
public sealed class SolutionLoader : IDisposable
{
    private static bool s_msbuildRegistered;
    private static readonly object s_lock = new();
    private MSBuildWorkspace? _workspace;

    public IReadOnlyList<string> Diagnostics { get; private set; } = [];

    public static void EnsureMSBuildRegistered()
    {
        if (s_msbuildRegistered) return;

        lock (s_lock)
        {
            if (s_msbuildRegistered) return;

            var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            if (instances.Count == 0)
            {
                throw new InvalidOperationException(
                    "No MSBuild instances found. Ensure .NET SDK is installed.");
            }

            // Prefer the latest version
            var instance = instances.OrderByDescending(i => i.Version).First();
            MSBuildLocator.RegisterInstance(instance);
            s_msbuildRegistered = true;
        }
    }

    public async Task<Solution> LoadSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        if (UseStaticWorkspace())
        {
            Diagnostics = ["Using static source-file analysis because AGENTBLAZOR_STATIC_WORKSPACE=1 is set."];
            return await StaticWorkspaceLoader.LoadSolutionAsync(solutionPath, ct).ConfigureAwait(false);
        }

        try
        {
            EnsureMSBuildRegistered();

            var diagnostics = new List<string>();
            _workspace = MSBuildWorkspace.Create();
            RegisterWorkspaceDiagnostics(_workspace, diagnostics);

            var solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
            Diagnostics = diagnostics;
            return solution;
        }
        catch (Exception ex) when (ShouldFallbackToStaticWorkspace(ex))
        {
            Diagnostics = [$"MSBuildWorkspace failed ({GetRelevantExceptionMessage(ex)}). Falling back to static source-file analysis."];
            return await StaticWorkspaceLoader.LoadSolutionAsync(solutionPath, ct).ConfigureAwait(false);
        }
    }

    public async Task<Project> LoadProjectAsync(string projectPath, CancellationToken ct = default)
    {
        if (UseStaticWorkspace())
        {
            Diagnostics = ["Using static source-file analysis because AGENTBLAZOR_STATIC_WORKSPACE=1 is set."];
            return await StaticWorkspaceLoader.LoadProjectAsync(projectPath, ct).ConfigureAwait(false);
        }

        try
        {
            EnsureMSBuildRegistered();

            var diagnostics = new List<string>();
            _workspace = MSBuildWorkspace.Create();
            RegisterWorkspaceDiagnostics(_workspace, diagnostics);

            var project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
            Diagnostics = diagnostics;
            return project;
        }
        catch (Exception ex) when (ShouldFallbackToStaticWorkspace(ex))
        {
            Diagnostics = [$"MSBuildWorkspace failed ({GetRelevantExceptionMessage(ex)}). Falling back to static source-file analysis."];
            return await StaticWorkspaceLoader.LoadProjectAsync(projectPath, ct).ConfigureAwait(false);
        }
    }

    private static void RegisterWorkspaceDiagnostics(Workspace workspace, ICollection<string> diagnostics)
    {
        workspace.RegisterWorkspaceFailedHandler(
            e => diagnostics.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}"),
            options: null);
    }

    /// <summary>
    /// Finds Blazor host projects in the solution by checking for Blazor markers.
    /// </summary>
    public static IReadOnlyList<Project> FindBlazorProjects(Solution solution)
    {
        var blazorProjects = new List<Project>();

        foreach (var project in solution.Projects)
        {
            if (IsBlazorProject(project))
            {
                blazorProjects.Add(project);
            }
        }

        return blazorProjects;
    }

    /// <summary>
    /// Checks if a project is a Blazor project by examining its references and properties.
    /// </summary>
    public static bool IsBlazorProject(Project project)
    {
        // Check for Blazor-related metadata references
        var hasBlazorReference = project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Any(r =>
            {
                var name = Path.GetFileNameWithoutExtension(r.FilePath ?? "");
                return name.StartsWith("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase);
            });

        // Check for .razor files
        var hasRazorFiles = project.AdditionalDocuments
            .Any(d => d.FilePath?.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) == true);

        // Also check regular documents for .razor (some project systems report them differently)
        if (!hasRazorFiles)
        {
            hasRazorFiles = project.Documents
                .Any(d => d.FilePath?.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) == true);
        }

        return hasBlazorReference || hasRazorFiles;
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }

    private static bool ShouldFallbackToStaticWorkspace(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName?.Contains("RemoteInvocationException", StringComparison.OrdinalIgnoreCase) == true ||
                current is TypeInitializationException ||
                current.Message.Contains("XMakeElements", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("MSBuildWorkspace", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UseStaticWorkspace()
        => Environment.GetEnvironmentVariable("AGENTBLAZOR_STATIC_WORKSPACE") == "1";

    private static string GetRelevantExceptionMessage(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("XMakeElements", StringComparison.OrdinalIgnoreCase))
            {
                return current.Message;
            }
        }

        return ex.Message;
    }
}

internal static class StaticWorkspaceLoader
{
    public static async Task<Solution> LoadSolutionAsync(string solutionPath, CancellationToken ct)
    {
        var solutionFullPath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(solutionFullPath)!;
        var projectPaths = solutionFullPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnxProjectPaths(solutionFullPath, solutionDirectory)
            : ReadSlnProjectPaths(solutionFullPath, solutionDirectory);

        if (projectPaths.Count == 0)
        {
            projectPaths = Directory.GetFiles(solutionDirectory, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !IsUnderIgnoredDirectory(path))
                .ToList();
        }

        return await BuildSolutionAsync(projectPaths, ct).ConfigureAwait(false);
    }

    public static async Task<Project> LoadProjectAsync(string projectPath, CancellationToken ct)
    {
        var solution = await BuildSolutionAsync([Path.GetFullPath(projectPath)], ct).ConfigureAwait(false);
        return solution.Projects.First();
    }

    private static async Task<Solution> BuildSolutionAsync(IReadOnlyList<string> projectPaths, CancellationToken ct)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectIds = new Dictionary<string, ProjectId>(StringComparer.OrdinalIgnoreCase);
        var projectInfos = new Dictionary<string, StaticProjectInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in projectPaths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            ct.ThrowIfCancellationRequested();
            var info = StaticProjectInfo.Read(projectPath);
            var projectId = ProjectId.CreateNewId(info.Name);
            projectIds[projectPath] = projectId;
            projectInfos[projectPath] = info;

            solution = solution.AddProject(
                ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    info.Name,
                    info.Name,
                    LanguageNames.CSharp,
                    filePath: projectPath));
        }

        foreach (var (projectPath, info) in projectInfos)
        {
            ct.ThrowIfCancellationRequested();
            var projectId = projectIds[projectPath];

            foreach (var referencePath in info.ProjectReferences)
            {
                if (projectIds.TryGetValue(referencePath, out var referenceId))
                {
                    solution = solution.AddProjectReference(projectId, new ProjectReference(referenceId));
                }
            }

            foreach (var filePath in EnumerateSourceFiles(info.Directory))
            {
                ct.ThrowIfCancellationRequested();
                var text = SourceText.From(await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false));

                if (filePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                {
                    solution = solution.AddAdditionalDocument(
                        DocumentId.CreateNewId(projectId),
                        Path.GetFileName(filePath),
                        text,
                        filePath: filePath);
                }
                else
                {
                    solution = solution.AddDocument(
                        DocumentId.CreateNewId(projectId),
                        Path.GetFileName(filePath),
                        text,
                        filePath: filePath);
                }
            }
        }

        return solution;
    }

    private static List<string> ReadSlnProjectPaths(string solutionPath, string solutionDirectory)
    {
        var projectPaths = new List<string>();

        foreach (var line in File.ReadLines(solutionPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var relativePath = NormalizeSolutionPath(parts[1].Trim('"'));
            if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projectPaths.Add(Path.GetFullPath(Path.Combine(solutionDirectory, relativePath)));
        }

        return projectPaths;
    }

    private static List<string> ReadSlnxProjectPaths(string solutionPath, string solutionDirectory)
    {
        var document = XDocument.Load(solutionPath);
        return document
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, NormalizeSolutionPath(path!))))
            .ToList();
    }

    private static string NormalizeSolutionPath(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
        => Directory
            .EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) &&
                !IsUnderIgnoredDirectory(path));

    private static bool IsUnderIgnoredDirectory(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record StaticProjectInfo(
        string Name,
        string Path,
        string Directory,
        IReadOnlyList<string> ProjectReferences)
    {
        public static StaticProjectInfo Read(string projectPath)
        {
            var fullPath = System.IO.Path.GetFullPath(projectPath);
            var directory = System.IO.Path.GetDirectoryName(fullPath)!;
            var name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
            var references = new List<string>();

            try
            {
                var document = XDocument.Load(fullPath);
                foreach (var reference in document.Descendants("ProjectReference"))
                {
                    var include = reference.Attribute("Include")?.Value;
                    if (!string.IsNullOrWhiteSpace(include))
                    {
                        references.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, include)));
                    }
                }
            }
            catch
            {
                // Keep fallback loading best-effort. The caller still gets source files from this project.
            }

            return new StaticProjectInfo(name, fullPath, directory, references);
        }
    }
}
