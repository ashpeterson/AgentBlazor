using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class AgentLoop
{
    private readonly Dictionary<string, AgentPatchProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentToolTrace> _transcript = [];

    public AgentLoop(string solutionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);
        SolutionRoot = Path.GetFullPath(solutionRoot);
    }

    public string SolutionRoot { get; }

    public IReadOnlyList<AgentToolTrace> Transcript => _transcript;

    public async Task<AgentFileRead> ReadFileAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolveInsideRoot(path);
        var content = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
        AddTrace("read_file", fullPath, "read file inside solution root");
        return new AgentFileRead
        {
            Path = fullPath,
            RelativePath = ToRelativePath(fullPath),
            Content = content
        };
    }

    public AgentPatchProposal ProposePatch(string summary, IReadOnlyList<AgentProposedFileChange> changes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (changes.Count == 0)
        {
            throw new InvalidOperationException("Patch proposal must include at least one file change.");
        }

        var normalized = changes
            .Select(change => NormalizeChange(change))
            .ToList();
        var proposal = new AgentPatchProposal
        {
            Id = "patch-" + Guid.NewGuid().ToString("N"),
            Summary = summary.Trim(),
            Changes = normalized
        };
        _proposals[proposal.Id] = proposal;
        AddTrace("propose_patch", string.Join(", ", normalized.Select(change => change.RelativePath)), summary);
        return proposal;
    }

    public string RenderDiff(AgentPatchProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var sb = new StringBuilder();
        foreach (var change in proposal.Changes)
        {
            sb.AppendLine($"--- {change.RelativePath}");
            sb.AppendLine($"+++ {change.RelativePath}");
            sb.AppendLine("@@");
            foreach (var line in SplitLines(change.OriginalContent))
            {
                sb.AppendLine("-" + line);
            }
            foreach (var line in SplitLines(change.UpdatedContent))
            {
                sb.AppendLine("+" + line);
            }
        }

        AddTrace("render_diff", proposal.Id, proposal.Summary);
        return sb.ToString();
    }

    public ScaffoldPreviewResult ToPreview(AgentPatchProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        AddTrace("render_preview", proposal.Id, proposal.Summary);
        return new ScaffoldPreviewResult
        {
            Changes = proposal.Changes
                .Select(change => new ScaffoldPreviewFile
                {
                    Path = change.Path,
                    ChangeKind = change.ChangeKind == AgentPatchChangeKind.Create
                        ? ScaffoldPreviewChangeKind.Create
                        : ScaffoldPreviewChangeKind.Update,
                    Summary = proposal.Summary,
                    OriginalContent = change.OriginalContent,
                    UpdatedContent = change.UpdatedContent
                })
                .ToList()
        };
    }

    public async Task<AgentPatchApplyResult> ApplyApprovedPatchAsync(
        string proposalId,
        string approvedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);

        if (!_proposals.TryGetValue(proposalId, out var proposal))
        {
            throw new InvalidOperationException($"Unknown patch proposal '{proposalId}'.");
        }

        foreach (var change in proposal.Changes)
        {
            ResolveInsideRoot(change.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
            await File.WriteAllTextAsync(change.Path, change.UpdatedContent, ct).ConfigureAwait(false);
        }

        AddTrace("apply_approved_patch", proposal.Id, $"approved by {approvedBy.Trim()}");
        return new AgentPatchApplyResult
        {
            ProposalId = proposal.Id,
            ApprovedBy = approvedBy.Trim(),
            AppliedPaths = proposal.Changes.Select(change => change.Path).ToList()
        };
    }

    public async Task<AgentCommandResult> RunValidationCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string approvedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = SolutionRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        AddTrace("run_validation_command", fileName + " " + string.Join(' ', arguments), $"approved by {approvedBy.Trim()}");
        return new AgentCommandResult
        {
            FileName = fileName,
            Arguments = arguments,
            ExitCode = process.ExitCode,
            StandardOutput = output,
            StandardError = error
        };
    }

    public async Task<AgentAuditRecord> WriteAuditAsync(
        string kind,
        string reviewedBy,
        AgentPatchProposal proposal,
        AgentPatchApplyResult applyResult,
        DateTimeOffset timestampUtc,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewedBy);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(applyResult);

        var auditDirectory = ResolveInsideRoot(Path.Combine(".agentblazor", "audit"));
        Directory.CreateDirectory(auditDirectory);
        var auditPath = Path.Combine(
            auditDirectory,
            $"{WorkflowOnboardingPlanner.ToSlug(kind)}-{timestampUtc:yyyyMMddHHmmss}.json");
        var record = new AgentAuditRecord
        {
            Kind = kind,
            ReviewedBy = reviewedBy.Trim(),
            TimestampUtc = timestampUtc,
            ProposalId = proposal.Id,
            Summary = proposal.Summary,
            ProposedFiles = proposal.Changes.Select(change => change.RelativePath).ToList(),
            AppliedFiles = applyResult.AppliedPaths.Select(ToRelativePath).ToList(),
            Metadata = metadata ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            Transcript = Transcript.ToList(),
            Path = ToRelativePath(auditPath)
        };
        var content = JsonSerializer.Serialize(record, AgentAuditRecord.JsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(auditPath, content, ct).ConfigureAwait(false);
        AddTrace("write_audit", ToRelativePath(auditPath), kind);
        return record;
    }

    private AgentProposedFileChange NormalizeChange(AgentProposedFileChange change)
    {
        var fullPath = ResolveInsideRoot(change.Path);
        var exists = File.Exists(fullPath);
        var original = exists ? File.ReadAllText(fullPath) : string.Empty;
        if (exists && !string.Equals(original, change.OriginalContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Patch proposal for '{ToRelativePath(fullPath)}' is stale.");
        }

        if (!exists && change.ChangeKind != AgentPatchChangeKind.Create)
        {
            throw new InvalidOperationException($"Patch proposal for '{ToRelativePath(fullPath)}' targets a missing file.");
        }

        return change with
        {
            Path = fullPath,
            RelativePath = ToRelativePath(fullPath),
            OriginalContent = original,
            ChangeKind = exists ? AgentPatchChangeKind.Update : AgentPatchChangeKind.Create
        };
    }

    private string ResolveInsideRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(SolutionRoot, path));
        if (!IsInsideRoot(SolutionRoot, fullPath))
        {
            throw new InvalidOperationException($"Agent tool path is outside the solution root: {path}");
        }

        return fullPath;
    }

    private string ToRelativePath(string fullPath)
        => Path.GetRelativePath(SolutionRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    private void AddTrace(string tool, string target, string summary)
        => _transcript.Add(new AgentToolTrace
        {
            Tool = tool,
            Target = target,
            Summary = summary,
            TimestampUtc = DateTimeOffset.UtcNow
        });

    private static bool IsInsideRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
}

public sealed record AgentFileRead
{
    public string Path { get; init; } = "";

    public string RelativePath { get; init; } = "";

    public string Content { get; init; } = "";
}

public sealed record AgentPatchProposal
{
    public string Id { get; init; } = "";

    public string Summary { get; init; } = "";

    public IReadOnlyList<AgentProposedFileChange> Changes { get; init; } = [];
}

public sealed record AgentProposedFileChange
{
    public string Path { get; init; } = "";

    public string RelativePath { get; init; } = "";

    public AgentPatchChangeKind ChangeKind { get; init; }

    public string OriginalContent { get; init; } = "";

    public string UpdatedContent { get; init; } = "";
}

public enum AgentPatchChangeKind
{
    Create,
    Update
}

public sealed record AgentPatchApplyResult
{
    public string ProposalId { get; init; } = "";

    public string ApprovedBy { get; init; } = "";

    public IReadOnlyList<string> AppliedPaths { get; init; } = [];
}

public sealed record AgentCommandResult
{
    public string FileName { get; init; } = "";

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = "";

    public string StandardError { get; init; } = "";
}

public sealed record AgentToolTrace
{
    public string Tool { get; init; } = "";

    public string Target { get; init; } = "";

    public string Summary { get; init; } = "";

    public DateTimeOffset TimestampUtc { get; init; }
}

public sealed record AgentAuditRecord
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Kind { get; init; } = "";

    public string ReviewedBy { get; init; } = "";

    public DateTimeOffset TimestampUtc { get; init; }

    public string ProposalId { get; init; } = "";

    public string Summary { get; init; } = "";

    public IReadOnlyList<string> ProposedFiles { get; init; } = [];

    public IReadOnlyList<string> AppliedFiles { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Metadata { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AgentToolTrace> Transcript { get; init; } = [];

    public string Path { get; init; } = "";
}
