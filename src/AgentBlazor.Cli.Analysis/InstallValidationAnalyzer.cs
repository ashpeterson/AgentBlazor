using System.Text.Json;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class InstallValidationAnalyzer
{
    private readonly InstallReadinessAnalyzer _readinessAnalyzer = new();
    private readonly ExistingAppScaffoldPlanner _scaffoldPlanner = new();

    public async Task<InstallValidationReport> AnalyzeAsync(
        string solutionOrProjectPath,
        string? hostProjectName,
        CancellationToken ct = default)
    {
        var readiness = await _readinessAnalyzer.AnalyzeAsync(solutionOrProjectPath, hostProjectName, ct).ConfigureAwait(false);
        var plan = await _scaffoldPlanner.PlanAsync(solutionOrProjectPath, hostProjectName, ct).ConfigureAwait(false);
        var checks = new List<InstallReadinessCheck>();
        var projectDirectory = Path.GetDirectoryName(readiness.HostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the host project directory for '{readiness.HostProjectPath}'.");
        var manifestPath = Path.Combine(projectDirectory, ".agentblazor", "scaffold-manifest.json");
        ScaffoldManifestDocument? manifest = null;

        if (!File.Exists(manifestPath))
        {
            checks.Add(new InstallReadinessCheck
            {
                Id = "scaffold-manifest",
                Title = "Scaffold manifest",
                Status = InstallReadinessStatus.Warning,
                Message = "No scaffold manifest was found for this host project.",
                FilePath = manifestPath,
                SuggestedFix = "This is expected for manual installs. Run `agentblazor scaffold --approve` if you want the CLI to write an install audit trail."
            });
        }
        else
        {
            try
            {
                await using var stream = File.OpenRead(manifestPath);
                manifest = await JsonSerializer.DeserializeAsync<ScaffoldManifestDocument>(
                    stream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    },
                    ct).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                manifest = null;
            }

            if (manifest is null)
            {
                checks.Add(new InstallReadinessCheck
                {
                    Id = "scaffold-manifest",
                    Title = "Scaffold manifest",
                    Status = InstallReadinessStatus.Missing,
                    Message = "The scaffold manifest exists but could not be parsed.",
                    FilePath = manifestPath,
                    SuggestedFix = "Regenerate the manifest with `agentblazor scaffold --approve`, or delete the invalid file if the install is being managed manually."
                });
            }
            else
            {
                checks.Add(new InstallReadinessCheck
                {
                    Id = "scaffold-manifest",
                    Title = "Scaffold manifest",
                    Status = InstallReadinessStatus.Pass,
                    Message = $"Found scaffold manifest with {manifest.ChangedFiles.Count} tracked file(s).",
                    FilePath = manifestPath
                });

                var hostMatches = string.Equals(manifest.HostProjectName, readiness.HostProjectName, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(
                                      NormalizePath(manifest.HostProjectPath),
                                      NormalizePath(readiness.HostProjectPath),
                                      StringComparison.OrdinalIgnoreCase);

                checks.Add(new InstallReadinessCheck
                {
                    Id = "manifest-host-match",
                    Title = "Manifest host match",
                    Status = hostMatches ? InstallReadinessStatus.Pass : InstallReadinessStatus.Missing,
                    Message = hostMatches
                        ? "The manifest points at the current host project."
                        : "The scaffold manifest points at a different host project than the one being validated.",
                    FilePath = manifestPath,
                    SuggestedFix = hostMatches
                        ? null
                        : "Re-run `agentblazor scaffold --approve` for this host if you want the manifest to track the current project."
                });

                var missingFiles = manifest.ChangedFiles
                    .Where(file => string.IsNullOrWhiteSpace(file.Path) || !File.Exists(file.Path))
                    .ToArray();

                checks.Add(new InstallReadinessCheck
                {
                    Id = "manifest-files",
                    Title = "Manifest file audit",
                    Status = missingFiles.Length == 0 ? InstallReadinessStatus.Pass : InstallReadinessStatus.Missing,
                    Message = missingFiles.Length == 0
                        ? "All files tracked by the scaffold manifest still exist."
                        : $"The scaffold manifest references {missingFiles.Length} file(s) that are no longer present.",
                    FilePath = manifestPath,
                    SuggestedFix = missingFiles.Length == 0
                        ? null
                        : "Re-run `agentblazor scaffold --approve` or update the host manually so the manifest reflects the current install state."
                });
            }
        }

        AddManualReviewChecks(plan, readiness, checks);

        return new InstallValidationReport
        {
            Readiness = readiness,
            Checks = checks
        };
    }

    private static void AddManualReviewChecks(
        ScaffoldPlan plan,
        InstallReadinessReport readiness,
        List<InstallReadinessCheck> checks)
    {
        var manualReviewItems = plan.Items.Where(item => item.Action == ScaffoldPlanAction.ManualReview).ToArray();
        if (manualReviewItems.Length == 0)
        {
            return;
        }

        foreach (var item in manualReviewItems)
        {
            var readinessCheck = readiness.Checks.FirstOrDefault(check => check.Id == item.Id);
            if (readinessCheck is null)
            {
                continue;
            }

            checks.Add(new InstallReadinessCheck
            {
                Id = $"manual-review:{item.Id}",
                Title = $"Manual review: {item.Id}",
                Status = readinessCheck.Status,
                Message = readinessCheck.Status == InstallReadinessStatus.Pass
                    ? $"Completed: {item.Summary}"
                    : $"Still needs review: {item.Summary}",
                FilePath = item.TargetPath,
                SuggestedFix = item.Guidance ?? item.Reason
            });
        }
    }

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);

    private sealed record ScaffoldManifestDocument
    {
        public string HostProjectName { get; init; } = string.Empty;

        public string HostProjectPath { get; init; } = string.Empty;

        public IReadOnlyList<ScaffoldManifestFileDocument> ChangedFiles { get; init; } = [];
    }

    private sealed record ScaffoldManifestFileDocument
    {
        public string Path { get; init; } = string.Empty;
    }
}
