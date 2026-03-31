using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

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
        EnsureMSBuildRegistered();

        var diagnostics = new List<string>();
        _workspace = MSBuildWorkspace.Create();
        _workspace.WorkspaceFailed += (_, e) =>
        {
            diagnostics.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}");
        };

        var solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
        Diagnostics = diagnostics;
        return solution;
    }

    public async Task<Project> LoadProjectAsync(string projectPath, CancellationToken ct = default)
    {
        EnsureMSBuildRegistered();

        var diagnostics = new List<string>();
        _workspace = MSBuildWorkspace.Create();
        _workspace.WorkspaceFailed += (_, e) =>
        {
            diagnostics.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}");
        };

        var project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: ct);
        Diagnostics = diagnostics;
        return project;
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
}
