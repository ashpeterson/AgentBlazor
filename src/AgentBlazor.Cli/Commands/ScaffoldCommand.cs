using System.ComponentModel;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Blazor;
using AgentBlazor.Cli.Analysis.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentBlazor.Cli.Commands;

public sealed class ScaffoldCommand : AsyncCommand<ScaffoldCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to solution/project, or 'workflows' for workflow onboarding")]
        public string? Path { get; init; }

        [CommandArgument(1, "[workflow-path]")]
        [Description("Path to solution/project when using 'scaffold workflows'")]
        public string? WorkflowPath { get; init; }

        [CommandOption("--host <PROJECT>")]
        [Description("Name of the Blazor host project (auto-detected if not specified)")]
        public string? HostProject { get; init; }

        [CommandOption("--dry-run")]
        [Description("Preview the proposed edits without mutating any files")]
        public bool DryRun { get; init; }

        [CommandOption("--approve")]
        [Description("Apply the standard-host scaffold edits")]
        public bool Approve { get; init; }

        [CommandOption("--diff")]
        [Description("Show exact file-level scaffold diffs")]
        public bool Diff { get; init; }

        [CommandOption("--use-local-source <PATH>")]
        [Description("Use local AgentBlazor source projects instead of the AgentBlazor package reference")]
        public string? LocalSourcePath { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("Scaffold provider registration: openai, azure-openai, or ollama")]
        public string? Provider { get; init; }

        [CommandOption("--description <DESCRIPTION>")]
        [Description("Short description of the application for workflow onboarding")]
        public string? Description { get; init; }

        [CommandOption("--agent-goals <GOALS>")]
        [Description("Comma- or semicolon-separated workflows the app agent should help users accomplish")]
        public string? AgentGoals { get; init; }

        [CommandOption("--save-config")]
        [Description("Save workflow onboarding intent to .agentblazorc")]
        public bool SaveConfig { get; init; }

        [CommandOption("--scan-scope <SCOPE>")]
        [Description("Workflow analysis scan scope: references or solution")]
        public string ScanScope { get; init; } = "references";

        [CommandOption("--workflow <IDS>")]
        [Description("Comma-separated workflow candidate ids or slugs to apply in non-interactive mode")]
        public string? WorkflowIds { get; init; }

        [CommandOption("--apply-approved")]
        [Description("Apply workflows already marked approved in .agentblazor/workflow-onboarding.json")]
        public bool ApplyApproved { get; init; }

        [CommandOption("--reject <IDS>")]
        [Description("Comma-separated workflow candidate ids or slugs to mark rejected in the review artifact")]
        public string? RejectWorkflowIds { get; init; }

        [CommandOption("--pin <IDS>")]
        [Description("Comma-separated workflow candidate ids or slugs to pin in the review artifact")]
        public string? PinWorkflowIds { get; init; }

        [CommandOption("--unpin <IDS>")]
        [Description("Comma-separated workflow candidate ids or slugs to unpin in the review artifact")]
        public string? UnpinWorkflowIds { get; init; }

        [CommandOption("--reviewed-by <NAME>")]
        [Description("Reviewer identity to record in workflow onboarding review artifacts")]
        public string? ReviewedBy { get; init; }

        [CommandOption("-y|--non-interactive")]
        [Description("Skip interactive prompts and use defaults")]
        public bool NonInteractive { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            if (string.Equals(settings.Path, "workflows", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteWorkflowScaffoldAsync(settings);
            }

            var path = await CommandPathResolver.ResolvePathAsync(settings.Path, settings.NonInteractive);
            if (path is null)
            {
                AnsiConsole.MarkupLine("[red]No solution or project file found.[/]");
                return 1;
            }

            var localSourcePath = ResolveLocalSourcePath(settings.LocalSourcePath);
            var provider = ScaffoldProviders.ParseOrThrow(settings.Provider);

            var planner = new ExistingAppScaffoldPlanner();
            var plan = await planner.PlanAsync(path, settings.HostProject);

            if (plan.IsBlocked)
            {
                RenderBlockedPlan(plan);
                return 2;
            }

            var applier = new ExistingAppScaffoldApplier();
            var preview = await applier.PreviewAsync(plan, localSourcePath, provider);

            if (!settings.Approve)
            {
                RenderPreview(plan, preview, settings.DryRun, settings.Diff, localSourcePath, provider);
                return 0;
            }

            if (settings.Diff && preview.HasChanges)
            {
                RenderDiffs(preview);
            }

            var result = await applier.ApplyAsync(plan, preview, localSourcePath, provider);
            RenderApplyResult(plan, result, provider);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(ex.FileName ?? ex.Message)}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected error:[/] {Markup.Escape(ex.Message)}");
            if (Environment.GetEnvironmentVariable("AGENTBLAZOR_DEBUG") == "1")
            {
                AnsiConsole.WriteException(ex);
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]Set AGENTBLAZOR_DEBUG=1 for full stack trace.[/]");
            }

            return 1;
        }
    }

    private static async Task<int> ExecuteWorkflowScaffoldAsync(Settings settings)
    {
        var path = await CommandPathResolver.ResolvePathAsync(settings.WorkflowPath, settings.NonInteractive);
        if (path is null)
        {
            AnsiConsole.MarkupLine("[red]No solution or project file found.[/]");
            return 1;
        }

        var provider = ScaffoldProviders.ParseOrThrow(settings.Provider);
        var scanScope = ParseScanScope(settings.ScanScope);
        var solutionRoot = Path.GetDirectoryName(path)!;
        var config = await AgentBlazorConfig.ReadAsync(solutionRoot);
        var hostProject = settings.HostProject ?? config?.HostProject;
        var description = ResolveDescription(settings.Description, config?.Description, settings.NonInteractive);
        var desiredAgentWorkflows = ResolveAgentGoals(settings.AgentGoals, config?.DesiredAgentWorkflows, settings.NonInteractive);
        var effectiveConfig = MergeConfig(config, description, desiredAgentWorkflows);

        ScaffoldPlan baselinePlan = null!;
        WorkflowOnboardingPlan workflowPlan = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Analyzing workflows...", async status =>
            {
                status.Status("Checking baseline install readiness...");
                baselinePlan = await new ExistingAppScaffoldPlanner().PlanAsync(path, hostProject);
                if (baselinePlan.IsBlocked)
                {
                    return;
                }

                status.Status("Building workflow analysis corpus...");
                var analyzer = new BlazorProjectAnalyzer();
                var analysis = await analyzer.AnalyzeAsync(
                    path,
                    baselinePlan.HostProjectName,
                    description,
                    effectiveConfig,
                    includeReadiness: false,
                    scanScope: scanScope);
                workflowPlan = new WorkflowOnboardingPlanner().Plan(analysis.Model, solutionRoot);
            });

        if (settings.SaveConfig)
        {
            await effectiveConfig.WriteAsync(solutionRoot);
            AnsiConsole.MarkupLine("[grey]Saved workflow onboarding intent to .agentblazorc[/]");
        }

        if (baselinePlan.IsBlocked)
        {
            RenderBlockedPlan(baselinePlan);
            return 2;
        }

        var writer = new WorkflowOnboardingArtifactWriter();
        var selected = SelectWorkflowCandidates(workflowPlan, settings, writer);
        var reviewDecisions = BuildReviewDecisions(workflowPlan, selected, settings);
        var hasReviewActions = HasReviewActions(settings);
        if (settings.Approve &&
            settings.ApplyApproved &&
            selected.Count == 0 &&
            string.IsNullOrWhiteSpace(settings.WorkflowIds))
        {
            AnsiConsole.MarkupLine("[red]No approved workflow candidates were found in .agentblazor/workflow-onboarding.json.[/]");
            RenderWorkflowReport(workflowPlan, baselinePlan, provider, selected);
            return 2;
        }

        if (settings.Approve &&
            settings.NonInteractive &&
            string.IsNullOrWhiteSpace(settings.WorkflowIds) &&
            !settings.ApplyApproved &&
            !hasReviewActions)
        {
            AnsiConsole.MarkupLine("[red]Non-interactive workflow scaffold requires --workflow <id-or-slug> with --approve.[/]");
            RenderWorkflowReport(workflowPlan, baselinePlan, provider, selected);
            return 2;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var changes = writer.Preview(workflowPlan, selected, today, reviewDecisions);

        if (!settings.Approve)
        {
            RenderWorkflowPreview(workflowPlan, baselinePlan, provider, selected, changes, settings.Diff);
            return 0;
        }

        if (selected.Count == 0 && !hasReviewActions)
        {
            AnsiConsole.MarkupLine("[yellow]No workflow candidates selected; no artifacts were applied.[/]");
            RenderWorkflowReport(workflowPlan, baselinePlan, provider, selected);
            return 0;
        }

        reviewDecisions = reviewDecisions with
        {
            ReviewedBy = ResolveReviewedByForApply(settings, reviewDecisions)
        };
        changes = writer.Preview(workflowPlan, selected, today, reviewDecisions);
        if (changes.Count == 0)
        {
            RenderWorkflowApplyResult(workflowPlan, baselinePlan, provider, selected, changes);
            return 0;
        }

        if (settings.Diff && changes.Count > 0)
        {
            RenderWorkflowDiffs(changes);
        }

        var agentLoop = new AgentLoop(workflowPlan.SolutionRoot);
        var proposal = agentLoop.ProposePatch(
            "Apply AgentBlazor workflow onboarding artifacts",
            changes.Select(ToAgentChange).ToList());
        _ = agentLoop.ToPreview(proposal);
        var applyResult = await agentLoop.ApplyApprovedPatchAsync(proposal.Id, reviewDecisions.ReviewedBy!);
        var audit = await agentLoop.WriteAuditAsync(
            "workflow-onboarding",
            reviewDecisions.ReviewedBy!,
            proposal,
            applyResult,
            DateTimeOffset.UtcNow,
            BuildWorkflowAuditMetadata(selected, reviewDecisions));
        RenderWorkflowApplyResult(workflowPlan, baselinePlan, provider, selected, changes, audit);
        return 0;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildWorkflowAuditMetadata(
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        WorkflowReviewDecisions decisions)
        => new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["selectedWorkflowIds"] = selected
                .Select(candidate => candidate.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ["approvedWorkflowIds"] = decisions.ApprovedIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ["rejectedWorkflowIds"] = decisions.RejectedIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ["pinnedWorkflowIds"] = decisions.PinnedIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ["unpinnedWorkflowIds"] = decisions.UnpinnedIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

    private static AgentProposedFileChange ToAgentChange(WorkflowArtifactChange change)
        => new()
        {
            Path = change.Path,
            ChangeKind = change.ChangeKind == WorkflowArtifactChangeKind.Create
                ? AgentPatchChangeKind.Create
                : AgentPatchChangeKind.Update,
            OriginalContent = change.OriginalContent,
            UpdatedContent = change.UpdatedContent
        };

    private static string ResolveReviewedByForApply(Settings settings, WorkflowReviewDecisions decisions)
    {
        if (!string.IsNullOrWhiteSpace(decisions.ReviewedBy))
        {
            return decisions.ReviewedBy;
        }

        if (settings.NonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            throw new InvalidOperationException("Workflow artifact application requires --reviewed-by <name> for the audit record.");
        }

        return AnsiConsole.Ask<string>("Reviewer identity for workflow artifact audit:");
    }

    private static IReadOnlyList<WorkflowOnboardingCandidate> SelectWorkflowCandidates(
        WorkflowOnboardingPlan plan,
        Settings settings,
        WorkflowOnboardingArtifactWriter writer)
    {
        if (!string.IsNullOrWhiteSpace(settings.WorkflowIds) || settings.ApplyApproved)
        {
            var requested = ParseCandidateIds(settings.WorkflowIds);
            if (settings.ApplyApproved)
            {
                requested.UnionWith(writer.ReadApprovedCandidateIds(plan.AgentBlazorDirectory));
            }

            var selected = plan.Candidates
                .Where(candidate => requested.Contains(candidate.Id) || requested.Contains(candidate.Slug))
                .ToList();
            if (!string.IsNullOrWhiteSpace(settings.WorkflowIds))
            {
                var explicitIds = ParseCandidateIds(settings.WorkflowIds);
                var missing = explicitIds
                    .Where(id => selected.All(candidate =>
                        !candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                        !candidate.Slug.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException($"Unknown workflow candidate(s): {string.Join(", ", missing)}");
                }
            }

            return selected;
        }

        if (!settings.Approve || settings.NonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return [];
        }

        if (plan.Candidates.Count == 0)
        {
            return [];
        }

        return AnsiConsole.Prompt(
            new MultiSelectionPrompt<WorkflowOnboardingCandidate>()
                .Title("Select workflows to scaffold")
                .NotRequired()
                .UseConverter(candidate => $"{candidate.Name} [{candidate.Id}]")
                .AddChoices(plan.Candidates));
    }

    private static WorkflowReviewDecisions BuildReviewDecisions(
        WorkflowOnboardingPlan plan,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        Settings settings)
    {
        var approvedIds = selected.Select(candidate => candidate.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rejectedIds = ParseCandidateIds(settings.RejectWorkflowIds);
        var pinnedIds = ParseCandidateIds(settings.PinWorkflowIds);
        var unpinnedIds = ParseCandidateIds(settings.UnpinWorkflowIds);
        ValidateCandidateIds(plan, rejectedIds, "--reject");
        ValidateCandidateIds(plan, pinnedIds, "--pin");
        ValidateCandidateIds(plan, unpinnedIds, "--unpin");

        var conflictingStatusIds = approvedIds
            .Intersect(rejectedIds, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (conflictingStatusIds.Count > 0)
        {
            throw new InvalidOperationException($"Workflow candidates cannot be both approved and rejected: {string.Join(", ", conflictingStatusIds)}");
        }

        var conflictingPinIds = pinnedIds
            .Intersect(unpinnedIds, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (conflictingPinIds.Count > 0)
        {
            throw new InvalidOperationException($"Workflow candidates cannot be both pinned and unpinned: {string.Join(", ", conflictingPinIds)}");
        }

        return new WorkflowReviewDecisions
        {
            ApprovedIds = approvedIds,
            RejectedIds = rejectedIds,
            PinnedIds = pinnedIds,
            UnpinnedIds = unpinnedIds,
            ReviewedBy = string.IsNullOrWhiteSpace(settings.ReviewedBy)
                ? null
                : settings.ReviewedBy.Trim()
        };
    }

    private static bool HasReviewActions(Settings settings)
        => !string.IsNullOrWhiteSpace(settings.RejectWorkflowIds) ||
            !string.IsNullOrWhiteSpace(settings.PinWorkflowIds) ||
            !string.IsNullOrWhiteSpace(settings.UnpinWorkflowIds);

    private static HashSet<string> ParseCandidateIds(string? ids)
        => string.IsNullOrWhiteSpace(ids)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void ValidateCandidateIds(
        WorkflowOnboardingPlan plan,
        HashSet<string> requestedIds,
        string optionName)
    {
        if (requestedIds.Count == 0)
        {
            return;
        }

        var known = plan.Candidates
            .SelectMany(candidate => new[] { candidate.Id, candidate.Slug })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requestedIds
            .Where(id => !known.Contains(id))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Unknown workflow candidate(s) for {optionName}: {string.Join(", ", missing)}");
        }
    }

    private static void RenderWorkflowReport(
        WorkflowOnboardingPlan workflowPlan,
        ScaffoldPlan baselinePlan,
        ScaffoldProvider? provider,
        IReadOnlyList<WorkflowOnboardingCandidate> selected)
    {
        AnsiConsole.Write(new Rule("[blue]AgentBlazor Workflow Onboarding[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(workflowPlan.Model.BlazorHostProject)}");
        AnsiConsole.MarkupLine($"[blue]Workflow candidates:[/] {workflowPlan.Candidates.Count}");
        AnsiConsole.MarkupLine($"[blue]Selected workflows:[/] {selected.Count}");
        AnsiConsole.MarkupLine($"[blue]Baseline scaffold items:[/] {baselinePlan.Items.Count}");
        if (provider is { } providerChoice)
        {
            AnsiConsole.MarkupLine($"[blue]Provider:[/] {Markup.Escape(providerChoice.ToDisplayName())}");
        }
    }

    private static void RenderWorkflowPreview(
        WorkflowOnboardingPlan workflowPlan,
        ScaffoldPlan baselinePlan,
        ScaffoldProvider? provider,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        IReadOnlyList<WorkflowArtifactChange> changes,
        bool showDiff)
    {
        RenderWorkflowReport(workflowPlan, baselinePlan, provider, selected);
        AnsiConsole.WriteLine();
        if (baselinePlan.Items.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Baseline wiring:[/] not applied by `scaffold workflows`; run baseline `agentblazor scaffold --approve` separately when desired.");
        }

        if (workflowPlan.Candidates.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No workflow candidates found.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Candidate");
        table.AddColumn("Risk");
        table.AddColumn("Methods");
        table.AddColumn("Evidence");
        foreach (var candidate in workflowPlan.Candidates)
        {
            table.AddRow(
                $"{Markup.Escape(candidate.Name)}\n[grey]{Markup.Escape(candidate.Id)}[/]",
                Markup.Escape(candidate.Risk),
                Markup.Escape(string.Join(", ", candidate.Methods.Take(5))),
                Markup.Escape(string.Join("; ", candidate.Evidence.Take(2))));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        if (changes.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Proposed workflow review/artifact changes:[/] {changes.Count}");
            foreach (var change in changes)
            {
                AnsiConsole.MarkupLine($"{GetWorkflowChangeKindMarkup(change.ChangeKind)} {Markup.Escape(change.Path)} [grey]{Markup.Escape(change.Summary)}[/]");
            }
        }
        if (selected.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No workflows selected for SOUL/skill generation. Use --workflow <id-or-slug> --approve, or approve interactively.[/]");
        }

        if (showDiff && changes.Count > 0)
        {
            AnsiConsole.WriteLine();
            RenderWorkflowDiffs(changes);
        }
    }

    private static void RenderWorkflowApplyResult(
        WorkflowOnboardingPlan workflowPlan,
        ScaffoldPlan baselinePlan,
        ScaffoldProvider? provider,
        IReadOnlyList<WorkflowOnboardingCandidate> selected,
        IReadOnlyList<WorkflowArtifactChange> changes,
        AgentAuditRecord? audit = null)
    {
        RenderWorkflowReport(workflowPlan, baselinePlan, provider, selected);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Workflow artifacts applied:[/] {changes.Count}");
        foreach (var change in changes)
        {
            AnsiConsole.MarkupLine($"{GetWorkflowChangeKindMarkup(change.ChangeKind)} {Markup.Escape(change.Path)} [grey]{Markup.Escape(change.Summary)}[/]");
        }
        if (audit is not null)
        {
            AnsiConsole.MarkupLine($"[grey]Audit:[/] {Markup.Escape(audit.Path)}");
        }
    }

    private static void RenderPreview(
        ScaffoldPlan plan,
        ScaffoldPreviewResult preview,
        bool dryRun,
        bool showDiff,
        string? localSourcePath,
        ScaffoldProvider? provider)
    {
        AnsiConsole.Write(new Rule("[blue]AgentBlazor Scaffold Preview[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(plan.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(plan.Readiness.UiProjectPath!)}");
        }
        if (!string.IsNullOrWhiteSpace(localSourcePath))
        {
            AnsiConsole.MarkupLine($"[blue]Local source:[/] {Markup.Escape(localSourcePath)}");
        }
        if (provider is { } providerChoice)
        {
            AnsiConsole.MarkupLine($"[blue]Provider:[/] {Markup.Escape(providerChoice.ToDisplayName())}");
        }
        if (plan.Readiness.HostShape.Kind == HostShapeKind.AdvancedReview && !string.IsNullOrWhiteSpace(plan.BlockReason))
        {
            AnsiConsole.MarkupLine($"[yellow]Advanced host:[/] {Markup.Escape(plan.BlockReason)}");
        }
        if (dryRun)
        {
            AnsiConsole.MarkupLine("[grey]--dry-run supplied.[/]");
        }
        if (showDiff)
        {
            AnsiConsole.MarkupLine("[grey]--diff supplied.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Preview mode is the default. Add --approve to apply these edits.[/]");
        }

        AnsiConsole.WriteLine();

        if (!preview.HasChanges && !plan.Items.Any(item => item.Action == ScaffoldPlanAction.ManualReview))
        {
            AnsiConsole.MarkupLine("[green]No scaffold changes proposed.[/] The app already has the baseline AgentBlazor wiring.");
            return;
        }

        if (preview.HasChanges)
        {
            var table = new Table().RoundedBorder();
            table.AddColumn("File");
            table.AddColumn("Change");

            foreach (var change in preview.Changes)
            {
                table.AddRow(
                    Markup.Escape(change.Path),
                    $"{GetChangeKindMarkup(change.ChangeKind)} {Markup.Escape(change.Summary)}");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]Proposed file changes:[/] {preview.ChangedFileCount}");
        }

        RenderManualReviewItems(plan);
        if (provider is { } selectedProvider && WillScaffoldProvider(plan))
        {
            AnsiConsole.MarkupLine($"[green]Provider template:[/] {Markup.Escape(selectedProvider.ToDisplayName())} registration will be scaffolded into Program.cs.");
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProvider.GetConfigurationHint())}");
        }
        else if (provider is { } selectedProviderForManualReview)
        {
            AnsiConsole.MarkupLine($"[yellow]Provider selected:[/] {Markup.Escape(selectedProviderForManualReview.ToDisplayName())} was captured, but startup wiring is still manual review for this host.");
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProviderForManualReview.GetConfigurationHint())}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Needs your decision:[/] add `--provider openai` (recommended), `--provider azure-openai`, or `--provider ollama` for concrete runtime registration.");
        }
        AnsiConsole.MarkupLine($"[grey]Preview command:[/] {BuildCommand(plan.InputPath, plan.HostProjectName, "--diff", provider)}");
        AnsiConsole.MarkupLine($"[grey]Approve command:[/] {BuildCommand(plan.InputPath, plan.HostProjectName, "--approve", provider)}");
        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");

        if (showDiff)
        {
            AnsiConsole.WriteLine();
            RenderDiffs(preview);
        }
    }

    private static string GetChangeKindMarkup(ScaffoldPreviewChangeKind changeKind) =>
        changeKind switch
        {
            ScaffoldPreviewChangeKind.Create => "[green]CREATE[/]",
            ScaffoldPreviewChangeKind.Update => "[yellow]UPDATE[/]",
            _ => "[grey]?[/]"
        };

    private static bool WillScaffoldProvider(ScaffoldPlan plan)
        => plan.Items.Any(item =>
            item.Id == "agentblazor-services" &&
            item.Action != ScaffoldPlanAction.ManualReview);

    private static void RenderBlockedPlan(ScaffoldPlan plan)
    {
        AnsiConsole.Write(new Rule("[yellow]AgentBlazor Scaffold Blocked[/]").RuleStyle("yellow"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(plan.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(plan.Readiness.UiProjectPath!)}");
        }
        AnsiConsole.WriteLine();

        if (!string.IsNullOrWhiteSpace(plan.BlockTitle))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(plan.BlockTitle)}:[/] {Markup.Escape(plan.BlockReason ?? string.Empty)}");
        }

        if (!string.IsNullOrWhiteSpace(plan.BlockSuggestedFix))
        {
            AnsiConsole.MarkupLine($"[grey]Next step:[/] {Markup.Escape(plan.BlockSuggestedFix!)}");
        }

        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");
    }

    private static void RenderManualReviewItems(ScaffoldPlan plan)
    {
        var reviewItems = plan.Items
            .Where(item => item.Action == ScaffoldPlanAction.ManualReview)
            .ToArray();

        if (reviewItems.Length == 0)
        {
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Review");
        table.AddColumn("Target");
        table.AddColumn("Reason");
        table.AddColumn("Guidance");

        foreach (var item in reviewItems)
        {
            table.AddRow(
                Markup.Escape(item.Summary),
                Markup.Escape(item.TargetPath),
                Markup.Escape(item.Reason),
                Markup.Escape(string.IsNullOrWhiteSpace(item.Guidance) ? "-" : item.Guidance));
        }

        AnsiConsole.MarkupLine($"[yellow]Manual review items:[/] {reviewItems.Length}");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        foreach (var item in reviewItems.Where(item => !string.IsNullOrWhiteSpace(item.Guidance)))
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(item.Summary)}[/]");
            AnsiConsole.MarkupLine($"[grey]Guidance:[/] {Markup.Escape(item.Guidance!)}");
        }

        if (reviewItems.Any(item => !string.IsNullOrWhiteSpace(item.Guidance)))
        {
            AnsiConsole.WriteLine();
        }
    }

    private static void RenderApplyResult(ScaffoldPlan plan, ScaffoldApplyResult result, ScaffoldProvider? provider)
    {
        AnsiConsole.Write(new Rule("[green]AgentBlazor Scaffold Applied[/]").RuleStyle("green"));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(plan.Readiness.UiProjectPath!)}");
        }
        if (provider is { } providerChoice)
        {
            AnsiConsole.MarkupLine($"[blue]Provider:[/] {Markup.Escape(providerChoice.ToDisplayName())}");
        }
        AnsiConsole.WriteLine();

        if (result.Changes.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No file changes were needed.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("File");
        table.AddColumn("Change");

        foreach (var change in result.Changes)
        {
            table.AddRow(
                Markup.Escape(change.Path),
                $"{GetChangeKindMarkup(change.ChangeKind)} {Markup.Escape(change.Summary)}");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Updated files:[/] {result.ChangedFileCount}");
        if (!string.IsNullOrWhiteSpace(result.ManifestPath))
        {
            AnsiConsole.MarkupLine($"[green]Manifest:[/] {Markup.Escape(result.ManifestPath)}");
        }
        RenderManualReviewItems(plan);
        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");
        if (provider is { } selectedProvider && WillScaffoldProvider(plan))
        {
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProvider.GetConfigurationHint())}");
        }
        else if (provider is { } selectedProviderForManualReview)
        {
            AnsiConsole.MarkupLine($"[grey]Provider note:[/] startup wiring was not auto-applied for this host. Use {Markup.Escape(selectedProviderForManualReview.ToDisplayName())} when completing the manual service registration step.");
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProviderForManualReview.GetConfigurationHint())}");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Human step:[/] choose a provider with `--provider openai` (recommended), `--provider azure-openai`, or `--provider ollama` and rerun scaffold preview if you want concrete Program.cs registration.");
        }
    }

    private static void RenderDiffs(ScaffoldPreviewResult preview)
    {
        AnsiConsole.Write(new Rule("[blue]Scaffold Diff[/]").RuleStyle("blue"));

        foreach (var change in preview.Changes)
        {
            AnsiConsole.MarkupLine($"[blue]{Markup.Escape(change.Path)}[/] [grey]({change.ChangeKind})[/]");
            foreach (var line in BuildDiffLines(change.OriginalContent, change.UpdatedContent))
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Removed:
                        AnsiConsole.MarkupLine($"[red]- {Markup.Escape(line.Text)}[/]");
                        break;
                    case DiffLineKind.Added:
                        AnsiConsole.MarkupLine($"[green]+ {Markup.Escape(line.Text)}[/]");
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(line.Text)}[/]");
                        break;
                }
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void RenderWorkflowDiffs(IReadOnlyList<WorkflowArtifactChange> changes)
    {
        AnsiConsole.Write(new Rule("[blue]Workflow Artifact Diff[/]").RuleStyle("blue"));

        foreach (var change in changes)
        {
            AnsiConsole.MarkupLine($"[blue]{Markup.Escape(change.Path)}[/] [grey]({change.ChangeKind})[/]");
            foreach (var line in BuildDiffLines(change.OriginalContent, change.UpdatedContent))
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Removed:
                        AnsiConsole.MarkupLine($"[red]- {Markup.Escape(line.Text)}[/]");
                        break;
                    case DiffLineKind.Added:
                        AnsiConsole.MarkupLine($"[green]+ {Markup.Escape(line.Text)}[/]");
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(line.Text)}[/]");
                        break;
                }
            }

            AnsiConsole.WriteLine();
        }
    }

    private static string GetWorkflowChangeKindMarkup(WorkflowArtifactChangeKind changeKind) =>
        changeKind switch
        {
            WorkflowArtifactChangeKind.Create => "[green]CREATE[/]",
            WorkflowArtifactChangeKind.Update => "[yellow]UPDATE[/]",
            _ => "[grey]?[/]"
        };

    private static IReadOnlyList<DiffLine> BuildDiffLines(string original, string updated)
    {
        var oldLines = NormalizeLines(original);
        var newLines = NormalizeLines(updated);
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];

        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var diff = new List<DiffLine>();
        var oldIndex = 0;
        var newIndex = 0;

        while (oldIndex < oldLines.Length && newIndex < newLines.Length)
        {
            if (string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal))
            {
                diff.Add(new DiffLine(DiffLineKind.Context, oldLines[oldIndex]));
                oldIndex++;
                newIndex++;
                continue;
            }

            if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
            {
                diff.Add(new DiffLine(DiffLineKind.Removed, oldLines[oldIndex]));
                oldIndex++;
            }
            else
            {
                diff.Add(new DiffLine(DiffLineKind.Added, newLines[newIndex]));
                newIndex++;
            }
        }

        while (oldIndex < oldLines.Length)
        {
            diff.Add(new DiffLine(DiffLineKind.Removed, oldLines[oldIndex]));
            oldIndex++;
        }

        while (newIndex < newLines.Length)
        {
            diff.Add(new DiffLine(DiffLineKind.Added, newLines[newIndex]));
            newIndex++;
        }

        return diff;
    }

    private static string[] NormalizeLines(string content)
        => string.IsNullOrEmpty(content)
            ? []
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private enum DiffLineKind
    {
        Context,
        Removed,
        Added
    }

    private sealed record DiffLine(DiffLineKind Kind, string Text);

    private static string? ResolveLocalSourcePath(string? localSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(localSourcePath))
        {
            return Path.GetFullPath(localSourcePath);
        }

        return TryFindAgentBlazorSourceRoot(AppContext.BaseDirectory);
    }

    private static string? TryFindAgentBlazorSourceRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AgentBlazor.Components", "AgentBlazor.Components.csproj");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string BuildCommand(
        string inputPath,
        string hostProjectName,
        string trailingFlag,
        ScaffoldProvider? provider)
    {
        var providerSegment = provider is null
            ? string.Empty
            : $" --provider {provider.Value.ToOptionValue()}";

        return $"`agentblazor scaffold {QuoteIfNeeded(inputPath)} --host {QuoteIfNeeded(hostProjectName)}{providerSegment} {trailingFlag}`";
    }

    private static string BuildDoctorCommand(string inputPath, string hostProjectName)
        => $"`agentblazor doctor {QuoteIfNeeded(inputPath)} --host {QuoteIfNeeded(hostProjectName)}`";

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static AnalysisScanScope ParseScanScope(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "references" or "reference" or "refs" => AnalysisScanScope.References,
            "solution" or "all" => AnalysisScanScope.Solution,
            _ => throw new InvalidOperationException(
                $"Unsupported scan scope '{value}'. Use 'references' or 'solution'.")
        };

    private static string ResolveDescription(string? explicitDescription, string? configuredDescription, bool nonInteractive)
    {
        var description = explicitDescription ?? configuredDescription;
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        if (nonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return "A Blazor application";
        }

        return AnsiConsole.Ask(
            "[yellow]Describe this app in 1-3 sentences. Include the domain, primary users, and work they do here:[/]",
            "A Blazor application");
    }

    private static IReadOnlyList<string> ResolveAgentGoals(
        string? explicitGoals,
        IReadOnlyList<string>? configuredGoals,
        bool nonInteractive)
    {
        var goals = ParseAgentGoals(explicitGoals);
        if (goals.Count > 0)
        {
            return goals;
        }

        if (configuredGoals is { Count: > 0 })
        {
            return configuredGoals
                .Where(goal => !string.IsNullOrWhiteSpace(goal))
                .Select(goal => goal.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (nonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return [];
        }

        var answer = AnsiConsole.Prompt(
            new TextPrompt<string>("[yellow]What workflows should the app agent help users accomplish?[/] [grey](optional; comma-separated)[/]")
                .AllowEmpty());
        return ParseAgentGoals(answer);
    }

    private static IReadOnlyList<string> ParseAgentGoals(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(goal => !string.IsNullOrWhiteSpace(goal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static AgentBlazorConfig MergeConfig(
        AgentBlazorConfig? config,
        string description,
        IReadOnlyList<string> desiredAgentWorkflows)
        => new()
        {
            HostProject = config?.HostProject,
            Description = description,
            DesiredAgentWorkflows = desiredAgentWorkflows.Count == 0 ? null : desiredAgentWorkflows.ToList(),
            WatchDebounceMs = config?.WatchDebounceMs,
            AdditionalServiceSuffixes = config?.AdditionalServiceSuffixes,
            AdditionalDomainVerbs = config?.AdditionalDomainVerbs,
            ExcludeMethodPatterns = config?.ExcludeMethodPatterns,
            ExcludeServicePatterns = config?.ExcludeServicePatterns,
            ExcludeDirectories = config?.ExcludeDirectories,
            AutoUpdateOnBuild = config?.AutoUpdateOnBuild,
            AnalyzeProvider = config?.AnalyzeProvider,
            AnalyzeModel = config?.AnalyzeModel
        };
}
