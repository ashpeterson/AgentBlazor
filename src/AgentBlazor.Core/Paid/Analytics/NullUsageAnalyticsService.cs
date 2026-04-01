namespace AgentBlazor.Core.Paid.Analytics;

/// <summary>
/// No-op usage analytics service for free tier.
/// Returns empty/default data for all queries.
/// </summary>
public sealed class NullUsageAnalyticsService : IUsageAnalyticsService
{
    public static NullUsageAnalyticsService Instance { get; } = new();

    public Task<UsageSummary> GetSummaryAsync(DateRange range, CancellationToken ct = default)
    {
        return Task.FromResult(new UsageSummary(0, 0, 0, 1.0, TimeSpan.Zero, []));
    }

    public Task<IReadOnlyList<ActionMetric>> GetTopActionsAsync(int limit = 20, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ActionMetric>>([]);
    }

    public Task<IReadOnlyList<AgentMetric>> GetAgentPerformanceAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AgentMetric>>([]);
    }

    public Task<IReadOnlyList<DailyUsage>> GetDailyTrendsAsync(int days = 30, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<DailyUsage>>([]);
    }

    public Task<double> GetSuccessRateAsync(DateRange range, CancellationToken ct = default)
    {
        return Task.FromResult(1.0);
    }

    public Task<TimeSpan> GetAverageResponseTimeAsync(DateRange range, CancellationToken ct = default)
    {
        return Task.FromResult(TimeSpan.Zero);
    }

    public Task<IReadOnlyList<UsageAnomaly>> DetectAnomaliesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<UsageAnomaly>>([]);
    }
}
