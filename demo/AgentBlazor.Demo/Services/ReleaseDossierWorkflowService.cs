using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Demo.Services;

[AgentCapability(
    "release_dossier",
    Name = "Release Dossier Workflow",
    Description = "Coordinate recipe release readiness and audit evidence into one approval-gated release dossier.",
    Category = "Workflow")]
internal sealed class ReleaseDossierCapabilities(ReleaseDossierWorkflowService workflow)
{
    [AgentAction("Assess release dossier readiness for the current operational scenario", ActionId = "assess_release_dossier_readiness")]
    public Task<CapabilityResult> AssessReleaseDossierReadinessAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.AssessReleaseDossierReadinessAsync(sessionId, cancellationToken);

    [AgentAction("Advance the next guided subsystem stage for the current release dossier scenario", ActionId = "advance_release_dossier_stage")]
    public Task<CapabilityResult> AdvanceNextStageAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.AdvanceNextStageAsync(sessionId, cancellationToken);

    [AgentAction("Prepare the release dossier for the current operational scenario", ActionId = "prepare_release_dossier", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareReleaseDossierAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.PrepareReleaseDossierAsync(sessionId, cancellationToken);

    [AgentAction("Apply the release dossier recovery playbook for the current operational scenario", ActionId = "apply_release_dossier_recovery_playbook")]
    public Task<CapabilityResult> ApplyRecoveryPlaybookAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.ApplyRecoveryPlaybookAsync(sessionId, cancellationToken);

    [AgentAction("Reset the release dossier workflow", ActionId = "reset_release_dossier_workflow")]
    public Task<CapabilityResult> ResetReleaseDossierWorkflowAsync()
    {
        workflow.Reset();
        return Task.FromResult(CapabilityResult.Success("Reset the release dossier workflow."));
    }
}

internal sealed class ReleaseDossierWorkflowService(
    DojoRecipeReleaseWorkflowService recipeWorkflow,
    DemoFileWorkflowService fileWorkflow)
{
    private const string OrchestrationRoute = "/demo/workflows/release-dossier";
    private const string DossierSource = "release-dossier";

    private readonly List<ReleaseDossierJourneyEvent> _journeyEvents = [];

    public event Action? Changed;

    public string? SessionId { get; private set; }

    public ReleaseDossierSnapshot Snapshot { get; private set; } = ReleaseDossierSnapshot.Empty;

    public CapabilityResult? LatestResult { get; private set; }

    public string? LatestNarrative { get; private set; }

    public ReleaseDossierDraft? CurrentDossier { get; private set; }

    public bool IsDossierDialogOpen { get; private set; }

    public IReadOnlyList<string> LatestBlockers { get; private set; } = [];

    public IReadOnlyList<ReleaseDossierJourneyEvent> JourneyEvents => _journeyEvents.ToArray();

    public string GetRecipeWorkflowRoute() => BuildWorkflowRoute("/demo/workflows/recipe-release", GetRecipeRouteFocus());

    public string GetFileWorkflowRoute() => BuildWorkflowRoute("/demo/workflows/file-audit-bundle", GetFileRouteFocus());

    public IReadOnlyList<ReleaseDossierJourneyStep> GetJourneySteps(string? returnedFrom = null)
    {
        var nextRoute = GetNextGuidedWorkflowRoute(returnedFrom);

        return
        [
            new ReleaseDossierJourneyStep(
                "recipe",
                "Recipe release",
                Snapshot.RecipePhase,
                DescribeRecipeStatus(),
                GetRecipeWorkflowRoute(),
                GetJourneyStepStatus(
                    isComplete: !NeedsRecipeSurface(),
                    isBlocked: Snapshot.RecipeBlocked,
                    isCurrent: string.Equals(nextRoute, GetRecipeWorkflowRoute(), StringComparison.OrdinalIgnoreCase))),
            new ReleaseDossierJourneyStep(
                "file",
                "Audit evidence",
                Snapshot.FilePhase,
                DescribeFileStatus(),
                GetFileWorkflowRoute(),
                GetJourneyStepStatus(
                    isComplete: !NeedsFileSurface(),
                    isBlocked: LatestBlockers.Any(static blocker => blocker.Contains("Audit evidence", StringComparison.OrdinalIgnoreCase)),
                    isCurrent: string.Equals(nextRoute, GetFileWorkflowRoute(), StringComparison.OrdinalIgnoreCase))),
            new ReleaseDossierJourneyStep(
                "dossier",
                "Release dossier",
                CurrentDossier is not null ? "Ready for review" : LatestBlockers.Count == 0 && nextRoute is null ? "Ready to prepare" : "Waiting on subsystem alignment",
                DescribeDossierStatus(),
                null,
                GetJourneyStepStatus(
                    isComplete: CurrentDossier is not null,
                    isBlocked: LatestBlockers.Count > 0,
                    isCurrent: CurrentDossier is null && nextRoute is null))
        ];
    }

    public string? GetNextGuidedWorkflowRoute(string? returnedFrom = null)
    {
        var returned = returnedFrom?.Trim().ToLowerInvariant();

        if (NeedsRecipeSurface() && returned is not "recipe")
        {
            return GetRecipeWorkflowRoute();
        }

        if (NeedsFileSurface() && returned is not "file")
        {
            return GetFileWorkflowRoute();
        }

        if (NeedsRecipeSurface())
        {
            return GetRecipeWorkflowRoute();
        }

        if (NeedsFileSurface())
        {
            return GetFileWorkflowRoute();
        }

        return null;
    }

    public string GetGuidedJourneySummary(string? returnedFrom = null, string? returnState = null)
    {
        if (!string.IsNullOrWhiteSpace(returnedFrom))
        {
            var surface = returnedFrom switch
            {
                "recipe" => "recipe release",
                "file" => "audit evidence",
                _ => returnedFrom
            };

            return string.IsNullOrWhiteSpace(returnState)
                ? $"Returned from the {surface} surface."
                : $"Returned from the {surface} surface with state '{returnState}'.";
        }

        if (LatestBlockers.Count > 0)
        {
            return $"The release dossier flow is still blocked: {LatestBlockers[0]}";
        }

        return CurrentDossier is not null
            ? "Recipe and evidence state are aligned, and the release dossier is ready."
            : "Use the guided journey to move through the recipe and evidence surfaces in order.";
    }

    public async Task LoadAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await RefreshSnapshotAsync(cancellationToken);
    }

    public async Task<CapabilityResult> AssessReleaseDossierReadinessAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await recipeWorkflow.AssessReleaseReadinessAsync(sessionId, cancellationToken);
        _ = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Local", cancellationToken);
        await RefreshSnapshotAsync(cancellationToken);

        LatestNarrative = $"Release dossier readiness spans recipe '{Snapshot.RecipeTitle}', {_snapshotFileCount()} evidence file(s), and {Snapshot.VerifiedFileCount} verified token(s).";
        LatestBlockers = BuildBlockers();
        AddJourneyEvent("assessment", "Readiness assessed", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> AdvanceNextStageAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await RefreshSnapshotAsync(cancellationToken);

        var nextStage = GetNextGuidedStageKey();
        if (nextStage is null)
        {
            LatestBlockers = BuildBlockers();
            LatestNarrative = LatestBlockers.Count == 0
                ? "Recipe and evidence stages are aligned. The next step is to prepare the approval-gated release dossier."
                : $"Subsystem blockers still remain before dossier preparation: {LatestBlockers[0]}";
            AddJourneyEvent("dossier", "Subsystem alignment complete", LatestNarrative);
            LatestResult = CapabilityResult.Success(LatestNarrative) with
            {
                Outputs = BuildOutputsWithStage("dossier"),
                Warnings = BuildWarnings(),
                NextActions = BuildNextActions()
            };

            NotifyChanged();
            return LatestResult;
        }

        switch (nextStage)
        {
            case "recipe":
                _ = await recipeWorkflow.AssessReleaseReadinessAsync(SessionId, cancellationToken);
                if (recipeWorkflow.LatestResult?.IsBlocked == true)
                {
                    _ = await recipeWorkflow.ApplyReleaseRecoveryPlaybookAsync(SessionId, cancellationToken);
                }

                _ = await recipeWorkflow.PrepareReleaseDraftAsync(SessionId, cancellationToken);
                break;
            case "file":
            {
                var currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
                _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
                _ = await fileWorkflow.RunRemoteHandoffAsync(SessionId ?? string.Empty, cancellationToken);
                _ = await fileWorkflow.ValidateRemoteTokensAsync(SessionId ?? string.Empty, cancellationToken);
                await RefreshSnapshotAsync(cancellationToken);
                if (NeedsFileSurface())
                {
                    _ = await fileWorkflow.ApplyRecoveryPlaybookAsync(SessionId ?? string.Empty, cancellationToken);
                    currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
                    _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
                    _ = await fileWorkflow.RunRemoteHandoffAsync(SessionId ?? string.Empty, cancellationToken);
                    _ = await fileWorkflow.ValidateRemoteTokensAsync(SessionId ?? string.Empty, cancellationToken);
                }
                break;
            }
        }

        await RefreshSnapshotAsync(cancellationToken);
        LatestBlockers = BuildBlockers();

        var stageTitle = nextStage switch
        {
            "recipe" => "recipe release",
            "file" => "audit evidence",
            _ => "workflow"
        };

        var stillNeedsStage = nextStage switch
        {
            "recipe" => NeedsRecipeSurface(),
            "file" => NeedsFileSurface(),
            _ => false
        };

        LatestNarrative = stillNeedsStage
            ? $"Advanced the {stageTitle} stage, but it still needs attention: {BuildStageBlocker(nextStage)}"
            : $"Advanced the {stageTitle} stage and updated the shared release-dossier state.";
        AddJourneyEvent(nextStage, $"Advanced {stageTitle}", LatestNarrative);
        LatestResult = (stillNeedsStage ? CapabilityResult.Blocked(LatestNarrative) : CapabilityResult.Success(LatestNarrative)) with
        {
            Outputs = BuildOutputsWithStage(nextStage),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> PrepareReleaseDossierAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        _ = await recipeWorkflow.PrepareReleaseDraftAsync(SessionId, cancellationToken);

        var currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
        _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
        _ = await fileWorkflow.RunRemoteHandoffAsync(SessionId ?? string.Empty, cancellationToken);
        _ = await fileWorkflow.ValidateRemoteTokensAsync(SessionId ?? string.Empty, cancellationToken);

        await RefreshSnapshotAsync(cancellationToken);
        LatestBlockers = BuildBlockers();

        if (LatestBlockers.Count > 0)
        {
            CurrentDossier = null;
            IsDossierDialogOpen = false;
            LatestNarrative = $"Release dossier is blocked: {LatestBlockers[0]}";
            AddJourneyEvent("dossier", "Dossier blocked", LatestNarrative);
            LatestResult = CapabilityResult.Blocked(LatestNarrative) with
            {
                Outputs = BuildOutputs(),
                Warnings = BuildWarnings(),
                NextActions = BuildNextActions()
            };

            NotifyChanged();
            return LatestResult;
        }

        CurrentDossier = new ReleaseDossierDraft(
            $"{Snapshot.RecipeTitle} release dossier",
            Snapshot.RecipeTitle,
            Snapshot.FileSnapshot.Files,
            [
                $"Release draft prepared for '{Snapshot.RecipeTitle}'.",
                $"Audit evidence bundle is in {Snapshot.FilePhase} phase with {Snapshot.VerifiedFileCount} verified token(s).",
                $"Release tags include {string.Join(", ", Snapshot.RecipeTags)}."
            ],
            [
                "Review the release draft and checklist.",
                "Confirm the verified evidence bundle is attached.",
                "Approve the release dossier for downstream handoff."
            ]);

        IsDossierDialogOpen = true;
        LatestNarrative = $"Prepared the release dossier for '{Snapshot.RecipeTitle}' with {CurrentDossier.FileNames.Count} evidence file(s).";
        AddJourneyEvent("dossier", "Dossier prepared", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions =
            [
                "Review the release dossier in the dialog.",
                "Approve the release dossier for handoff."
            ]
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> ApplyRecoveryPlaybookAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        _ = await recipeWorkflow.ApplyReleaseRecoveryPlaybookAsync(SessionId, cancellationToken);
        var currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
        _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
        _ = await fileWorkflow.ApplyRecoveryPlaybookAsync(SessionId ?? string.Empty, cancellationToken);

        await RefreshSnapshotAsync(cancellationToken);
        CurrentDossier = null;
        IsDossierDialogOpen = false;
        LatestBlockers = BuildBlockers();
        LatestNarrative = LatestBlockers.Count == 0
            ? "Applied the release dossier recovery playbook. The dossier can now be prepared."
            : $"Applied the release dossier recovery playbook, but blockers remain: {LatestBlockers[0]}";
        AddJourneyEvent("recovery", "Recovery playbook applied", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> ProcessGuidedReturnAsync(
        string? sessionId,
        string? returnedFrom,
        string? returnState,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await RefreshSnapshotAsync(cancellationToken);

        LatestBlockers = BuildBlockers();
        LatestNarrative = BuildReturnNarrative(returnedFrom, returnState);
        AddJourneyEvent(returnedFrom?.Trim().ToLowerInvariant() ?? "return", $"Returned from {returnedFrom ?? "workflow"}", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public void SetDossierDialogVisible(bool visible)
    {
        IsDossierDialogOpen = visible;
        NotifyChanged();
    }

    public void Reset()
    {
        recipeWorkflow.ResetTransientState();
        CurrentDossier = null;
        IsDossierDialogOpen = false;
        LatestNarrative = null;
        LatestResult = null;
        LatestBlockers = [];
        Snapshot = ReleaseDossierSnapshot.Empty;
        _journeyEvents.Clear();
        NotifyChanged();
    }

    private async Task RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        await recipeWorkflow.LoadAsync(SessionId, cancellationToken);
        var fileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Local", cancellationToken);
        var recipeTags = BuildRecipeTags(recipeWorkflow.Snapshot?.Recipe);
        var latestFileJob = fileSnapshot.Jobs.OrderByDescending(job => job.UpdatedUtc).FirstOrDefault();
        var recipeBlocked = recipeWorkflow.LatestResult?.IsBlocked == true
            || (!string.IsNullOrWhiteSpace(recipeWorkflow.LatestAssessment) &&
                recipeWorkflow.LatestAssessment.Contains("blocked", StringComparison.OrdinalIgnoreCase));

        Snapshot = new ReleaseDossierSnapshot(
            recipeWorkflow.Snapshot?.Recipe.Title ?? "No recipe loaded",
            recipeWorkflow.CurrentDraft is not null ? "Draft prepared" : recipeBlocked ? "Recovery required" : !string.IsNullOrWhiteSpace(recipeWorkflow.LatestAssessment) ? "Assessed" : "Idle",
            recipeBlocked,
            recipeTags,
            fileSnapshot,
            latestFileJob?.Status ?? "Idle",
            fileSnapshot.Jobs.Count(job => string.Equals(job.Status, "Verified", StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<string> BuildBlockers()
    {
        var blockers = new List<string>();

        if (Snapshot.RecipeBlocked && !string.IsNullOrWhiteSpace(recipeWorkflow.LatestAssessment))
        {
            blockers.Add(recipeWorkflow.LatestAssessment);
        }

        if (Snapshot.FileSnapshot.UploadMode != "Remote")
        {
            blockers.Add("Audit evidence is still in Local mode.");
        }

        var currentFiles = Snapshot.FileSnapshot.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latestJobsByFile = Snapshot.FileSnapshot.Jobs
            .Where(job => currentFiles.Contains(job.FileName))
            .GroupBy(job => job.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(job => job.UpdatedUtc).First())
            .ToArray();

        if (latestJobsByFile.Any(job => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("Audit evidence bundle still has rejected or failed remote processing.");
        }
        else if (latestJobsByFile.Length > 0 && latestJobsByFile.All(job => !string.Equals(job.Status, "Verified", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("Audit evidence bundle has not been verified yet.");
        }

        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> BuildWarnings()
    {
        var warnings = new List<string>();
        warnings.AddRange(recipeWorkflow.LatestResult?.Warnings ?? []);
        warnings.AddRange(Snapshot.FileSnapshot.Jobs
            .Where(job => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(job => $"{job.FileName}: {job.Message}"));
        return warnings;
    }

    private IReadOnlyList<string> BuildNextActions()
    {
        if (LatestBlockers.Count > 0)
        {
            return
            [
                "Apply the release dossier recovery playbook.",
                "Prepare the release dossier again once the blockers are cleared."
            ];
        }

        if (CurrentDossier is not null)
        {
            return
            [
                "Review the release dossier in the dialog.",
                "Approve the dossier for downstream handoff."
            ];
        }

        return
        [
            "Assess release dossier readiness.",
            "Prepare the release dossier once recipe and evidence state are aligned."
        ];
    }

    private string? GetNextGuidedStageKey()
    {
        if (NeedsRecipeSurface())
        {
            return "recipe";
        }

        if (NeedsFileSurface())
        {
            return "file";
        }

        return null;
    }

    private bool NeedsRecipeSurface() => Snapshot.RecipeBlocked || recipeWorkflow.CurrentDraft is null;

    private bool NeedsFileSurface()
        => !string.Equals(Snapshot.FileSnapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase)
           || Snapshot.VerifiedFileCount == 0;

    private string DescribeRecipeStatus()
    {
        if (Snapshot.RecipeBlocked)
        {
            return recipeWorkflow.LatestAssessment ?? "Recipe release is still blocked.";
        }

        if (recipeWorkflow.CurrentDraft is not null)
        {
            return $"Release draft is ready with {recipeWorkflow.CurrentDraft.ChecklistItems.Count} checklist item(s).";
        }

        return string.IsNullOrWhiteSpace(recipeWorkflow.LatestAssessment)
            ? "Recipe release readiness has not been assessed yet."
            : "Recipe release readiness has been assessed, but the draft still needs to be prepared.";
    }

    private string DescribeFileStatus()
    {
        if (!string.Equals(Snapshot.FileSnapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase))
        {
            return "Evidence files are still staged locally.";
        }

        if (LatestBlockers.Any(static blocker => blocker.Contains("Audit evidence bundle still has rejected or failed remote processing.", StringComparison.OrdinalIgnoreCase)))
        {
            return "Remote processing failed for at least one evidence file.";
        }

        if (Snapshot.VerifiedFileCount == 0)
        {
            return "No remote evidence tokens have been verified yet.";
        }

        return $"{Snapshot.VerifiedFileCount} evidence file(s) are verified for release handoff.";
    }

    private string DescribeDossierStatus()
    {
        if (CurrentDossier is not null)
        {
            return $"Dossier '{CurrentDossier.Title}' is ready for the approval-gated handoff.";
        }

        if (LatestBlockers.Count > 0)
        {
            return $"Dossier preparation is waiting on the subsystem blockers: {LatestBlockers[0]}";
        }

        return "Recipe and evidence state are aligned. The next step is to prepare the approval-gated release dossier.";
    }

    private string BuildReturnNarrative(string? returnedFrom, string? returnState)
    {
        var summary = GetGuidedJourneySummary(returnedFrom, returnState);
        var nextRoute = GetNextGuidedWorkflowRoute(returnedFrom);

        if (CurrentDossier is not null)
        {
            return $"{summary} The release dossier is ready for review.";
        }

        if (nextRoute is null)
        {
            return $"{summary} Recipe and evidence surfaces are aligned. Prepare the release dossier when you are ready to cross the approval boundary.";
        }

        var nextSurface = nextRoute.Contains("/demo/workflows/recipe-release", StringComparison.OrdinalIgnoreCase)
            ? "recipe release"
            : "audit evidence";
        return $"{summary} Next, continue with the {nextSurface} surface.";
    }

    private IReadOnlyDictionary<string, object?> BuildOutputs()
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["recipeTitle"] = Snapshot.RecipeTitle,
            ["recipePhase"] = Snapshot.RecipePhase,
            ["recipeTags"] = Snapshot.RecipeTags.ToArray(),
            ["fileCount"] = _snapshotFileCount(),
            ["filePhase"] = Snapshot.FilePhase,
            ["verifiedFileCount"] = Snapshot.VerifiedFileCount,
            ["blockers"] = LatestBlockers.ToArray()
        };
    }

    private IReadOnlyDictionary<string, object?> BuildOutputsWithStage(string stageKey)
    {
        var outputs = new Dictionary<string, object?>(BuildOutputs(), StringComparer.OrdinalIgnoreCase)
        {
            ["advancedStage"] = stageKey,
            ["nextGuidedStage"] = GetNextGuidedStageKey()
        };

        return outputs;
    }

    private string BuildStageBlocker(string stageKey)
    {
        return stageKey switch
        {
            "recipe" => recipeWorkflow.LatestAssessment ?? "Recipe release still needs attention.",
            "file" => LatestBlockers.FirstOrDefault(static blocker => blocker.Contains("Audit evidence", StringComparison.OrdinalIgnoreCase))
                      ?? "Audit evidence still needs attention.",
            _ => LatestBlockers.FirstOrDefault() ?? "The workflow still needs attention."
        };
    }

    private string GetRecipeRouteFocus()
    {
        if (Snapshot.RecipeBlocked)
        {
            return "recovery";
        }

        return recipeWorkflow.CurrentDraft is not null
            ? "draft"
            : "assessment";
    }

    private string GetFileRouteFocus()
    {
        if (LatestBlockers.Any(static blocker => blocker.Contains("Audit evidence", StringComparison.OrdinalIgnoreCase)))
        {
            return "recovery";
        }

        return Snapshot.VerifiedFileCount > 0
            ? "verification"
            : "handoff";
    }

    private static string BuildWorkflowRoute(string route, string focus)
        => $"{route}?source={Uri.EscapeDataString(DossierSource)}&focus={Uri.EscapeDataString(focus)}&returnTo={Uri.EscapeDataString(OrchestrationRoute)}";

    private static IReadOnlyList<string> BuildRecipeTags(DojoRecipeModel? recipe)
    {
        if (recipe is null)
        {
            return [];
        }

        var tags = new List<string>();
        if (recipe.HighProtein) tags.Add("High protein");
        if (recipe.LowCarb) tags.Add("Low carb");
        if (recipe.Vegetarian) tags.Add("Vegetarian");
        if (recipe.Vegan) tags.Add("Vegan");
        if (recipe.BudgetFriendly) tags.Add("Budget friendly");
        if (recipe.OnePotMeal) tags.Add("One pot");
        if (recipe.Spicy) tags.Add("Spicy");
        return tags.Count == 0 ? ["Everyday cooking"] : tags;
    }

    private static ReleaseDossierJourneyStepStatus GetJourneyStepStatus(bool isComplete, bool isBlocked, bool isCurrent)
    {
        if (isComplete)
        {
            return ReleaseDossierJourneyStepStatus.Complete;
        }

        if (isBlocked)
        {
            return ReleaseDossierJourneyStepStatus.Blocked;
        }

        return isCurrent ? ReleaseDossierJourneyStepStatus.Current : ReleaseDossierJourneyStepStatus.Pending;
    }

    private void AddJourneyEvent(string stageKey, string title, string summary)
    {
        _journeyEvents.Insert(0, new ReleaseDossierJourneyEvent(DateTimeOffset.UtcNow, stageKey, title, summary));
        if (_journeyEvents.Count > 12)
        {
            _journeyEvents.RemoveRange(12, _journeyEvents.Count - 12);
        }
    }

    private int _snapshotFileCount() => Snapshot.FileSnapshot.Files.Count;

    private void NotifyChanged() => Changed?.Invoke();
}

internal sealed record ReleaseDossierSnapshot(
    string RecipeTitle,
    string RecipePhase,
    bool RecipeBlocked,
    IReadOnlyList<string> RecipeTags,
    DemoFileWorkflowSnapshot FileSnapshot,
    string FilePhase,
    int VerifiedFileCount)
{
    public static ReleaseDossierSnapshot Empty { get; } = new(
        "No recipe loaded",
        "Idle",
        false,
        [],
        new DemoFileWorkflowSnapshot([], "Local", [], []),
        "Idle",
        0);
}

internal sealed record ReleaseDossierDraft(
    string Title,
    string RecipeTitle,
    IReadOnlyList<string> FileNames,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> HandoffChecklist);

internal enum ReleaseDossierJourneyStepStatus
{
    Pending,
    Current,
    Blocked,
    Complete
}

internal sealed record ReleaseDossierJourneyStep(
    string Key,
    string Title,
    string Phase,
    string Summary,
    string? Route,
    ReleaseDossierJourneyStepStatus Status);

internal sealed record ReleaseDossierJourneyEvent(
    DateTimeOffset TimestampUtc,
    string StageKey,
    string Title,
    string Summary);
