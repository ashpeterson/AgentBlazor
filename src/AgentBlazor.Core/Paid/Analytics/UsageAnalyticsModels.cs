namespace AgentBlazor.Core.Paid.Analytics;

/// <summary>
/// Date range for analytics queries.
/// </summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public static DateRange Last7Days => new(
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
        DateOnly.FromDateTime(DateTime.UtcNow));

    public static DateRange Last30Days => new(
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        DateOnly.FromDateTime(DateTime.UtcNow));

    public static DateRange Last90Days => new(
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)),
        DateOnly.FromDateTime(DateTime.UtcNow));
}

/// <summary>
/// High-level usage summary for a date range.
/// </summary>
public record UsageSummary(
    int TotalActions,
    int UniqueUsers,
    int UniqueSessions,
    double SuccessRate,
    TimeSpan AvgResponseTime,
    IReadOnlyList<string> TopActions);

/// <summary>
/// Metrics for a specific action.
/// </summary>
public record ActionMetric(
    string ActionId,
    int ExecutionCount,
    double SuccessRate,
    TimeSpan AvgDuration);

/// <summary>
/// Metrics for a specific agent.
/// </summary>
public record AgentMetric(
    string AgentId,
    int TotalActions,
    int UniqueUsers,
    double SuccessRate,
    TimeSpan AvgResponseTime);

/// <summary>
/// Daily usage statistics.
/// </summary>
public record DailyUsage(
    DateOnly Date,
    int ActionCount,
    int UserCount,
    double SuccessRate);

/// <summary>
/// Detected anomaly in usage patterns.
/// </summary>
public record UsageAnomaly(
    DateTimeOffset DetectedAt,
    string AnomalyType,
    string Description,
    double Severity,
    IReadOnlyDictionary<string, object?> Details);
