using Microsoft.Data.Sqlite;

namespace AgentBlazor.Core.Paid.Analytics;

/// <summary>
/// SQLite-backed usage analytics service for Pro/Enterprise tiers.
/// Queries the action_history table for aggregated metrics.
/// </summary>
public sealed class SqliteUsageAnalyticsService : IUsageAnalyticsService, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _connectionOpened;

    public SqliteUsageAnalyticsService(string? connectionString = null)
    {
        connectionString ??= "Data Source=agentblazor-history.db";
        _connection = new SqliteConnection(connectionString);
    }

    public static SqliteUsageAnalyticsService CreateWithPath(string dbPath)
    {
        return new SqliteUsageAnalyticsService($"Data Source={dbPath}");
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connectionOpened) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connectionOpened) return;
            await _connection.OpenAsync(ct).ConfigureAwait(false);
            _connectionOpened = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UsageSummary> GetSummaryAsync(DateRange range, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COUNT(*) as total_actions,
                    COUNT(DISTINCT user_id) as unique_users,
                    COUNT(DISTINCT session_id) as unique_sessions,
                    AVG(CASE WHEN succeeded = 1 THEN 100.0 ELSE 0.0 END) as success_rate,
                    AVG(COALESCE(duration_ms, 0)) as avg_duration_ms
                FROM action_history
                WHERE DATE(timestamp) >= @startDate AND DATE(timestamp) <= @endDate
                """;

            cmd.Parameters.AddWithValue("@startDate", range.Start.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@endDate", range.End.ToString("yyyy-MM-dd"));

            int totalActions = 0;
            int uniqueUsers = 0;
            int uniqueSessions = 0;
            double successRate = 100.0;
            double avgDurationMs = 0;

            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    totalActions = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    uniqueUsers = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    uniqueSessions = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                    successRate = reader.IsDBNull(3) ? 100.0 : reader.GetDouble(3);
                    avgDurationMs = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                }
            }

            // Get top actions
            using var topCmd = _connection.CreateCommand();
            topCmd.CommandText = """
                SELECT action_id
                FROM action_history
                WHERE DATE(timestamp) >= @startDate AND DATE(timestamp) <= @endDate
                GROUP BY action_id
                ORDER BY COUNT(*) DESC
                LIMIT 5
                """;

            topCmd.Parameters.AddWithValue("@startDate", range.Start.ToString("yyyy-MM-dd"));
            topCmd.Parameters.AddWithValue("@endDate", range.End.ToString("yyyy-MM-dd"));

            var topActions = new List<string>();
            using (var reader = await topCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    topActions.Add(reader.GetString(0));
                }
            }

            return new UsageSummary(
                totalActions,
                uniqueUsers,
                uniqueSessions,
                successRate / 100.0,
                TimeSpan.FromMilliseconds(avgDurationMs),
                topActions);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActionMetric>> GetTopActionsAsync(int limit = 20, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    action_id,
                    COUNT(*) as execution_count,
                    AVG(CASE WHEN succeeded = 1 THEN 100.0 ELSE 0.0 END) as success_rate,
                    AVG(COALESCE(duration_ms, 0)) as avg_duration_ms
                FROM action_history
                GROUP BY action_id
                ORDER BY execution_count DESC
                LIMIT @limit
                """;

            cmd.Parameters.AddWithValue("@limit", limit);

            var results = new List<ActionMetric>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new ActionMetric(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetDouble(2) / 100.0,
                    TimeSpan.FromMilliseconds(reader.GetDouble(3))));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AgentMetric>> GetAgentPerformanceAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    agent_id,
                    COUNT(*) as total_actions,
                    COUNT(DISTINCT user_id) as unique_users,
                    AVG(CASE WHEN succeeded = 1 THEN 100.0 ELSE 0.0 END) as success_rate,
                    AVG(COALESCE(duration_ms, 0)) as avg_duration_ms
                FROM action_history
                GROUP BY agent_id
                ORDER BY total_actions DESC
                """;

            var results = new List<AgentMetric>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new AgentMetric(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetDouble(3) / 100.0,
                    TimeSpan.FromMilliseconds(reader.GetDouble(4))));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<DailyUsage>> GetDailyTrendsAsync(int days = 30, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    DATE(timestamp) as date,
                    COUNT(*) as action_count,
                    COUNT(DISTINCT user_id) as user_count,
                    AVG(CASE WHEN succeeded = 1 THEN 100.0 ELSE 0.0 END) as success_rate
                FROM action_history
                WHERE DATE(timestamp) >= DATE('now', @daysOffset)
                GROUP BY DATE(timestamp)
                ORDER BY date DESC
                """;

            cmd.Parameters.AddWithValue("@daysOffset", $"-{days} days");

            var results = new List<DailyUsage>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var dateStr = reader.GetString(0);
                results.Add(new DailyUsage(
                    DateOnly.Parse(dateStr),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetDouble(3) / 100.0));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<double> GetSuccessRateAsync(DateRange range, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT AVG(CASE WHEN succeeded = 1 THEN 100.0 ELSE 0.0 END)
                FROM action_history
                WHERE DATE(timestamp) >= @startDate AND DATE(timestamp) <= @endDate
                """;

            cmd.Parameters.AddWithValue("@startDate", range.Start.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@endDate", range.End.ToString("yyyy-MM-dd"));

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is double d ? d / 100.0 : 1.0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TimeSpan> GetAverageResponseTimeAsync(DateRange range, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT AVG(COALESCE(duration_ms, 0))
                FROM action_history
                WHERE DATE(timestamp) >= @startDate AND DATE(timestamp) <= @endDate
                  AND duration_ms IS NOT NULL
                """;

            cmd.Parameters.AddWithValue("@startDate", range.Start.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@endDate", range.End.ToString("yyyy-MM-dd"));

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is double d ? TimeSpan.FromMilliseconds(d) : TimeSpan.Zero;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<UsageAnomaly>> DetectAnomaliesAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var anomalies = new List<UsageAnomaly>();

            // Detect error rate spikes (days with >20% failure rate)
            using var errorCmd = _connection.CreateCommand();
            errorCmd.CommandText = """
                SELECT
                    DATE(timestamp) as date,
                    COUNT(*) as total,
                    SUM(CASE WHEN succeeded = 0 THEN 1 ELSE 0 END) as failures
                FROM action_history
                WHERE DATE(timestamp) >= DATE('now', '-7 days')
                GROUP BY DATE(timestamp)
                HAVING (failures * 1.0 / total) > 0.2
                ORDER BY date DESC
                """;

            using (var reader = await errorCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var date = reader.GetString(0);
                    var total = reader.GetInt32(1);
                    var failures = reader.GetInt32(2);
                    var failureRate = (double)failures / total;

                    anomalies.Add(new UsageAnomaly(
                        DateTimeOffset.Parse(date),
                        "high_error_rate",
                        $"Error rate of {failureRate:P0} detected on {date}",
                        failureRate,
                        new Dictionary<string, object?>
                        {
                            ["date"] = date,
                            ["total_actions"] = total,
                            ["failures"] = failures,
                            ["failure_rate"] = failureRate
                        }));
                }
            }

            // Detect usage drops (days with <50% of average daily usage)
            using var dropCmd = _connection.CreateCommand();
            dropCmd.CommandText = """
                WITH daily_counts AS (
                    SELECT DATE(timestamp) as date, COUNT(*) as count
                    FROM action_history
                    WHERE DATE(timestamp) >= DATE('now', '-30 days')
                    GROUP BY DATE(timestamp)
                ),
                avg_count AS (
                    SELECT AVG(count) as avg FROM daily_counts
                )
                SELECT d.date, d.count, a.avg
                FROM daily_counts d, avg_count a
                WHERE d.count < a.avg * 0.5
                  AND d.date >= DATE('now', '-7 days')
                ORDER BY d.date DESC
                """;

            using (var reader = await dropCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var date = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    var avg = reader.GetDouble(2);

                    anomalies.Add(new UsageAnomaly(
                        DateTimeOffset.Parse(date),
                        "usage_drop",
                        $"Usage dropped to {count} actions on {date} (avg: {avg:F0})",
                        1.0 - (count / avg),
                        new Dictionary<string, object?>
                        {
                            ["date"] = date,
                            ["action_count"] = count,
                            ["average"] = avg
                        }));
                }
            }

            return anomalies;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
