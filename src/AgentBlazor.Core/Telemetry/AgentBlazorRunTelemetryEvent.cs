namespace AgentBlazor.Telemetry;

public enum AgentBlazorRunEventKind
{
    Started,
    Finished
}

public enum AgentBlazorRunOutcome
{
    Succeeded,
    ProviderMissing,
    NoAgentRegistered,
    NoAllowedActions,
    Failed,
    Canceled
}

public static class AgentBlazorTelemetrySources
{
    public const string Runtime = "runtime";

    public const string AgUiHosted = "agui-hosted";
}

public sealed record AgentBlazorRunTelemetryEvent
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public AgentBlazorRunEventKind Kind { get; init; }

    public string Source { get; init; } = AgentBlazorTelemetrySources.Runtime;

    public string AgentName { get; init; } = "none";

    public string? RequestedAgentName { get; init; }

    public AgentBlazorRunOutcome? Outcome { get; init; }

    public int PlannedActionCount { get; init; }

    public int ExecutionResultCount { get; init; }

    public int FailedExecutionCount { get; init; }

    public int BlockedActionCount { get; init; }

    public string? Tier { get; init; }

    public bool ProviderConfigured { get; init; }

    public bool HasContext { get; init; }

    public bool HasRegisteredComponents { get; init; }

    public string? Detail { get; init; }
}
