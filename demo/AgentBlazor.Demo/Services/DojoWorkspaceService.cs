using System.ComponentModel.DataAnnotations;

namespace AgentBlazor.Demo.Services;

internal sealed class DojoWorkspaceService
{
    private readonly object _gate = new();
    private readonly DojoRecipeModel _recipe = new()
    {
        Title = "Classic Scrambled Eggs",
        Minutes = 15,
        Difficulty = "Beginner",
        HighProtein = true,
        LowCarb = true,
        Vegetarian = true
    };

    private readonly List<DojoIngredientRow> _ingredients =
    [
        new("ing-001", "Eggs", "2", false, string.Empty),
        new("ing-002", "Butter", "1 tbsp", false, string.Empty),
        new("ing-003", "Salt", "to taste", false, string.Empty),
        new("ing-004", "Chives", "1 tbsp", true, "optional, chopped")
    ];

    private readonly List<string> _steps =
    [
        "Crack the eggs into a bowl and whisk with salt.",
        "Heat a nonstick pan over medium heat and melt the butter.",
        "Pour in the eggs and stir continuously with a spatula.",
        "Cook until softly set, then remove from heat.",
        "Garnish with chives if desired."
    ];

    private readonly List<DojoRunNote> _runNotes;
    private DateTime? _lastSavedUtc;

    public DojoWorkspaceService()
    {
        var now = DateTime.UtcNow;
        _runNotes =
        [
            new(now.AddMinutes(-6), "Session started with recipe canvas preset."),
            new(now.AddMinutes(-4), "Assistant suggested changing duration from 20 to 15 minutes."),
            new(now.AddMinutes(-2), "Generated summary card for ingredients and preparation steps.")
        ];
    }

    public Task<DojoWorkspaceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(CreateSnapshotUnsafe());
        }
    }

    public Task<DojoWorkspaceSnapshot> SaveRecipeAsync(DojoRecipeModel recipe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _recipe.Title = recipe.Title.Trim();
            _recipe.Minutes = recipe.Minutes;
            _recipe.Difficulty = recipe.Difficulty.Trim();
            _recipe.HighProtein = recipe.HighProtein;
            _recipe.LowCarb = recipe.LowCarb;
            _recipe.Spicy = recipe.Spicy;
            _recipe.Vegetarian = recipe.Vegetarian;

            _lastSavedUtc = DateTime.UtcNow;
            _runNotes.Insert(0, new DojoRunNote(_lastSavedUtc.Value, $"Saved draft for '{_recipe.Title}'."));

            return Task.FromResult(CreateSnapshotUnsafe());
        }
    }

    public Task<DojoWorkspaceSnapshot> AddIngredientAsync(
        DojoIngredientDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var name = draft.Name.Trim();
        var amount = string.IsNullOrWhiteSpace(draft.Amount) ? "n/a" : draft.Amount.Trim();
        var notes = string.IsNullOrWhiteSpace(draft.Notes) ? string.Empty : draft.Notes.Trim();

        lock (_gate)
        {
            var row = new DojoIngredientRow(
                BuildNextIngredientIdUnsafe(),
                name,
                amount,
                draft.Optional,
                notes);

            _ingredients.Add(row);
            _runNotes.Insert(0, new DojoRunNote(DateTime.UtcNow, $"Added ingredient '{row.Name}'."));
            return Task.FromResult(CreateSnapshotUnsafe());
        }
    }

    public Task<DojoWorkspaceSnapshot> AddStepAsync(
        DojoStepDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var text = draft.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return GetSnapshotAsync(cancellationToken);
        }

        lock (_gate)
        {
            _steps.Add(text);
            _runNotes.Insert(0, new DojoRunNote(DateTime.UtcNow, $"Added step {_steps.Count}."));
            return Task.FromResult(CreateSnapshotUnsafe());
        }
    }

    private DojoWorkspaceSnapshot CreateSnapshotUnsafe() =>
        new(
            Recipe: CloneRecipe(_recipe),
            Ingredients: [.. _ingredients],
            Steps: [.. _steps],
            RunNotes: [.. _runNotes],
            LastSavedUtc: _lastSavedUtc);

    private static DojoRecipeModel CloneRecipe(DojoRecipeModel recipe) =>
        new()
        {
            Title = recipe.Title,
            Minutes = recipe.Minutes,
            Difficulty = recipe.Difficulty,
            HighProtein = recipe.HighProtein,
            LowCarb = recipe.LowCarb,
            Spicy = recipe.Spicy,
            Vegetarian = recipe.Vegetarian
        };

    private string BuildNextIngredientIdUnsafe()
    {
        var max = 0;
        foreach (var row in _ingredients)
        {
            if (TryParseIngredientNumber(row.IngredientId, out var parsed))
            {
                max = Math.Max(max, parsed);
            }
        }

        return $"ing-{(max + 1):D3}";
    }

    private static bool TryParseIngredientNumber(string ingredientId, out int number)
    {
        number = 0;
        if (!ingredientId.StartsWith("ing-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(ingredientId[4..], out number);
    }
}

internal sealed record DojoWorkspaceSnapshot(
    DojoRecipeModel Recipe,
    IReadOnlyList<DojoIngredientRow> Ingredients,
    IReadOnlyList<string> Steps,
    IReadOnlyList<DojoRunNote> RunNotes,
    DateTime? LastSavedUtc);

public sealed class DojoRecipeModel
{
    public string Title { get; set; } = string.Empty;

    [Range(5, 180)]
    public int Minutes { get; set; } = 15;

    public string Difficulty { get; set; } = "Beginner";

    public bool HighProtein { get; set; }

    public bool LowCarb { get; set; }

    public bool Spicy { get; set; }

    public bool Vegetarian { get; set; }
}

internal sealed class DojoIngredientDraft
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string Amount { get; set; } = string.Empty;

    public bool Optional { get; set; }

    public string Notes { get; set; } = string.Empty;
}

internal sealed class DojoStepDraft
{
    [Required]
    [StringLength(300)]
    public string Text { get; set; } = string.Empty;
}

internal sealed record DojoIngredientRow(
    string IngredientId,
    string Name,
    string Amount,
    bool Optional,
    string Notes);

internal sealed record DojoRunNote(DateTime TimestampUtc, string Message);
