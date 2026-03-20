using System.Text;

namespace AgentBlazor.Core.Runtime.Tracing;

/// <summary>
/// Service for querying and managing prompt traces.
/// </summary>
internal sealed class PromptTraceService : IPromptTraceService
{
    private readonly IPromptTraceStore _store;

    public PromptTraceService(IPromptTraceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public int TraceCount => _store.Count;

    public Task<PromptTrace?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        return _store.GetByIdAsync(traceId, cancellationToken);
    }

    public async Task<IReadOnlyList<PromptTrace>> GetTracesAsync(
        PromptTraceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<PromptTrace> traces;

        // Use indexed queries when possible
        if (!string.IsNullOrEmpty(query.SessionId))
        {
            traces = await _store.GetBySessionAsync(query.SessionId, query.Limit + query.Offset, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(query.UserId))
        {
            traces = await _store.GetByUserAsync(query.UserId, query.Limit + query.Offset, cancellationToken);
        }
        else if (query.Outcome.HasValue)
        {
            traces = await _store.GetByOutcomeAsync(query.Outcome.Value, query.Limit + query.Offset, cancellationToken);
        }
        else
        {
            traces = await _store.GetRecentAsync(query.Limit + query.Offset, cancellationToken);
        }

        // Apply additional filters
        var filtered = traces.AsEnumerable();

        if (query.Since.HasValue)
        {
            filtered = filtered.Where(t => t.Entry.Timestamp >= query.Since.Value);
        }

        if (query.Until.HasValue)
        {
            filtered = filtered.Where(t => t.Entry.Timestamp <= query.Until.Value);
        }

        if (!string.IsNullOrEmpty(query.PrimaryIntent))
        {
            filtered = filtered.Where(t =>
                string.Equals(t.Classification?.PrimaryIntent, query.PrimaryIntent, StringComparison.OrdinalIgnoreCase));
        }

        // Apply outcome filter if not already used for indexed query
        if (query.Outcome.HasValue && string.IsNullOrEmpty(query.SessionId) && string.IsNullOrEmpty(query.UserId))
        {
            // Already filtered by GetByOutcomeAsync
        }
        else if (query.Outcome.HasValue)
        {
            filtered = filtered.Where(t => t.Response?.Outcome == query.Outcome.Value);
        }

        // Apply pagination
        return filtered
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToArray();
    }

    public Task<PromptTraceStatistics> GetStatisticsAsync(
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        return _store.GetStatisticsAsync(since, cancellationToken);
    }

    public Task ClearTracesAsync(CancellationToken cancellationToken = default)
    {
        return _store.ClearAsync(cancellationToken);
    }

    public async Task<string> GenerateReportAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var traces = await _store.GetRecentAsync(limit, cancellationToken);
        var stats = await _store.GetStatisticsAsync(cancellationToken: cancellationToken);

        var sb = new StringBuilder();

        sb.AppendLine("# Prompt Trace Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Statistics Summary
        sb.AppendLine("## Summary Statistics");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Total Traces | {stats.TotalTraces} |");
        sb.AppendLine($"| Succeeded | {stats.SucceededCount} |");
        sb.AppendLine($"| Failed | {stats.FailedCount} |");
        sb.AppendLine($"| Partial Success | {stats.PartialSuccessCount} |");
        sb.AppendLine($"| Avg Duration | {stats.AverageDuration.TotalMilliseconds:F1}ms |");
        sb.AppendLine($"| Min Duration | {stats.MinDuration.TotalMilliseconds:F1}ms |");
        sb.AppendLine($"| Max Duration | {stats.MaxDuration.TotalMilliseconds:F1}ms |");
        sb.AppendLine();

        // Intent Distribution
        if (stats.IntentCounts.Count > 0)
        {
            sb.AppendLine("## Intent Distribution");
            sb.AppendLine();
            sb.AppendLine("| Intent | Count |");
            sb.AppendLine("|--------|-------|");
            foreach (var (intent, count) in stats.IntentCounts.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"| {intent} | {count} |");
            }
            sb.AppendLine();
        }

        // Component Usage
        if (stats.ComponentCounts.Count > 0)
        {
            sb.AppendLine("## Component Usage");
            sb.AppendLine();
            sb.AppendLine("| Component | Count |");
            sb.AppendLine("|-----------|-------|");
            foreach (var (component, count) in stats.ComponentCounts.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"| {component} | {count} |");
            }
            sb.AppendLine();
        }

        // Individual Traces
        sb.AppendLine("## Trace Details");
        sb.AppendLine();

        foreach (var trace in traces)
        {
            sb.AppendLine($"### Trace: {trace.TraceId}");
            sb.AppendLine();
            sb.AppendLine($"**Timestamp:** {trace.Entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC");
            sb.AppendLine();
            sb.AppendLine($"**User Message:** {trace.Entry.UserMessage}");
            sb.AppendLine();

            if (trace.Entry.SessionId is not null)
            {
                sb.AppendLine($"**Session:** {trace.Entry.SessionId}");
            }

            if (trace.Entry.AgentName is not null)
            {
                sb.AppendLine($"**Agent:** {trace.Entry.AgentName}");
            }

            sb.AppendLine();

            // Classification
            if (trace.Classification is not null)
            {
                sb.AppendLine("#### Classification");
                sb.AppendLine();
                sb.AppendLine($"| Property | Value |");
                sb.AppendLine($"|----------|-------|");
                sb.AppendLine($"| Method | {trace.Classification.Method} |");
                sb.AppendLine($"| Intent | {trace.Classification.PrimaryIntent} |");
                sb.AppendLine($"| Confidence | {trace.Classification.Confidence:P0} |");
                sb.AppendLine($"| Component | {trace.Classification.TargetComponentId ?? "-"} |");
                sb.AppendLine($"| Action | {trace.Classification.TargetActionId ?? "-"} |");
                sb.AppendLine($"| Duration | {trace.Classification.Duration.TotalMilliseconds:F1}ms |");
                sb.AppendLine();

                if (trace.Classification.ExtractedEntities?.Count > 0)
                {
                    sb.AppendLine("**Extracted Entities:**");
                    foreach (var (key, value) in trace.Classification.ExtractedEntities)
                    {
                        sb.AppendLine($"- `{key}`: `{value}`");
                    }
                    sb.AppendLine();
                }
            }

            // Resolution
            if (trace.Resolution is not null)
            {
                sb.AppendLine("#### Resolution");
                sb.AppendLine();
                sb.AppendLine($"| Property | Value |");
                sb.AppendLine($"|----------|-------|");
                sb.AppendLine($"| Component | {trace.Resolution.ComponentId} |");
                sb.AppendLine($"| Action | {trace.Resolution.ActionId} |");
                sb.AppendLine($"| Requires Approval | {trace.Resolution.RequiresApproval} |");
                sb.AppendLine($"| Duration | {trace.Resolution.Duration.TotalMilliseconds:F1}ms |");
                sb.AppendLine();
                sb.AppendLine($"**Reason:** {trace.Resolution.Reason}");
                sb.AppendLine();

                if (trace.Resolution.Arguments?.Count > 0)
                {
                    sb.AppendLine("**Arguments:**");
                    foreach (var (key, value) in trace.Resolution.Arguments)
                    {
                        sb.AppendLine($"- `{key}`: `{value}`");
                    }
                    sb.AppendLine();
                }
            }

            // Planning
            if (trace.Planning is not null)
            {
                sb.AppendLine("#### Workflow Planning");
                sb.AppendLine();
                sb.AppendLine($"- **Available Tools:** {trace.Planning.ToolCount}");
                sb.AppendLine($"- **Planned Workflow Steps:** {trace.Planning.WorkflowSteps.Count}");
                sb.AppendLine($"- **Duration:** {trace.Planning.Duration.TotalMilliseconds:F1}ms");
                sb.AppendLine();

                for (var index = 0; index < trace.Planning.WorkflowSteps.Count; index++)
                {
                    var action = trace.Planning.WorkflowSteps[index];
                    sb.AppendLine($"  - **Step {index + 1}:** `{FormatStepTarget(action.ComponentId, action.ActionId)}`");
                    sb.AppendLine($"    Reason: {action.Reason}");
                    if (action.Arguments?.Count > 0)
                    {
                        var args = string.Join(", ", action.Arguments.Select(static kvp => $"{kvp.Key}={kvp.Value}"));
                        sb.AppendLine($"    Arguments: {args}");
                    }
                }
                sb.AppendLine();
            }

            // Execution
            if (trace.Execution is not null)
            {
                sb.AppendLine("#### Workflow Execution");
                sb.AppendLine();
                sb.AppendLine($"| Step | Outcome | Message | Duration |");
                sb.AppendLine($"|------|---------|---------|----------|");
                foreach (var result in trace.Execution.ExecutionSteps)
                {
                    var status = FormatExecutionOutcome(result);
                    var msg = result.Message.Length > 50 ? $"{result.Message[..50]}..." : result.Message;
                    sb.AppendLine($"| {FormatStepTarget(result.ComponentId, result.ActionId)} | {status} | {msg} | {result.Duration.TotalMilliseconds:F1}ms |");
                }
                sb.AppendLine();
                sb.AppendLine($"**Total Duration:** {trace.Execution.TotalDuration.TotalMilliseconds:F1}ms | **Completed:** {trace.Execution.SuccessCount} | **Failed/Blocked:** {trace.Execution.FailureCount}");
                sb.AppendLine();
            }

            // Response
            if (trace.Response is not null)
            {
                sb.AppendLine("#### Response");
                sb.AppendLine();
                sb.AppendLine($"- **Outcome:** {trace.Response.Outcome}");
                sb.AppendLine($"- **Total Duration:** {trace.Response.TotalDuration.TotalMilliseconds:F1}ms");
                sb.AppendLine();

                if (trace.Response.ErrorMessage is not null)
                {
                    sb.AppendLine($"**Error:** {trace.Response.ErrorMessage}");
                    sb.AppendLine();
                }

                var responsePreview = trace.Response.ResponseText.Length > 200
                    ? $"{trace.Response.ResponseText[..200]}..."
                    : trace.Response.ResponseText;
                sb.AppendLine($"**Response:** {responsePreview}");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatStepTarget(string componentId, string actionId)
        => $"{componentId}.{actionId}";

    private static string FormatExecutionOutcome(PromptTraceExecutionResult result)
        => result.Succeeded
            ? "Completed"
            : result.Outcome switch
            {
                ActionOutcome.Blocked => "Blocked",
                ActionOutcome.NeedsClarification => "Needs clarification",
                ActionOutcome.Queued => "Queued",
                ActionOutcome.Failed => "Failed",
                _ => "Failed"
            };
}
