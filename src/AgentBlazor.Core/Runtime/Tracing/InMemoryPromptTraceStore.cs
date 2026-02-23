using System.Collections.Concurrent;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// In-memory implementation of IPromptTraceStore with automatic TTL cleanup.
/// Suitable for development and testing scenarios.
/// </summary>
internal sealed class InMemoryPromptTraceStore : IPromptTraceStore, IDisposable
{
    private readonly ConcurrentDictionary<string, PromptTrace> _traces = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _sessionIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _userIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly PromptTracingOptions _options;
    private readonly ILogger<InMemoryPromptTraceStore>? _logger;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private Timer? _cleanupTimer;
    private bool _disposed;

    public InMemoryPromptTraceStore(
        IOptions<PromptTracingOptions>? options = null,
        ILogger<InMemoryPromptTraceStore>? logger = null)
    {
        _options = options?.Value ?? new PromptTracingOptions();
        _logger = logger;

        if (_options.EnableAutoCleanup)
        {
            _cleanupTimer = new Timer(
                _ => _ = CleanupExpiredTracesAsync(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);
        }
    }

    public int Count => _traces.Count;

    public Task StoreAsync(PromptTrace trace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);

        // Enforce max trace count by removing oldest traces
        while (_traces.Count >= _options.MaxTraceCount)
        {
            var oldest = _traces.Values
                .OrderBy(t => t.Entry.Timestamp)
                .FirstOrDefault();

            if (oldest is not null)
            {
                RemoveTrace(oldest.TraceId);
            }
            else
            {
                break;
            }
        }

        _traces[trace.TraceId] = trace;

        // Update session index
        if (!string.IsNullOrEmpty(trace.Entry.SessionId))
        {
            var sessionSet = _sessionIndex.GetOrAdd(trace.Entry.SessionId, _ => []);
            lock (sessionSet)
            {
                sessionSet.Add(trace.TraceId);
            }
        }

        // Update user index
        if (!string.IsNullOrEmpty(trace.Entry.UserId))
        {
            var userSet = _userIndex.GetOrAdd(trace.Entry.UserId, _ => []);
            lock (userSet)
            {
                userSet.Add(trace.TraceId);
            }
        }

        _logger?.LogDebug("Stored trace {TraceId} for session {SessionId}",
            trace.TraceId, trace.Entry.SessionId);

        return Task.CompletedTask;
    }

    public Task<PromptTrace?> GetByIdAsync(string traceId, CancellationToken cancellationToken = default)
    {
        _traces.TryGetValue(traceId, out var trace);
        return Task.FromResult(trace);
    }

    public Task<IReadOnlyList<PromptTrace>> GetBySessionAsync(
        string sessionId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionIndex.TryGetValue(sessionId, out var traceIds))
        {
            return Task.FromResult<IReadOnlyList<PromptTrace>>([]);
        }

        List<string> ids;
        lock (traceIds)
        {
            ids = [.. traceIds];
        }

        var traces = ids
            .Select(id => _traces.TryGetValue(id, out var t) ? t : null)
            .Where(t => t is not null)
            .Cast<PromptTrace>()
            .OrderByDescending(t => t.Entry.Timestamp)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PromptTrace>>(traces);
    }

    public Task<IReadOnlyList<PromptTrace>> GetByUserAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_userIndex.TryGetValue(userId, out var traceIds))
        {
            return Task.FromResult<IReadOnlyList<PromptTrace>>([]);
        }

        List<string> ids;
        lock (traceIds)
        {
            ids = [.. traceIds];
        }

        var traces = ids
            .Select(id => _traces.TryGetValue(id, out var t) ? t : null)
            .Where(t => t is not null)
            .Cast<PromptTrace>()
            .OrderByDescending(t => t.Entry.Timestamp)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PromptTrace>>(traces);
    }

    public Task<IReadOnlyList<PromptTrace>> GetRecentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var traces = _traces.Values
            .OrderByDescending(t => t.Entry.Timestamp)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PromptTrace>>(traces);
    }

    public Task<IReadOnlyList<PromptTrace>> GetByOutcomeAsync(
        PromptTraceOutcome outcome,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var traces = _traces.Values
            .Where(t => t.Response?.Outcome == outcome)
            .OrderByDescending(t => t.Entry.Timestamp)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PromptTrace>>(traces);
    }

    public Task<PromptTraceStatistics> GetStatisticsAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        var traces = _traces.Values.AsEnumerable();

        if (since.HasValue)
        {
            traces = traces.Where(t => t.Entry.Timestamp >= since.Value);
        }

        var traceList = traces.ToList();

        if (traceList.Count == 0)
        {
            return Task.FromResult(PromptTraceStatistics.Empty);
        }

        var durations = traceList
            .Where(t => t.Response is not null)
            .Select(t => t.TotalDuration)
            .ToList();

        var outcomeCounts = traceList
            .Where(t => t.Response is not null)
            .GroupBy(t => t.Response!.Outcome.ToString())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var intentCounts = traceList
            .Where(t => t.Classification is not null)
            .GroupBy(t => t.Classification!.PrimaryIntent)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var componentCounts = traceList
            .Where(t => t.Classification?.TargetComponentId is not null)
            .GroupBy(t => t.Classification!.TargetComponentId!)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var succeededCount = traceList.Count(t => t.Response?.Outcome == PromptTraceOutcome.Succeeded);
        var failedCount = traceList.Count(t => t.Response?.Outcome == PromptTraceOutcome.Failed);
        var partialCount = traceList.Count(t => t.Response?.Outcome == PromptTraceOutcome.PartialSuccess);

        return Task.FromResult(new PromptTraceStatistics
        {
            TotalTraces = traceList.Count,
            SucceededCount = succeededCount,
            FailedCount = failedCount,
            PartialSuccessCount = partialCount,
            AverageDuration = durations.Count > 0
                ? TimeSpan.FromTicks((long)durations.Average(d => d.Ticks))
                : TimeSpan.Zero,
            MinDuration = durations.Count > 0 ? durations.Min() : TimeSpan.Zero,
            MaxDuration = durations.Count > 0 ? durations.Max() : TimeSpan.Zero,
            OutcomeCounts = outcomeCounts,
            IntentCounts = intentCounts,
            ComponentCounts = componentCounts
        });
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _traces.Clear();
        _sessionIndex.Clear();
        _userIndex.Clear();

        _logger?.LogInformation("Cleared all prompt traces");
        return Task.CompletedTask;
    }

    private void RemoveTrace(string traceId)
    {
        if (_traces.TryRemove(traceId, out var trace))
        {
            // Remove from session index
            if (!string.IsNullOrEmpty(trace.Entry.SessionId) &&
                _sessionIndex.TryGetValue(trace.Entry.SessionId, out var sessionSet))
            {
                lock (sessionSet)
                {
                    sessionSet.Remove(traceId);
                }
            }

            // Remove from user index
            if (!string.IsNullOrEmpty(trace.Entry.UserId) &&
                _userIndex.TryGetValue(trace.Entry.UserId, out var userSet))
            {
                lock (userSet)
                {
                    userSet.Remove(traceId);
                }
            }
        }
    }

    private async Task CleanupExpiredTracesAsync()
    {
        if (!await _cleanupLock.WaitAsync(0))
        {
            return; // Another cleanup is in progress
        }

        try
        {
            var cutoff = DateTimeOffset.UtcNow - _options.TraceRetention;
            var expiredIds = _traces.Values
                .Where(t => t.Entry.Timestamp < cutoff)
                .Select(t => t.TraceId)
                .ToList();

            foreach (var id in expiredIds)
            {
                RemoveTrace(id);
            }

            if (expiredIds.Count > 0)
            {
                _logger?.LogDebug("Cleaned up {Count} expired traces", expiredIds.Count);
            }
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        _cleanupLock.Dispose();
    }
}
