using System.ComponentModel.DataAnnotations;
using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

internal sealed class DojoWorkspaceService(IDbContextFactory<DemoWorkflowDbContext> dbContextFactory)
{
    private const string DefaultSessionKey = "dojo-default";

    public async Task<DojoWorkspaceSnapshot> GetSnapshotAsync(string? sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessionKey = NormalizeSessionKey(sessionId);

        await EnsureWorkspaceInitializedAsync(db, sessionKey, cancellationToken);
        return await BuildSnapshotAsync(db, sessionKey, cancellationToken);
    }

    public async Task<DojoWorkspaceSnapshot> SaveRecipeAsync(
        string? sessionId,
        DojoRecipeModel recipe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        cancellationToken.ThrowIfCancellationRequested();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessionKey = NormalizeSessionKey(sessionId);

        await EnsureWorkspaceInitializedAsync(db, sessionKey, cancellationToken);

        var workspace = await db.DojoWorkspaces
            .SingleAsync(x => x.SessionKey == sessionKey, cancellationToken);

        var title = string.IsNullOrWhiteSpace(recipe.Title)
            ? workspace.Title
            : recipe.Title.Trim();
        var difficulty = string.IsNullOrWhiteSpace(recipe.Difficulty)
            ? workspace.Difficulty
            : recipe.Difficulty.Trim();

        workspace.Title = title;
        workspace.Minutes = recipe.Minutes;
        workspace.Difficulty = difficulty;
        workspace.HighProtein = recipe.HighProtein;
        workspace.LowCarb = recipe.LowCarb;
        workspace.Spicy = recipe.Spicy;
        workspace.Vegetarian = recipe.Vegetarian;
        workspace.LastSavedUtc = DateTime.UtcNow;
        workspace.UpdatedUtc = DateTime.UtcNow;

        db.DojoRunNotes.Add(new DojoRunNoteEntity
        {
            SessionKey = sessionKey,
            TimestampUtc = workspace.LastSavedUtc.Value,
            Message = $"Saved draft for '{workspace.Title}'."
        });

        await db.SaveChangesAsync(cancellationToken);
        return await BuildSnapshotAsync(db, sessionKey, cancellationToken);
    }

    public async Task<DojoWorkspaceSnapshot> AddIngredientAsync(
        string? sessionId,
        DojoIngredientDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var name = draft.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return await GetSnapshotAsync(sessionId, cancellationToken);
        }

        var amount = string.IsNullOrWhiteSpace(draft.Amount) ? "n/a" : draft.Amount.Trim();
        var notes = string.IsNullOrWhiteSpace(draft.Notes) ? string.Empty : draft.Notes.Trim();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessionKey = NormalizeSessionKey(sessionId);

        await EnsureWorkspaceInitializedAsync(db, sessionKey, cancellationToken);

        var ingredientId = await BuildNextIngredientIdAsync(db, sessionKey, cancellationToken);
        var sortOrder = await db.DojoIngredients
            .Where(x => x.SessionKey == sessionKey)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;

        db.DojoIngredients.Add(new DojoIngredientEntity
        {
            SessionKey = sessionKey,
            IngredientId = ingredientId,
            Name = name,
            Amount = amount,
            Optional = draft.Optional,
            Notes = notes,
            SortOrder = sortOrder + 1
        });

        db.DojoRunNotes.Add(new DojoRunNoteEntity
        {
            SessionKey = sessionKey,
            TimestampUtc = DateTime.UtcNow,
            Message = $"Added ingredient '{name}'."
        });

        await db.SaveChangesAsync(cancellationToken);
        return await BuildSnapshotAsync(db, sessionKey, cancellationToken);
    }

    public async Task<DojoWorkspaceSnapshot> AddStepAsync(
        string? sessionId,
        DojoStepDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        var text = draft.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return await GetSnapshotAsync(sessionId, cancellationToken);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessionKey = NormalizeSessionKey(sessionId);

        await EnsureWorkspaceInitializedAsync(db, sessionKey, cancellationToken);

        var stepNumber = await db.DojoSteps
            .Where(x => x.SessionKey == sessionKey)
            .Select(x => (int?)x.StepNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var nextStep = stepNumber + 1;

        db.DojoSteps.Add(new DojoStepEntity
        {
            SessionKey = sessionKey,
            StepNumber = nextStep,
            Text = text
        });

        db.DojoRunNotes.Add(new DojoRunNoteEntity
        {
            SessionKey = sessionKey,
            TimestampUtc = DateTime.UtcNow,
            Message = $"Added step {nextStep}."
        });

        await db.SaveChangesAsync(cancellationToken);
        return await BuildSnapshotAsync(db, sessionKey, cancellationToken);
    }

    public async Task AppendRunNoteAsync(
        string? sessionId,
        string message,
        DateTime? timestampUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessionKey = NormalizeSessionKey(sessionId);

        await EnsureWorkspaceInitializedAsync(db, sessionKey, cancellationToken);
        db.DojoRunNotes.Add(new DojoRunNoteEntity
        {
            SessionKey = sessionKey,
            TimestampUtc = timestampUtc ?? DateTime.UtcNow,
            Message = message.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string NormalizeSessionKey(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return DefaultSessionKey;
        }

        var filtered = new string(sessionId
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            .ToArray());

        if (string.IsNullOrWhiteSpace(filtered))
        {
            return DefaultSessionKey;
        }

        return filtered.Length <= 128
            ? filtered
            : filtered[..128];
    }

    private static async Task EnsureWorkspaceInitializedAsync(
        DemoWorkflowDbContext db,
        string sessionKey,
        CancellationToken cancellationToken)
    {
        if (await db.DojoWorkspaces.AnyAsync(x => x.SessionKey == sessionKey, cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;

        db.DojoWorkspaces.Add(new DojoWorkspaceEntity
        {
            SessionKey = sessionKey,
            Title = "Classic Scrambled Eggs",
            Minutes = 15,
            Difficulty = "Beginner",
            HighProtein = true,
            LowCarb = true,
            Spicy = false,
            Vegetarian = true,
            LastSavedUtc = null,
            CreatedUtc = now,
            UpdatedUtc = now
        });

        db.DojoIngredients.AddRange(
            new DojoIngredientEntity
            {
                SessionKey = sessionKey,
                IngredientId = "ing-001",
                Name = "Eggs",
                Amount = "2",
                Optional = false,
                Notes = string.Empty,
                SortOrder = 1
            },
            new DojoIngredientEntity
            {
                SessionKey = sessionKey,
                IngredientId = "ing-002",
                Name = "Butter",
                Amount = "1 tbsp",
                Optional = false,
                Notes = string.Empty,
                SortOrder = 2
            },
            new DojoIngredientEntity
            {
                SessionKey = sessionKey,
                IngredientId = "ing-003",
                Name = "Salt",
                Amount = "to taste",
                Optional = false,
                Notes = string.Empty,
                SortOrder = 3
            },
            new DojoIngredientEntity
            {
                SessionKey = sessionKey,
                IngredientId = "ing-004",
                Name = "Chives",
                Amount = "1 tbsp",
                Optional = true,
                Notes = "optional, chopped",
                SortOrder = 4
            });

        db.DojoSteps.AddRange(
            new DojoStepEntity { SessionKey = sessionKey, StepNumber = 1, Text = "Crack the eggs into a bowl and whisk with salt." },
            new DojoStepEntity { SessionKey = sessionKey, StepNumber = 2, Text = "Heat a nonstick pan over medium heat and melt the butter." },
            new DojoStepEntity { SessionKey = sessionKey, StepNumber = 3, Text = "Pour in the eggs and stir continuously with a spatula." },
            new DojoStepEntity { SessionKey = sessionKey, StepNumber = 4, Text = "Cook until softly set, then remove from heat." },
            new DojoStepEntity { SessionKey = sessionKey, StepNumber = 5, Text = "Garnish with chives if desired." });

        db.DojoRunNotes.AddRange(
            new DojoRunNoteEntity
            {
                SessionKey = sessionKey,
                TimestampUtc = now.AddMinutes(-6),
                Message = "Session started with recipe canvas preset."
            },
            new DojoRunNoteEntity
            {
                SessionKey = sessionKey,
                TimestampUtc = now.AddMinutes(-4),
                Message = "Assistant suggested changing duration from 20 to 15 minutes."
            },
            new DojoRunNoteEntity
            {
                SessionKey = sessionKey,
                TimestampUtc = now.AddMinutes(-2),
                Message = "Generated summary card for ingredients and preparation steps."
            });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<DojoWorkspaceSnapshot> BuildSnapshotAsync(
        DemoWorkflowDbContext db,
        string sessionKey,
        CancellationToken cancellationToken)
    {
        var workspace = await db.DojoWorkspaces
            .AsNoTracking()
            .SingleAsync(x => x.SessionKey == sessionKey, cancellationToken);

        var ingredients = await db.DojoIngredients
            .AsNoTracking()
            .Where(x => x.SessionKey == sessionKey)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.IngredientId)
            .Select(x => new DojoIngredientRow(
                x.IngredientId,
                x.Name,
                x.Amount,
                x.Optional,
                x.Notes))
            .ToListAsync(cancellationToken);

        var steps = await db.DojoSteps
            .AsNoTracking()
            .Where(x => x.SessionKey == sessionKey)
            .OrderBy(x => x.StepNumber)
            .Select(static x => x.Text)
            .ToListAsync(cancellationToken);

        var notes = await db.DojoRunNotes
            .AsNoTracking()
            .Where(x => x.SessionKey == sessionKey)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(100)
            .Select(x => new DojoRunNote(x.TimestampUtc, x.Message))
            .ToListAsync(cancellationToken);

        return new DojoWorkspaceSnapshot(
            Recipe: new DojoRecipeModel
            {
                Title = workspace.Title,
                Minutes = workspace.Minutes,
                Difficulty = workspace.Difficulty,
                HighProtein = workspace.HighProtein,
                LowCarb = workspace.LowCarb,
                Spicy = workspace.Spicy,
                Vegetarian = workspace.Vegetarian
            },
            Ingredients: ingredients,
            Steps: steps,
            RunNotes: notes,
            LastSavedUtc: workspace.LastSavedUtc);
    }

    private static async Task<string> BuildNextIngredientIdAsync(
        DemoWorkflowDbContext db,
        string sessionKey,
        CancellationToken cancellationToken)
    {
        var ingredientIds = await db.DojoIngredients
            .Where(x => x.SessionKey == sessionKey)
            .Select(x => x.IngredientId)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var ingredientId in ingredientIds)
        {
            if (TryParseIngredientNumber(ingredientId, out var parsed))
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

    public bool BudgetFriendly { get; set; }

    public bool OnePotMeal { get; set; }

    public bool Vegan { get; set; }
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
