using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed record WorkflowOnboardingCandidate
{
    public string Id { get; init; } = "";

    public string Slug { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string CapabilityClassName { get; init; } = "";

    public string Risk { get; init; } = "safe read-only";

    public bool RequiresApproval { get; init; }

    public IReadOnlyList<string> Methods { get; init; } = [];

    public IReadOnlyList<string> Routes { get; init; } = [];

    public IReadOnlyList<string> Evidence { get; init; } = [];

    public IReadOnlyList<AnalysisCorpusChunk> RetrievedEvidence { get; init; } = [];

    public bool ExistingCapabilityOnly { get; init; }
}

public sealed record WorkflowOnboardingPlan
{
    public string SolutionRoot { get; init; } = "";

    public string AgentBlazorDirectory { get; init; } = "";

    public ProjectModel Model { get; init; } = new();

    public IReadOnlyList<WorkflowOnboardingCandidate> Candidates { get; init; } = [];
}

public sealed record WorkflowReviewDecisions
{
    public IReadOnlySet<string> ApprovedIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> RejectedIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> PinnedIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> UnpinnedIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string? ReviewedBy { get; init; }

    public static WorkflowReviewDecisions Empty { get; } = new();
}

internal sealed record WorkflowReviewCandidateState
{
    public string Status { get; init; } = "proposed";

    public bool Pinned { get; init; }
}

public sealed class WorkflowOnboardingPlanner
{
    private readonly IAnalysisRetrieval _retrieval;

    public WorkflowOnboardingPlanner()
        : this(new LexicalAnalysisRetrieval())
    {
    }

    public WorkflowOnboardingPlanner(IAnalysisRetrieval retrieval)
    {
        _retrieval = retrieval;
    }

    public WorkflowOnboardingPlan Plan(ProjectModel model, string solutionRoot)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);

        var candidates = model.WorkflowClusters
            .Where(cluster => cluster.Methods.Count >= 2)
            .Select(cluster => FromCluster(model, cluster))
            .Concat(BuildExistingCapabilityCandidates(model))
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.ExistingCapabilityOnly)
            .ThenBy(candidate => RiskRank(candidate.Risk))
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkflowOnboardingPlan
        {
            SolutionRoot = solutionRoot,
            AgentBlazorDirectory = Path.Combine(solutionRoot, ".agentblazor"),
            Model = model,
            Candidates = candidates
        };
    }

    private WorkflowOnboardingCandidate FromCluster(ProjectModel model, WorkflowClusterModel cluster)
    {
        var query = string.Join(
            ' ',
            cluster.Name,
            cluster.Summary,
            string.Join(' ', cluster.DomainTerms),
            string.Join(' ', cluster.Methods.Select(method => method.Method)),
            string.Join(' ', model.DesiredAgentWorkflows));
        var retrieved = _retrieval.Search(model.Corpus, query, maxResults: 6)
            .Select(result => result.Chunk)
            .ToList();

        return new WorkflowOnboardingCandidate
        {
            Id = cluster.Id,
            Slug = ToSlug(cluster.Name),
            Name = cluster.Name,
            Description = cluster.Summary,
            CapabilityClassName = ToPascalCase(cluster.Name) + "Capability",
            Risk = cluster.Risk,
            RequiresApproval = cluster.RequiresApproval,
            Methods = cluster.Methods.Select(method => $"{method.Service}.{method.Method}").ToList(),
            Routes = cluster.RouteHints,
            Evidence = cluster.Evidence,
            RetrievedEvidence = retrieved
        };
    }

    private IEnumerable<WorkflowOnboardingCandidate> BuildExistingCapabilityCandidates(ProjectModel model)
    {
        var confirmedGroups = model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .GroupBy(action => action.SourceService, StringComparer.OrdinalIgnoreCase);

        foreach (var group in confirmedGroups)
        {
            var actions = group
                .OrderBy(action => action.MethodName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (actions.Count == 0)
            {
                continue;
            }

            var name = group.Key.EndsWith("Capability", StringComparison.OrdinalIgnoreCase)
                ? group.Key[..^"Capability".Length]
                : group.Key;
            var displayName = SplitPascalCase(name);
            yield return new WorkflowOnboardingCandidate
            {
                Id = "existing-" + ToSlug(group.Key),
                Slug = ToSlug(displayName),
                Name = displayName,
                Description = $"Existing AgentBlazor capability {group.Key} with {actions.Count} confirmed action(s).",
                CapabilityClassName = group.Key,
                Risk = actions.Any(action => action.RequiresApproval || action.IsMutationLikely) ? "approval required" : "safe read-only",
                RequiresApproval = actions.Any(action => action.RequiresApproval || action.IsMutationLikely),
                Methods = actions.Select(action => $"{action.SourceService}.{action.MethodName}").ToList(),
                Routes = actions.SelectMany(action => action.RelevantRoutes).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Evidence = ["existing [AgentCapability] / [AgentAction] static evidence"],
                RetrievedEvidence = _retrieval.Search(model.Corpus, group.Key, maxResults: 4).Select(result => result.Chunk).ToList(),
                ExistingCapabilityOnly = true
            };
        }
    }

    private static int RiskRank(string risk)
        => risk.Contains("high", StringComparison.OrdinalIgnoreCase) ? 3 :
            risk.Contains("approval", StringComparison.OrdinalIgnoreCase) ? 2 :
            risk.Contains("mutation", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    public static string ToSlug(string value)
    {
        var words = SplitWords(value);
        return words.Count == 0 ? "workflow" : string.Join('-', words).ToLowerInvariant();
    }

    private static string ToPascalCase(string value)
        => string.Concat(SplitWords(value).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    private static string SplitPascalCase(string value)
        => string.Join(' ', SplitWords(value).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    private static IReadOnlyList<string> SplitWords(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(character) && !char.IsUpper(current[^1]))
            {
                Flush();
            }

            current.Append(char.ToLowerInvariant(character));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            words.Add(current.ToString());
            current.Clear();
        }
    }
}

public sealed class WorkflowOnboardingArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IReadOnlySet<string> ReadApprovedCandidateIds(string agentBlazorDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentBlazorDirectory);

        var reviewPath = Path.Combine(agentBlazorDirectory, "workflow-onboarding.json");
        return ReadExistingReviewState(reviewPath)
            .Where(pair => pair.Value.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<WorkflowArtifactChange> Preview(
        WorkflowOnboardingPlan plan,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        DateOnly today,
        WorkflowReviewDecisions? decisions = null)
        => BuildChanges(plan, selected, today, decisions ?? WorkflowReviewDecisions.Empty);

    public async Task<IReadOnlyList<WorkflowArtifactChange>> ApplyAsync(
        WorkflowOnboardingPlan plan,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        DateOnly today,
        WorkflowReviewDecisions? decisions = null,
        CancellationToken ct = default)
    {
        var changes = BuildChanges(plan, selected, today, decisions ?? WorkflowReviewDecisions.Empty);
        foreach (var change in changes)
        {
            if (!IsInsideRoot(plan.SolutionRoot, change.Path))
            {
                throw new InvalidOperationException($"Refusing to write outside the solution root: {change.Path}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
            await File.WriteAllTextAsync(change.Path, change.UpdatedContent, ct).ConfigureAwait(false);
        }

        return changes;
    }

    private static IReadOnlyList<WorkflowArtifactChange> BuildChanges(
        WorkflowOnboardingPlan plan,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        DateOnly today,
        WorkflowReviewDecisions decisions)
    {
        var changes = new List<WorkflowArtifactChange>();
        var agentDir = plan.AgentBlazorDirectory;
        var skillsDir = Path.Combine(agentDir, "skills");
        var ordered = selected.OrderBy(candidate => candidate.Slug, StringComparer.OrdinalIgnoreCase).ToList();
        var reviewPath = Path.Combine(agentDir, "workflow-onboarding.json");
        var reviewState = ResolveReviewState(plan, ordered, decisions, reviewPath);
        var reviewedBy = ResolveReviewedBy(reviewPath, decisions);

        AddChange(
            changes,
            reviewPath,
            BuildReviewJson(plan, reviewState, today, reviewedBy),
            "Workflow onboarding review data");
        AddChange(
            changes,
            Path.Combine(agentDir, "workflow-onboarding.md"),
            BuildReviewMarkdown(plan, reviewState, today, reviewedBy),
            "Workflow onboarding review report");
        AddChange(
            changes,
            Path.Combine(agentDir, "workflow-onboarding.html"),
            BuildReviewHtml(plan, reviewState, today, reviewedBy),
            "Workflow onboarding review dashboard");
        if (ordered.Count == 0)
        {
            return changes;
        }

        AddChange(changes, Path.Combine(agentDir, "SOUL.md"), BuildSoul(plan, ordered, today), "SOUL.md project constitution");
        AddChange(changes, Path.Combine(skillsDir, "index.json"), BuildIndexJson(ordered), "Level 0 skill index");
        var metadataPath = Path.Combine(skillsDir, ".metadata.json");
        AddChange(changes, metadataPath, BuildMetadataJson(ordered, today, metadataPath), "Skill usage and curator metadata");

        foreach (var candidate in ordered)
        {
            var skillDir = Path.Combine(skillsDir, candidate.Slug);
            AddChange(changes, Path.Combine(skillDir, "SKILL.md"), BuildSkill(candidate, today), $"Workflow skill {candidate.Name}");
            if (candidate.RetrievedEvidence.Count > 0)
            {
                AddChange(
                    changes,
                    Path.Combine(skillDir, "references", "evidence.md"),
                    BuildEvidenceReference(candidate),
                    $"Evidence reference for {candidate.Name}");
            }
        }

        return changes;
    }

    private static void AddChange(List<WorkflowArtifactChange> changes, string path, string updated, string summary)
    {
        var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add(new WorkflowArtifactChange
        {
            Path = path,
            OriginalContent = original,
            UpdatedContent = updated,
            ChangeKind = File.Exists(path) ? WorkflowArtifactChangeKind.Update : WorkflowArtifactChangeKind.Create,
            Summary = summary
        });
    }

    private static string BuildSoul(WorkflowOnboardingPlan plan, IReadOnlyList<WorkflowOnboardingCandidate> selected, DateOnly today)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AgentBlazor SOUL");
        sb.AppendLine();
        sb.AppendLine($"Last reviewed: {today:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## Domain");
        sb.AppendLine();
        sb.AppendLine($"- App: {plan.Model.AppName}");
        sb.AppendLine($"- Host project: {plan.Model.BlazorHostProject}");
        sb.AppendLine($"- Description: {plan.Model.Description}");
        if (plan.Model.Corpus.DomainTerms.Count > 0)
        {
            sb.AppendLine($"- Domain terms: {string.Join(", ", plan.Model.Corpus.DomainTerms.Take(16))}");
        }
        if (plan.Model.DesiredAgentWorkflows.Count > 0)
        {
            sb.AppendLine("- Developer-stated agent goals:");
            foreach (var goal in plan.Model.DesiredAgentWorkflows)
            {
                sb.AppendLine($"  - {goal}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Approved Workflow Scope");
        if (selected.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("- No workflows have been approved for scaffolding yet.");
        }
        else
        {
            foreach (var candidate in selected)
            {
                sb.AppendLine($"- {candidate.Name} (`{candidate.Id}`): {candidate.Description}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Restrictions");
        sb.AppendLine();
        sb.AppendLine("- Do not execute mutating workflow steps unless the skill and CLI session require approval.");
        sb.AppendLine("- Do not modify files outside the detected solution root without explicit developer approval.");
        sb.AppendLine("- Do not invent services, routes, methods, entities, or policies that are not present in static analysis evidence.");
        sb.AppendLine("- Keep provider secrets and environment-specific configuration under developer control.");
        sb.AppendLine();
        sb.AppendLine("## Safety Boundaries");
        sb.AppendLine();
        sb.AppendLine("- Prefer read-only inspection before proposing mutations.");
        sb.AppendLine("- Surface high-risk, admin, tenant, auth, database, cache, and permission changes as manual review.");
        sb.AppendLine("- Preserve existing manual `[AgentCapability]` and `[AgentAction]` workflows.");
        return sb.ToString();
    }

    private static string BuildReviewJson(
        WorkflowOnboardingPlan plan,
        IReadOnlyDictionary<string, WorkflowReviewCandidateState> reviewState,
        DateOnly today,
        string? reviewedBy)
    {
        var approvedCandidateIds = ReviewIdsByStatus(reviewState, "approved");
        var rejectedCandidateIds = ReviewIdsByStatus(reviewState, "rejected");
        var pinnedCandidateIds = reviewState
            .Where(pair => pair.Value.Pinned)
            .Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var payload = new
        {
            schemaVersion = 1,
            generatedAt = $"{today:yyyy-MM-dd}",
            reviewedBy,
            review = new
            {
                reviewedBy,
                generatedAt = $"{today:yyyy-MM-dd}",
                approvedCandidateIds,
                rejectedCandidateIds,
                pinnedCandidateIds
            },
            app = new
            {
                name = plan.Model.AppName,
                hostProject = plan.Model.BlazorHostProject,
                description = plan.Model.Description,
                desiredAgentWorkflows = plan.Model.DesiredAgentWorkflows,
                domainTerms = plan.Model.Corpus.DomainTerms.Take(24).ToList()
            },
            candidates = plan.Candidates
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => new
                {
                    id = candidate.Id,
                    slug = candidate.Slug,
                    name = candidate.Name,
                    description = candidate.Description,
                    status = reviewState.TryGetValue(candidate.Id, out var state) ? state.Status : "proposed",
                    pinned = state?.Pinned ?? false,
                    risk = candidate.Risk,
                    requiresApproval = candidate.RequiresApproval,
                    capabilityClassName = candidate.CapabilityClassName,
                    requiredApprovals = BuildRequiredApprovals(candidate),
                    methods = candidate.Methods,
                    routes = candidate.Routes,
                    evidence = candidate.Evidence,
                    fileReferences = candidate.RetrievedEvidence
                        .Where(chunk => !string.IsNullOrWhiteSpace(chunk.FilePath))
                        .Select(chunk => NormalizeReviewPath(plan.SolutionRoot, chunk.FilePath))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    retrievedEvidence = candidate.RetrievedEvidence
                        .Select(chunk => new
                        {
                            kind = chunk.Kind.ToString(),
                            title = chunk.Title,
                            filePath = string.IsNullOrWhiteSpace(chunk.FilePath)
                                ? null
                                : NormalizeReviewPath(plan.SolutionRoot, chunk.FilePath),
                            routes = chunk.RelatedRoutes,
                            services = chunk.RelatedServices,
                            methods = chunk.RelatedMethods
                        })
                        .ToList()
                })
                .ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
    }

    private static string BuildReviewMarkdown(
        WorkflowOnboardingPlan plan,
        IReadOnlyDictionary<string, WorkflowReviewCandidateState> reviewState,
        DateOnly today,
        string? reviewedBy)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AgentBlazor Workflow Onboarding Review");
        sb.AppendLine();
        sb.AppendLine($"Generated: {today:yyyy-MM-dd}");
        sb.AppendLine($"Reviewed by: {MarkdownValueOrPending(reviewedBy)}");
        sb.AppendLine();
        sb.AppendLine("## App Context");
        sb.AppendLine();
        sb.AppendLine($"- App: {plan.Model.AppName}");
        sb.AppendLine($"- Host project: {plan.Model.BlazorHostProject}");
        sb.AppendLine($"- Description: {plan.Model.Description}");
        if (plan.Model.DesiredAgentWorkflows.Count > 0)
        {
            sb.AppendLine("- Developer-stated agent goals:");
            foreach (var goal in plan.Model.DesiredAgentWorkflows)
            {
                sb.AppendLine($"  - {goal}");
            }
        }

        if (plan.Model.Corpus.DomainTerms.Count > 0)
        {
            sb.AppendLine($"- Domain terms: {string.Join(", ", plan.Model.Corpus.DomainTerms.Take(24))}");
        }

        sb.AppendLine();
        sb.AppendLine("## Candidate Summary");
        sb.AppendLine();
        sb.AppendLine("| Status | Candidate | Risk | Methods | Routes |");
        sb.AppendLine("| --- | --- | --- | ---: | --- |");
        foreach (var candidate in plan.Candidates.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            var state = reviewState.TryGetValue(candidate.Id, out var item)
                ? item
                : new WorkflowReviewCandidateState();
            var status = state.Pinned ? $"{state.Status}, pinned" : state.Status;
            var routes = candidate.Routes.Count == 0 ? "-" : string.Join(", ", candidate.Routes.Select(route => $"`{route}`"));
            sb.AppendLine($"| {status} | `{candidate.Id}` {EscapeMarkdownTable(candidate.Name)} | {EscapeMarkdownTable(candidate.Risk)} | {candidate.Methods.Count} | {routes} |");
        }

        foreach (var candidate in plan.Candidates.OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
        {
            var state = reviewState.TryGetValue(candidate.Id, out var item)
                ? item
                : new WorkflowReviewCandidateState();
            sb.AppendLine();
            sb.AppendLine($"## {candidate.Name}");
            sb.AppendLine();
            sb.AppendLine($"- Status: {state.Status}");
            sb.AppendLine($"- Pinned: {state.Pinned.ToString().ToLowerInvariant()}");
            sb.AppendLine($"- Candidate id: `{candidate.Id}`");
            sb.AppendLine($"- Slug: `{candidate.Slug}`");
            sb.AppendLine($"- Risk: {candidate.Risk}");
            sb.AppendLine($"- Suggested capability class: `{candidate.CapabilityClassName}`");
            sb.AppendLine($"- Required approvals: {string.Join(", ", BuildRequiredApprovals(candidate))}");
            if (candidate.Routes.Count > 0)
            {
                sb.AppendLine($"- Routes: {string.Join(", ", candidate.Routes.Select(route => $"`{route}`"))}");
            }
            sb.AppendLine();
            sb.AppendLine(candidate.Description);
            sb.AppendLine();
            sb.AppendLine("### Methods");
            foreach (var method in candidate.Methods)
            {
                sb.AppendLine($"- `{method}`");
            }

            sb.AppendLine();
            sb.AppendLine("### Evidence");
            foreach (var evidence in candidate.Evidence.DefaultIfEmpty("static analysis evidence"))
            {
                sb.AppendLine($"- {evidence}");
            }

            var files = candidate.RetrievedEvidence
                .Where(chunk => !string.IsNullOrWhiteSpace(chunk.FilePath))
                .Select(chunk => NormalizeReviewPath(plan.SolutionRoot, chunk.FilePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### File References");
                foreach (var file in files)
                {
                    sb.AppendLine($"- `{file}`");
                }
            }
        }

        return sb.ToString();
    }

    private static string BuildReviewHtml(
        WorkflowOnboardingPlan plan,
        IReadOnlyDictionary<string, WorkflowReviewCandidateState> reviewState,
        DateOnly today,
        string? reviewedBy)
    {
        var candidates = plan.Candidates
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var approvedCount = candidates.Count(candidate =>
            reviewState.TryGetValue(candidate.Id, out var state) &&
            state.Status.Equals("approved", StringComparison.OrdinalIgnoreCase));
        var rejectedCount = candidates.Count(candidate =>
            reviewState.TryGetValue(candidate.Id, out var state) &&
            state.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase));
        var approvalRequiredCount = candidates.Count(candidate => candidate.RequiresApproval);

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("  <title>AgentBlazor Workflow Onboarding Review</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { color-scheme: light; --ink: #172033; --muted: #5b6575; --line: #d9dee8; --surface: #f6f8fb; --accent: #0f766e; --warn: #a16207; --danger: #b42318; }");
        sb.AppendLine("    * { box-sizing: border-box; }");
        sb.AppendLine("    body { margin: 0; font-family: Arial, Helvetica, sans-serif; color: var(--ink); background: #ffffff; line-height: 1.45; }");
        sb.AppendLine("    header { padding: 28px 32px 18px; border-bottom: 1px solid var(--line); background: var(--surface); }");
        sb.AppendLine("    main { padding: 24px 32px 40px; max-width: 1180px; }");
        sb.AppendLine("    h1 { margin: 0 0 10px; font-size: 28px; letter-spacing: 0; }");
        sb.AppendLine("    h2 { margin: 30px 0 12px; font-size: 20px; letter-spacing: 0; }");
        sb.AppendLine("    h3 { margin: 0 0 8px; font-size: 17px; letter-spacing: 0; }");
        sb.AppendLine("    p { margin: 0 0 10px; }");
        sb.AppendLine("    code { font-family: Consolas, Monaco, monospace; font-size: 0.95em; }");
        sb.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 12px; }");
        sb.AppendLine("    th, td { padding: 10px 12px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }");
        sb.AppendLine("    th { font-size: 12px; color: var(--muted); text-transform: uppercase; background: var(--surface); }");
        sb.AppendLine("    ul { margin: 8px 0 0; padding-left: 20px; }");
        sb.AppendLine("    .meta { color: var(--muted); }");
        sb.AppendLine("    .metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; margin-top: 18px; max-width: 780px; }");
        sb.AppendLine("    .metric { border: 1px solid var(--line); background: #fff; padding: 14px; border-radius: 6px; }");
        sb.AppendLine("    .metric strong { display: block; font-size: 24px; }");
        sb.AppendLine("    .candidate { border: 1px solid var(--line); border-radius: 6px; padding: 16px; margin: 14px 0; }");
        sb.AppendLine("    .badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 12px; font-weight: 700; background: #e8eef8; color: #24324a; }");
        sb.AppendLine("    .approved { background: #dff3ed; color: var(--accent); }");
        sb.AppendLine("    .rejected { background: #fee4e2; color: var(--danger); }");
        sb.AppendLine("    .proposed { background: #fff3d6; color: var(--warn); }");
        sb.AppendLine("    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 14px; }");
        sb.AppendLine("    @media (max-width: 720px) { header, main { padding-left: 18px; padding-right: 18px; } table { font-size: 14px; } th, td { padding: 8px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<header>");
        sb.AppendLine("  <h1>AgentBlazor Workflow Onboarding Review</h1>");
        sb.AppendLine($"  <p class=\"meta\">Generated {Html(today.ToString("yyyy-MM-dd"))} for {Html(plan.Model.AppName)} ({Html(plan.Model.BlazorHostProject)})</p>");
        sb.AppendLine($"  <p class=\"meta\">Reviewed by: {Html(string.IsNullOrWhiteSpace(reviewedBy) ? "pending" : reviewedBy)}</p>");
        sb.AppendLine("  <div class=\"metrics\">");
        AppendMetric(sb, "Candidates", candidates.Count.ToString());
        AppendMetric(sb, "Approved", approvedCount.ToString());
        AppendMetric(sb, "Rejected", rejectedCount.ToString());
        AppendMetric(sb, "Require Approval", approvalRequiredCount.ToString());
        sb.AppendLine("  </div>");
        sb.AppendLine("</header>");
        sb.AppendLine("<main>");
        sb.AppendLine("  <section>");
        sb.AppendLine("    <h2>App Context</h2>");
        sb.AppendLine($"    <p>{Html(plan.Model.Description)}</p>");
        if (plan.Model.DesiredAgentWorkflows.Count > 0)
        {
            sb.AppendLine("    <p class=\"meta\">Developer-stated agent goals</p>");
            AppendList(sb, plan.Model.DesiredAgentWorkflows);
        }
        if (plan.Model.Corpus.DomainTerms.Count > 0)
        {
            sb.AppendLine($"    <p class=\"meta\">Domain terms: {Html(string.Join(", ", plan.Model.Corpus.DomainTerms.Take(24)))}</p>");
        }
        sb.AppendLine("  </section>");
        sb.AppendLine("  <section>");
        sb.AppendLine("    <h2>Review Queue</h2>");
        sb.AppendLine("    <table>");
        sb.AppendLine("      <thead><tr><th>Status</th><th>Candidate</th><th>Risk</th><th>Methods</th><th>Routes</th></tr></thead>");
        sb.AppendLine("      <tbody>");
        foreach (var candidate in candidates)
        {
            var state = reviewState.TryGetValue(candidate.Id, out var item)
                ? item
                : new WorkflowReviewCandidateState();
            var status = state.Pinned ? $"{state.Status}, pinned" : state.Status;
            var routes = candidate.Routes.Count == 0 ? "-" : string.Join(", ", candidate.Routes);
            sb.AppendLine("        <tr>");
            sb.AppendLine($"          <td><span class=\"badge {HtmlAttribute(state.Status)}\">{Html(status)}</span></td>");
            sb.AppendLine($"          <td><code>{Html(candidate.Id)}</code><br>{Html(candidate.Name)}</td>");
            sb.AppendLine($"          <td>{Html(candidate.Risk)}</td>");
            sb.AppendLine($"          <td>{candidate.Methods.Count}</td>");
            sb.AppendLine($"          <td>{Html(routes)}</td>");
            sb.AppendLine("        </tr>");
        }
        sb.AppendLine("      </tbody>");
        sb.AppendLine("    </table>");
        sb.AppendLine("  </section>");
        sb.AppendLine("  <section>");
        sb.AppendLine("    <h2>Evidence</h2>");
        foreach (var candidate in candidates)
        {
            AppendCandidateHtml(sb, plan, reviewState, candidate);
        }
        sb.AppendLine("  </section>");
        sb.AppendLine("</main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, string label, string value)
    {
        sb.AppendLine("    <div class=\"metric\">");
        sb.AppendLine($"      <strong>{Html(value)}</strong>");
        sb.AppendLine($"      <span>{Html(label)}</span>");
        sb.AppendLine("    </div>");
    }

    private static void AppendCandidateHtml(
        StringBuilder sb,
        WorkflowOnboardingPlan plan,
        IReadOnlyDictionary<string, WorkflowReviewCandidateState> reviewState,
        WorkflowOnboardingCandidate candidate)
    {
        var state = reviewState.TryGetValue(candidate.Id, out var item)
            ? item
            : new WorkflowReviewCandidateState();
        sb.AppendLine("    <article class=\"candidate\">");
        sb.AppendLine($"      <h3>{Html(candidate.Name)} <span class=\"badge {HtmlAttribute(state.Status)}\">{Html(state.Status)}</span></h3>");
        sb.AppendLine($"      <p>{Html(candidate.Description)}</p>");
        sb.AppendLine("      <div class=\"grid\">");
        sb.AppendLine("        <div>");
        sb.AppendLine("          <p class=\"meta\">Methods</p>");
        AppendList(sb, candidate.Methods);
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div>");
        sb.AppendLine("          <p class=\"meta\">Required approvals</p>");
        AppendList(sb, BuildRequiredApprovals(candidate));
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        if (candidate.Evidence.Count > 0)
        {
            sb.AppendLine("      <p class=\"meta\">Static evidence</p>");
            AppendList(sb, candidate.Evidence);
        }

        var files = candidate.RetrievedEvidence
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.FilePath))
            .Select(chunk => NormalizeReviewPath(plan.SolutionRoot, chunk.FilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count > 0)
        {
            sb.AppendLine("      <p class=\"meta\">File references</p>");
            AppendList(sb, files);
        }
        sb.AppendLine("    </article>");
    }

    private static void AppendList(StringBuilder sb, IEnumerable<string> values)
    {
        sb.AppendLine("      <ul>");
        foreach (var value in values)
        {
            sb.AppendLine($"        <li>{Html(value)}</li>");
        }
        sb.AppendLine("      </ul>");
    }

    private static string Html(string value)
        => WebUtility.HtmlEncode(value);

    private static string HtmlAttribute(string value)
        => Html(WorkflowOnboardingPlanner.ToSlug(value));

    private static IReadOnlyList<string> ReviewIdsByStatus(
        IReadOnlyDictionary<string, WorkflowReviewCandidateState> reviewState,
        string status)
        => reviewState
            .Where(pair => pair.Value.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string MarkdownValueOrPending(string? value)
        => string.IsNullOrWhiteSpace(value) ? "_pending_" : value;

    private static string NormalizeReviewPath(string solutionRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (!Path.IsPathRooted(path))
        {
            return path.Replace(Path.DirectorySeparatorChar, '/');
        }

        var fullRoot = Path.GetFullPath(solutionRoot);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Replace(Path.DirectorySeparatorChar, '/');
        }

        return Path.GetRelativePath(fullRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static IReadOnlyDictionary<string, WorkflowReviewCandidateState> ResolveReviewState(
        WorkflowOnboardingPlan plan,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        WorkflowReviewDecisions decisions,
        string reviewPath)
    {
        var existing = ReadExistingReviewState(reviewPath);
        var approvedIds = selected
            .Select(candidate => candidate.Id)
            .Concat(decisions.ApprovedIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return plan.Candidates.ToDictionary(
            candidate => candidate.Id,
            candidate =>
            {
                existing.TryGetValue(candidate.Id, out var current);
                var status = current?.Status ?? "proposed";
                var pinned = current?.Pinned ?? false;

                if (approvedIds.Contains(candidate.Id) || approvedIds.Contains(candidate.Slug))
                {
                    status = "approved";
                }
                else if (decisions.RejectedIds.Contains(candidate.Id) || decisions.RejectedIds.Contains(candidate.Slug))
                {
                    status = "rejected";
                }

                if (decisions.PinnedIds.Contains(candidate.Id) || decisions.PinnedIds.Contains(candidate.Slug))
                {
                    pinned = true;
                }
                else if (decisions.UnpinnedIds.Contains(candidate.Id) || decisions.UnpinnedIds.Contains(candidate.Slug))
                {
                    pinned = false;
                }

                return new WorkflowReviewCandidateState
                {
                    Status = IsKnownReviewStatus(status) ? status : "proposed",
                    Pinned = pinned
                };
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, WorkflowReviewCandidateState> ReadExistingReviewState(string reviewPath)
    {
        var result = new Dictionary<string, WorkflowReviewCandidateState>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(reviewPath))
        {
            return result;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(reviewPath));
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            var id = ReadString(candidate, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var status = ReadString(candidate, "status");
            result[id] = new WorkflowReviewCandidateState
            {
                Status = IsKnownReviewStatus(status) ? status : "proposed",
                Pinned = ReadBool(candidate, "pinned")
            };
        }

        return result;
    }

    private static string? ResolveReviewedBy(string reviewPath, WorkflowReviewDecisions decisions)
    {
        if (!string.IsNullOrWhiteSpace(decisions.ReviewedBy))
        {
            return decisions.ReviewedBy;
        }

        return ReadExistingReviewedBy(reviewPath);
    }

    private static string? ReadExistingReviewedBy(string reviewPath)
    {
        if (!File.Exists(reviewPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(reviewPath));
        if (document.RootElement.TryGetProperty("reviewedBy", out var topLevelReviewer) &&
            topLevelReviewer.ValueKind == JsonValueKind.String)
        {
            var value = topLevelReviewer.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (document.RootElement.TryGetProperty("review", out var review) &&
            review.ValueKind == JsonValueKind.Object &&
            review.TryGetProperty("reviewedBy", out var reviewer) &&
            reviewer.ValueKind == JsonValueKind.String)
        {
            var value = reviewer.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsKnownReviewStatus(string status)
        => status.Equals("proposed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("rejected", StringComparison.OrdinalIgnoreCase);

    private static string BuildIndexJson(IReadOnlyList<WorkflowOnboardingCandidate> selected)
    {
        var payload = new
        {
            schemaVersion = 1,
            generatedBy = "agentblazor scaffold workflows",
            skills = selected.Select(candidate => new
            {
                name = candidate.Slug,
                displayName = candidate.Name,
                description = candidate.Description,
                category = "workflow",
                risk = candidate.Risk,
                platforms = new[] { "blazor" },
                workflowIds = new[] { candidate.Id },
                skillPath = $"{candidate.Slug}/SKILL.md"
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
    }

    private static IReadOnlyList<string> BuildRequiredApprovals(WorkflowOnboardingCandidate candidate)
        => candidate.RequiresApproval
            ? ["analysis artifacts", "skill files", "capability/workflow classes", "Program.cs/service wiring", "validation commands"]
            : ["analysis artifacts", "skill files", "validation commands"];

    private static string EscapeMarkdownTable(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string BuildMetadataJson(IReadOnlyList<WorkflowOnboardingCandidate> selected, DateOnly today, string metadataPath)
    {
        var existing = ReadExistingMetadata(metadataPath);
        var payload = new
        {
            schemaVersion = 1,
            generatedAt = $"{today:yyyy-MM-dd}",
            curator = new
            {
                staleAfterDays = 30,
                archiveAfterAdditionalDays = 60
            },
            skills = selected.Select(candidate => new
            {
                name = candidate.Slug,
                workflowIds = new[] { candidate.Id },
                pinned = existing.TryGetValue(candidate.Slug, out var item) && item.Pinned,
                readCount = item?.ReadCount ?? 0,
                executionCount = item?.ExecutionCount ?? 0,
                lastRead = item?.LastRead,
                lastExecuted = item?.LastExecuted,
                lastReviewed = item?.LastReviewed ?? $"{today:yyyy-MM-dd}",
                state = item?.State == "archived" ? "active" : item?.State ?? "active"
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
    }

    private static Dictionary<string, ExistingSkillMetadata> ReadExistingMetadata(string metadataPath)
    {
        var result = new Dictionary<string, ExistingSkillMetadata>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(metadataPath))
        {
            return result;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        if (!document.RootElement.TryGetProperty("skills", out var skills) ||
            skills.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var skill in skills.EnumerateArray())
        {
            var name = ReadString(skill, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            result[name] = new ExistingSkillMetadata
            {
                Pinned = ReadBool(skill, "pinned"),
                ReadCount = ReadInt(skill, "readCount"),
                ExecutionCount = ReadInt(skill, "executionCount"),
                LastRead = ReadNullableString(skill, "lastRead"),
                LastExecuted = ReadNullableString(skill, "lastExecuted"),
                LastReviewed = ReadNullableString(skill, "lastReviewed"),
                State = ReadNullableString(skill, "state")
            };
        }

        return result;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadNullableString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static string BuildSkill(WorkflowOnboardingCandidate candidate, DateOnly today)
    {
        var approvals = candidate.RequiresApproval
            ? "[\"analysis artifacts\", \"skill files\", \"capability/workflow classes\", \"Program.cs/service wiring\", \"validation commands\"]"
            : "[\"analysis artifacts\", \"skill files\", \"validation commands\"]";
        var workflowIds = string.Join(", ", candidate.Methods.Select(method => $"\"{method}\""));
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {candidate.Slug}");
        sb.AppendLine($"description: {EscapeYaml(candidate.Description)}");
        sb.AppendLine("category: workflow");
        sb.AppendLine($"risk: {EscapeYaml(candidate.Risk)}");
        sb.AppendLine("platforms: [\"blazor\"]");
        sb.AppendLine($"workflowIds: [\"{candidate.Id}\"]");
        sb.AppendLine($"requiredApprovals: {approvals}");
        sb.AppendLine("pinned: false");
        sb.AppendLine($"lastReviewed: {today:yyyy-MM-dd}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {candidate.Name}");
        sb.AppendLine();
        sb.AppendLine(candidate.Description);
        sb.AppendLine();
        sb.AppendLine("## Allowed Workflow");
        sb.AppendLine();
        sb.AppendLine($"- Capability class: `{candidate.CapabilityClassName}`");
        sb.AppendLine($"- Methods: {workflowIds}");
        if (candidate.Routes.Count > 0)
        {
            sb.AppendLine($"- Routes: {string.Join(", ", candidate.Routes.Select(route => $"`{route}`"))}");
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence");
        foreach (var evidence in candidate.Evidence.DefaultIfEmpty("static analysis evidence"))
        {
            sb.AppendLine($"- {evidence}");
        }

        sb.AppendLine();
        sb.AppendLine("## Restrictions");
        sb.AppendLine();
        sb.AppendLine("- Use only the methods listed in this skill unless the developer approves a new workflow scope.");
        sb.AppendLine("- Require approval before mutating state when `risk` is not `safe read-only`.");
        sb.AppendLine("- Prefer `skill_view(name, file_path)` for deep evidence files instead of loading all references by default.");
        return sb.ToString();
    }

    private static string BuildEvidenceReference(WorkflowOnboardingCandidate candidate)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Evidence for {candidate.Name}");
        sb.AppendLine();
        foreach (var chunk in candidate.RetrievedEvidence)
        {
            sb.AppendLine($"## {chunk.Title}");
            sb.AppendLine();
            sb.AppendLine($"- Kind: {chunk.Kind}");
            if (!string.IsNullOrWhiteSpace(chunk.FilePath))
            {
                sb.AppendLine($"- File: `{chunk.FilePath}`");
            }
            if (chunk.RelatedRoutes.Count > 0)
            {
                sb.AppendLine($"- Routes: {string.Join(", ", chunk.RelatedRoutes.Select(route => $"`{route}`"))}");
            }
            sb.AppendLine();
            sb.AppendLine(chunk.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool IsInsideRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeYaml(string value)
        => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

public sealed record WorkflowArtifactChange
{
    public string Path { get; init; } = "";

    public WorkflowArtifactChangeKind ChangeKind { get; init; }

    public string Summary { get; init; } = "";

    public string OriginalContent { get; init; } = "";

    public string UpdatedContent { get; init; } = "";
}

public enum WorkflowArtifactChangeKind
{
    Create,
    Update
}

internal sealed record ExistingSkillMetadata
{
    public bool Pinned { get; init; }

    public int ReadCount { get; init; }

    public int ExecutionCount { get; init; }

    public string? LastRead { get; init; }

    public string? LastExecuted { get; init; }

    public string? LastReviewed { get; init; }

    public string? State { get; init; }
}
