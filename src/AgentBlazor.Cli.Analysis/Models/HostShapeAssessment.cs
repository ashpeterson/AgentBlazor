namespace AgentBlazor.Cli.Analysis.Models;

public sealed record HostShapeAssessment
{
    public HostShapeKind Kind { get; init; } = HostShapeKind.Standard;

    public HostFamily Family { get; init; } = HostFamily.StandardWebApp;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? FilePath { get; init; }

    public string? SuggestedFix { get; init; }
}

public enum HostShapeKind
{
    Standard,
    AdvancedReview,
    Unsupported
}

public enum HostFamily
{
    StandardWebApp,
    LegacyServer,
    HostedWebAssembly,
    OqtaneStyle,
    Unknown
}
