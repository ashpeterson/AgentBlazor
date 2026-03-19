namespace AgentBlazor.App;

/// <summary>
/// Structured result returned by semantic capability actions.
/// This is intentionally richer than plain text so the runtime adapter can later
/// project summary, explanation, warnings, and UI suggestions separately.
/// </summary>
public sealed record CapabilityResult(string Summary)
{
    public bool Succeeded { get; init; } = true;

    public bool RequiresClarification { get; init; }

    public string? ClarificationQuestion { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyDictionary<string, object?> Outputs { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CapabilityUiSuggestion> UiSuggestions { get; init; } = [];

    public IReadOnlyList<string> NextActions { get; init; } = [];

    public static CapabilityResult Success(string summary) => new(summary);

    public static CapabilityResult Failure(string summary) => new(summary)
    {
        Succeeded = false
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
