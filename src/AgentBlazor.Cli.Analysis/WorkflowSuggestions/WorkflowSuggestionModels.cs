namespace AgentBlazor.Cli.Analysis.WorkflowSuggestions;

public sealed record WorkflowSuggestionSet
{
    public IReadOnlyList<WorkflowSuggestion> Suggestions { get; init; } = [];

    public IReadOnlyList<RejectedWorkflowSuggestion> Rejected { get; init; } = [];

    public string Model { get; init; } = string.Empty;
}

public sealed record WorkflowSuggestion
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<WorkflowMethodReference> Methods { get; init; } = [];

    public string CapabilityClass { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string Reasoning { get; init; } = string.Empty;

    public double Confidence { get; init; }
}

public sealed record WorkflowMethodReference
{
    public string Service { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;
}

public sealed record RejectedWorkflowSuggestion
{
    public string Name { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}
