namespace AgentBlazor.Core.Paid.Analytics;

/// <summary>
/// Usage analytics service for Pro/Enterprise tiers.
/// Provides insights into agent usage patterns, performance, and trends.
/// </summary>
public interface IUsageAnalyticsService
{
    /// <summary>
    /// Gets a high-level usage summary for the specified date range.
    /// </summary>
    Task<UsageSummary> GetSummaryAsync(DateRange range, CancellationToken ct = default);

    /// <summary>
    /// Gets the most frequently executed actions.
    /// </summary>
    Task<IReadOnlyList<ActionMetric>> GetTopActionsAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Gets performance metrics for each agent.
    /// </summary>
    Task<IReadOnlyList<AgentMetric>> GetAgentPerformanceAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets daily usage trends for the specified number of days.
    /// </summary>
    Task<IReadOnlyList<DailyUsage>> GetDailyTrendsAsync(int days = 30, CancellationToken ct = default);

    /// <summary>
    /// Gets the overall success rate for the specified date range.
    /// </summary>
    Task<double> GetSuccessRateAsync(DateRange range, CancellationToken ct = default);

    /// <summary>
    /// Gets the average response time for the specified date range.
    /// </summary>
    Task<TimeSpan> GetAverageResponseTimeAsync(DateRange range, CancellationToken ct = default);

    /// <summary>
    /// Detects anomalies in usage patterns (e.g., sudden drops, spikes, error bursts).
    /// </summary>
    Task<IReadOnlyList<UsageAnomaly>> DetectAnomaliesAsync(CancellationToken ct = default);
}
