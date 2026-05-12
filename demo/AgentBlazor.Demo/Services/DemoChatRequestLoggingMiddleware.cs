using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Middleware;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoChatRequestLoggingMiddleware(
    IDemoChatRequestLog requestLog,
    IOptions<DemoLoggingOptions> options,
    IOptions<AgentBlazorOptions> agentOptions,
    ILogger<DemoChatRequestLoggingMiddleware> logger) : IAgentTurnMiddleware
{
    private readonly DemoLoggingOptions _options = options.Value;
    private readonly AgentBlazorOptions _agentOptions = agentOptions.Value;

    public async Task InvokeAsync(
        AgentTurnContext context,
        Func<CancellationToken, Task> next,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            await next(ct);
            return;
        }

        var requestId = Guid.NewGuid().ToString("n");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(ct);
            stopwatch.Stop();

            var response = context.Response;
            var entry = CreateEntry(
                context.Request,
                requestId,
                stopwatch.ElapsedMilliseconds,
                response is null ? "missing-response" : "ok",
                response);

            await requestLog.AppendAsync(entry, ct);
            logger.LogInformation(
                "Demo chat request {RequestId} completed: route={Route} agent={AgentName} status={Status} promptLength={PromptLength} durationMs={DurationMs} planned={PlannedActionCount} executed={ExecutionResultCount} failed={FailedExecutionCount}",
                entry.RequestId,
                entry.Route,
                entry.AgentName,
                entry.Status,
                entry.PromptLength,
                entry.DurationMs,
                entry.PlannedActionCount,
                entry.ExecutionResultCount,
                entry.FailedExecutionCount);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var entry = CreateEntry(
                context.Request,
                requestId,
                stopwatch.ElapsedMilliseconds,
                "error",
                context.Response,
                ex);

            await requestLog.AppendAsync(entry, CancellationToken.None);
            logger.LogError(
                ex,
                "Demo chat request {RequestId} failed: route={Route} agent={AgentName} promptLength={PromptLength} durationMs={DurationMs}",
                entry.RequestId,
                entry.Route,
                entry.AgentName,
                entry.PromptLength,
                entry.DurationMs);

            throw;
        }
    }

    private DemoChatRequestLogEntry CreateEntry(
        AgentTurnRequest request,
        string requestId,
        long durationMs,
        string status,
        AgentTurnResponse? response,
        Exception? exception = null)
    {
        var executionPlan = response?.ExecutionPlan;
        var usage = response?.Usage;
        IReadOnlyList<AgentExecutionStep> executionSteps = executionPlan?.Steps ?? [];
        var failedExecutionCount = executionSteps.Count(static step =>
            step.Status is not AgentExecutionStepStatus.Completed);
        var estimatedCost = EstimateCost(usage?.InputTokenCount, usage?.OutputTokenCount);

        return new DemoChatRequestLogEntry
        {
            RequestId = requestId,
            Route = TryGetContext(request, AgentRuntimeContextKeys.CurrentRoute),
            AgentName = response?.AgentName,
            RequestedAgentName = request.AgentName,
            SessionHash = HashIdentifier(request.GetEffectiveSessionId()),
            PromptLength = request.UserMessage.Length,
            PromptPreview = _options.IncludePromptPreview
                ? BuildPromptPreview(request.UserMessage, _options.PromptPreviewMaxLength)
                : null,
            DurationMs = durationMs,
            Status = status,
            ResponseLength = response?.ResponseText.Length ?? 0,
            Model = _agentOptions.Provider.Model,
            PromptTokens = usage?.InputTokenCount,
            CompletionTokens = usage?.OutputTokenCount,
            TotalTokens = usage?.TotalTokenCount,
            EstimatedCost = estimatedCost,
            EstimatedCostCurrency = estimatedCost is null ? null : "USD",
            RequiresApproval = response?.RequiresApproval ?? false,
            RequiresClarification = response?.RequiresClarification ?? false,
            PlannedActionCount = executionPlan?.Steps.Count ?? response?.PlannedActions.Count ?? 0,
            ExecutionResultCount = executionSteps.Count > 0
                ? executionSteps.Count
                : response?.ExecutionResults.Count ?? 0,
            FailedExecutionCount = executionSteps.Count > 0
                ? failedExecutionCount
                : response?.ExecutionResults.Count(static result => !result.Succeeded) ?? 0,
            ErrorType = exception?.GetType().Name,
            ErrorMessage = exception?.Message
        };
    }

    private decimal? EstimateCost(long? inputTokens, long? outputTokens)
    {
        if (inputTokens is null && outputTokens is null)
        {
            return null;
        }

        var inputCost = (inputTokens ?? 0) / 1_000_000m * _options.InputTokenCostPerMillion;
        var outputCost = (outputTokens ?? 0) / 1_000_000m * _options.OutputTokenCostPerMillion;
        return Math.Round(inputCost + outputCost, 8, MidpointRounding.AwayFromZero);
    }

    private static string? TryGetContext(AgentTurnRequest request, string key)
    {
        return request.Context is not null &&
               request.Context.TryGetValue(key, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static string HashIdentifier(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string BuildPromptPreview(string message, int maxLength)
    {
        var normalized = message.ReplaceLineEndings(" ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(1, maxLength)] + "...";
    }
}
