using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Demo.Services;

[AgentCapability(
    "recipe_release",
    Name = "Recipe Release Workflow",
    Description = "Assess recipe readiness, explain blockers, and prepare a publish-ready release draft from the dojo workspace.",
    Category = "Workflow")]
internal sealed class DojoRecipeReleaseCapabilities(DojoRecipeReleaseWorkflowService workflow)
{
    [AgentAction("Assess the current dojo recipe for release readiness", ActionId = "assess_release_readiness")]
    public Task<CapabilityResult> AssessReleaseReadinessAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.AssessReleaseReadinessAsync(sessionId, cancellationToken);

    [AgentAction("Prepare a publish-ready release draft for the current dojo recipe", ActionId = "prepare_release_draft", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareReleaseDraftAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.PrepareReleaseDraftAsync(sessionId, cancellationToken);

    [AgentAction("Apply the recipe release recovery playbook for the current dojo recipe", ActionId = "apply_release_recovery_playbook")]
    public Task<CapabilityResult> ApplyReleaseRecoveryPlaybookAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.ApplyReleaseRecoveryPlaybookAsync(sessionId, cancellationToken);

    [AgentAction("Reset the recipe release workflow state", ActionId = "reset_release_workflow")]
    public Task<CapabilityResult> ResetReleaseWorkflowAsync()
    {
        workflow.ResetTransientState();
        return Task.FromResult(CapabilityResult.Success("Reset the recipe release workflow state."));
    }
}

internal sealed class DojoRecipeReleaseWorkflowService(DojoWorkspaceService workspace)
{
    private static readonly string[] NonVeganIngredients =
    [
        "butter",
        "cheese",
        "cream",
        "egg",
        "eggs",
        "honey",
        "milk",
        "yogurt"
    ];

    public event Action? Changed;

    public string? SessionId { get; private set; }

    public DojoWorkspaceSnapshot? Snapshot { get; private set; }

    public string? LatestAssessment { get; private set; }

    public CapabilityResult? LatestResult { get; private set; }

    public RecipeReleaseDraft? CurrentDraft { get; private set; }

    public bool IsReleaseDialogOpen { get; private set; }

    public async Task LoadAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        Snapshot = await workspace.GetSnapshotAsync(sessionId, cancellationToken);
        LatestResult = null;
        NotifyChanged();
    }

    public async Task SaveRecipeAsync(DojoRecipeModel recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        Snapshot = await workspace.SaveRecipeAsync(SessionId, recipe, cancellationToken);
        CurrentDraft = null;
        IsReleaseDialogOpen = false;
        LatestAssessment = null;
        LatestResult = null;
        NotifyChanged();
    }

    public async Task<CapabilityResult> AssessReleaseReadinessAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        Snapshot = await workspace.GetSnapshotAsync(sessionId, cancellationToken);

        var evaluation = EvaluateRelease(Snapshot);
        LatestAssessment = evaluation.Summary;
        CurrentDraft = null;
        IsReleaseDialogOpen = false;
        NotifyChanged();

        var result = CapabilityResult.Success(evaluation.Summary) with
        {
            Outputs = BuildOutputs(Snapshot, evaluation),
            Warnings = evaluation.Warnings,
            NextActions = BuildNextActions(evaluation)
        };

        LatestResult = result;
        return result;
    }

    public async Task<CapabilityResult> PrepareReleaseDraftAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        Snapshot = await workspace.GetSnapshotAsync(sessionId, cancellationToken);

        var evaluation = EvaluateRelease(Snapshot);
        LatestAssessment = evaluation.Summary;

        if (evaluation.Blockers.Count > 0)
        {
            CurrentDraft = null;
            IsReleaseDialogOpen = false;
            NotifyChanged();

            var blockedResult = CapabilityResult.Blocked(evaluation.Summary) with
            {
                Outputs = BuildOutputs(Snapshot, evaluation),
                Warnings = evaluation.Warnings,
                NextActions = BuildNextActions(evaluation)
            };

            LatestResult = blockedResult;
            return blockedResult;
        }

        CurrentDraft = BuildReleaseDraft(Snapshot, evaluation);
        IsReleaseDialogOpen = true;
        LatestAssessment = $"Prepared a release draft for '{Snapshot.Recipe.Title}' with {CurrentDraft.ChecklistItems.Count} checklist items.";

        await workspace.AppendRunNoteAsync(
            sessionId,
            $"Prepared a recipe release draft for '{Snapshot.Recipe.Title}'.",
            cancellationToken: cancellationToken);

        NotifyChanged();

        var successResult = CapabilityResult.Success(LatestAssessment) with
        {
            Outputs = new Dictionary<string, object?>(BuildOutputs(Snapshot, evaluation), StringComparer.OrdinalIgnoreCase)
            {
                ["draftTitle"] = CurrentDraft.Title,
                ["draftChecklistCount"] = CurrentDraft.ChecklistItems.Count,
                ["draftTags"] = CurrentDraft.Tags.ToArray()
            },
            Warnings = evaluation.Warnings,
            NextActions =
            [
                "Review the release checklist in the dialog",
                "Adjust recipe flags or ingredients if the release narrative needs refinement"
            ]
        };

        LatestResult = successResult;
        return successResult;
    }

    public async Task<CapabilityResult> ApplyReleaseRecoveryPlaybookAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        Snapshot = await workspace.GetSnapshotAsync(sessionId, cancellationToken);

        var evaluation = EvaluateRelease(Snapshot);
        if (evaluation.Blockers.Count == 0)
        {
            LatestAssessment = $"'{Snapshot.Recipe.Title}' does not need the recovery playbook because no release blockers are active.";
            var noOpResult = CapabilityResult.Success(LatestAssessment) with
            {
                Outputs = BuildOutputs(Snapshot, evaluation),
                Warnings = evaluation.Warnings,
                NextActions =
                [
                    "Prepare the release draft when you are ready to cross the approval boundary."
                ]
            };

            LatestResult = noOpResult;
            NotifyChanged();
            return noOpResult;
        }

        var appliedActions = new List<string>();

        if (Snapshot.Recipe.Vegan)
        {
            var conflictingIngredients = Snapshot.Ingredients
                .Where(ingredient => ContainsNonVeganIngredient(ingredient.Name))
                .Select(static ingredient => ingredient.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (conflictingIngredients.Length > 0)
            {
                Snapshot.Recipe.Vegan = false;
                appliedActions.Add($"Cleared the vegan claim because the recipe still contains {string.Join(", ", conflictingIngredients)}.");
            }
        }

        Snapshot = await workspace.SaveRecipeAsync(sessionId, Snapshot.Recipe, cancellationToken);
        await workspace.AppendRunNoteAsync(
            sessionId,
            appliedActions.Count > 0
                ? $"Applied recipe release recovery playbook: {string.Join(" ", appliedActions)}"
                : "Ran the recipe release recovery playbook, but no automatic metadata repairs were needed.",
            cancellationToken: cancellationToken);

        var refreshedEvaluation = EvaluateRelease(Snapshot);
        LatestAssessment = appliedActions.Count > 0
            ? $"Applied the recipe recovery playbook. {refreshedEvaluation.Summary}"
            : $"Recipe recovery playbook ran, but manual edits are still required. {refreshedEvaluation.Summary}";
        CurrentDraft = null;
        IsReleaseDialogOpen = false;
        NotifyChanged();

        var result = CapabilityResult.Success(LatestAssessment) with
        {
            Outputs = new Dictionary<string, object?>(BuildOutputs(Snapshot, refreshedEvaluation), StringComparer.OrdinalIgnoreCase)
            {
                ["recoveryActions"] = appliedActions.ToArray()
            },
            Warnings = refreshedEvaluation.Warnings,
            NextActions = refreshedEvaluation.Blockers.Count > 0
                ? BuildNextActions(refreshedEvaluation)
                : [
                    "Prepare the release draft now that the automatic blocker has been cleared.",
                    "Review the release checklist in the approval dialog."
                ]
        };

        LatestResult = result;
        return result;
    }

    public void SetReleaseDialogVisible(bool visible)
    {
        IsReleaseDialogOpen = visible;
        NotifyChanged();
    }

    public void ResetTransientState()
    {
        LatestAssessment = null;
        LatestResult = null;
        CurrentDraft = null;
        IsReleaseDialogOpen = false;
        NotifyChanged();
    }

    private static RecipeReleaseDraft BuildReleaseDraft(
        DojoWorkspaceSnapshot snapshot,
        RecipeReleaseEvaluation evaluation)
    {
        var checklistItems = new List<string>
        {
            $"Confirm the release title '{snapshot.Recipe.Title}'.",
            $"Verify {snapshot.Ingredients.Count} ingredient line(s) and {snapshot.Steps.Count} preparation step(s).",
            $"Publish nutrition and cooking tags: {string.Join(", ", evaluation.Tags)}.",
            $"Attach the latest prep summary for a {snapshot.Recipe.Minutes}-minute {snapshot.Recipe.Difficulty.ToLowerInvariant()} recipe."
        };

        if (snapshot.Ingredients.Any(static ingredient => ingredient.Optional))
        {
            checklistItems.Add("Call out optional garnish ingredients separately in the release notes.");
        }

        return new RecipeReleaseDraft(
            $"{snapshot.Recipe.Title} release draft",
            evaluation.Summary,
            checklistItems,
            evaluation.Tags,
            evaluation.Blockers,
            evaluation.Warnings);
    }

    private static RecipeReleaseEvaluation EvaluateRelease(DojoWorkspaceSnapshot snapshot)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        var tags = BuildTags(snapshot.Recipe);

        if (string.IsNullOrWhiteSpace(snapshot.Recipe.Title))
        {
            blockers.Add("Recipe title is missing.");
        }

        if (snapshot.Ingredients.Count < 3)
        {
            blockers.Add("At least three ingredients are required before a release draft can be prepared.");
        }

        if (snapshot.Steps.Count < 3)
        {
            blockers.Add("At least three preparation steps are required before the recipe can be released.");
        }

        if (snapshot.Recipe.Vegan)
        {
            var conflictingIngredients = snapshot.Ingredients
                .Where(ingredient => ContainsNonVeganIngredient(ingredient.Name))
                .Select(static ingredient => ingredient.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (conflictingIngredients.Length > 0)
            {
                blockers.Add($"The recipe is marked vegan but still contains {string.Join(", ", conflictingIngredients)}.");
            }
        }

        if (snapshot.Recipe.Minutes > 30)
        {
            warnings.Add("The cook time is above the usual quick-release threshold.");
        }

        if (snapshot.Steps.Count > 5)
        {
            warnings.Add("The release summary should call out that the recipe has a longer multi-step preparation flow.");
        }

        if (snapshot.Ingredients.Count(static ingredient => ingredient.Optional) > 1)
        {
            warnings.Add("Optional ingredients should be grouped clearly in the release copy.");
        }

        var summary = blockers.Count > 0
            ? $"Release is blocked for '{snapshot.Recipe.Title}': {blockers[0]}"
            : $"'{snapshot.Recipe.Title}' is release-ready with {snapshot.Ingredients.Count} ingredients and {snapshot.Steps.Count} preparation steps.";

        return new RecipeReleaseEvaluation(summary, blockers, warnings, tags);
    }

    private static IReadOnlyDictionary<string, object?> BuildOutputs(
        DojoWorkspaceSnapshot snapshot,
        RecipeReleaseEvaluation evaluation)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["recipeTitle"] = snapshot.Recipe.Title,
            ["minutes"] = snapshot.Recipe.Minutes,
            ["difficulty"] = snapshot.Recipe.Difficulty,
            ["ingredientCount"] = snapshot.Ingredients.Count,
            ["stepCount"] = snapshot.Steps.Count,
            ["vegan"] = snapshot.Recipe.Vegan,
            ["vegetarian"] = snapshot.Recipe.Vegetarian,
            ["budgetFriendly"] = snapshot.Recipe.BudgetFriendly,
            ["onePotMeal"] = snapshot.Recipe.OnePotMeal,
            ["tags"] = evaluation.Tags.ToArray(),
            ["blockers"] = evaluation.Blockers.ToArray(),
            ["warningCount"] = evaluation.Warnings.Count
        };
    }

    private static IReadOnlyList<string> BuildNextActions(RecipeReleaseEvaluation evaluation)
    {
        if (evaluation.Blockers.Count > 0)
        {
            return
            [
                "Fix the conflicting recipe metadata or ingredient list",
                "Re-run the release readiness assessment"
            ];
        }

        return
        [
            "Prepare the release draft",
            "Review the release checklist in the approval dialog"
        ];
    }

    private static IReadOnlyList<string> BuildTags(DojoRecipeModel recipe)
    {
        var tags = new List<string>();

        if (recipe.HighProtein)
        {
            tags.Add("High protein");
        }

        if (recipe.LowCarb)
        {
            tags.Add("Low carb");
        }

        if (recipe.Vegetarian)
        {
            tags.Add("Vegetarian");
        }

        if (recipe.Vegan)
        {
            tags.Add("Vegan");
        }

        if (recipe.BudgetFriendly)
        {
            tags.Add("Budget friendly");
        }

        if (recipe.OnePotMeal)
        {
            tags.Add("One pot");
        }

        if (recipe.Spicy)
        {
            tags.Add("Spicy");
        }

        if (tags.Count == 0)
        {
            tags.Add("Everyday cooking");
        }

        return tags;
    }

    private static bool ContainsNonVeganIngredient(string ingredientName)
    {
        return NonVeganIngredients.Any(keyword =>
            ingredientName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private void NotifyChanged() => Changed?.Invoke();
}

internal sealed record RecipeReleaseDraft(
    string Title,
    string Summary,
    IReadOnlyList<string> ChecklistItems,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

internal sealed record RecipeReleaseEvaluation(
    string Summary,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Tags);
