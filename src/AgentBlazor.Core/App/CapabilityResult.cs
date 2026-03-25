namespace AgentBlazor.App;

/// <summary>
/// Structured result returned by semantic capability actions.
/// This is intentionally richer than plain text so the runtime adapter can later
/// project summary, explanation, warnings, and UI suggestions separately.
/// </summary>
public sealed record CapabilityResult(string Summary)
{
    public bool Succeeded { get; init; } = true;

    public bool IsBlocked { get; init; }

    public bool RequiresClarification { get; init; }

    public string? ClarificationQuestion { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyDictionary<string, object?> Outputs { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CapabilityUiSuggestion> UiSuggestions { get; init; } = [];

    public IReadOnlyList<string> NextActions { get; init; } = [];

    public CapabilityResult WithWarning(string warning)
        => WithWarnings(warning);

    public CapabilityResult WithWarnings(params string[] warnings)
    {
        var merged = Warnings
            .Concat(warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with { Warnings = merged };
    }

    public CapabilityResult WithNextAction(string nextAction)
        => WithNextActions(nextAction);

    public CapabilityResult WithNextActions(params string[] nextActions)
    {
        var merged = NextActions
            .Concat(nextActions.Where(static nextAction => !string.IsNullOrWhiteSpace(nextAction)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with { NextActions = merged };
    }

    public CapabilityResult WithOutput(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var outputs = new Dictionary<string, object?>(Outputs, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };

        return this with { Outputs = outputs };
    }

    public CapabilityResult WithOutputs(IReadOnlyDictionary<string, object?> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        var merged = new Dictionary<string, object?>(Outputs, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in outputs)
        {
            merged[key] = value;
        }

        return this with { Outputs = merged };
    }

    public static CapabilityResult Success(string summary) => new(summary);

    public static CapabilityResult Failure(string summary) => new(summary)
    {
        Succeeded = false
    };

    public static CapabilityResult Blocked(string summary) => new(summary)
    {
        Succeeded = false,
        IsBlocked = true
    };

    public static CapabilityResult NeedsClarification(string question) => new("Clarification required.")
    {
        Succeeded = false,
        RequiresClarification = true,
        ClarificationQuestion = question
    };
}

public sealed record CapabilityUiSuggestion(
    string Kind,
    string Target,
    IReadOnlyDictionary<string, object?> Arguments);
