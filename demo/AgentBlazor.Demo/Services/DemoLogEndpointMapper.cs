using System.Text.Json;
using AgentBlazor.Demo.Configuration;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

internal static class DemoLogEndpointMapper
{
    public static void MapDemoLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/internal/demo-logs");

        group.MapGet("/", async (
            HttpContext httpContext,
            IDemoChatRequestLog requestLog,
            IOptions<DemoLoggingOptions> options,
            int? lines,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, options.Value))
            {
                return Results.NotFound();
            }

            var tail = await requestLog.ReadTailAsync(lines ?? 200, cancellationToken);
            return Results.Text(string.Join(Environment.NewLine, tail), "application/x-ndjson");
        });

        group.MapGet("/traffic", async (
            HttpContext httpContext,
            IDemoTrafficLog trafficLog,
            IOptions<DemoLoggingOptions> options,
            int? lines,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, options.Value))
            {
                return Results.NotFound();
            }

            var tail = await trafficLog.ReadTailAsync(lines ?? 200, cancellationToken);
            return Results.Text(string.Join(Environment.NewLine, tail), "application/x-ndjson");
        });

        group.MapGet("/summary", async (
            HttpContext httpContext,
            IDemoChatRequestLog requestLog,
            IDemoTrafficLog trafficLog,
            IOptions<DemoLoggingOptions> options,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, options.Value))
            {
                return Results.NotFound();
            }

            var trafficLines = await trafficLog.ReadAllAsync(cancellationToken);
            var chatLines = await requestLog.ReadAllAsync(cancellationToken);
            return Results.Json(BuildSummary(trafficLines, chatLines));
        });

        group.MapGet("/download", (
            HttpContext httpContext,
            IDemoChatRequestLog requestLog,
            IOptions<DemoLoggingOptions> options) =>
        {
            if (!IsAuthorized(httpContext, options.Value))
            {
                return Results.NotFound();
            }

            return File.Exists(requestLog.LogFilePath)
                ? Results.File(requestLog.LogFilePath, "application/x-ndjson", "agentblazor-demo-chat-requests.jsonl")
                : Results.NotFound();
        });

        group.MapGet("/traffic/download", (
            HttpContext httpContext,
            IDemoTrafficLog trafficLog,
            IOptions<DemoLoggingOptions> options) =>
        {
            if (!IsAuthorized(httpContext, options.Value))
            {
                return Results.NotFound();
            }

            return File.Exists(trafficLog.LogFilePath)
                ? Results.File(trafficLog.LogFilePath, "application/x-ndjson", "agentblazor-demo-traffic-requests.jsonl")
                : Results.NotFound();
        });
    }

    private static bool IsAuthorized(HttpContext httpContext, DemoLoggingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccessToken))
        {
            return false;
        }

        if (httpContext.Request.Headers.TryGetValue("X-Demo-Log-Token", out var headerValues) &&
            string.Equals(headerValues.ToString(), options.AccessToken, StringComparison.Ordinal))
        {
            return true;
        }

        return httpContext.Request.Query.TryGetValue("token", out var queryValues) &&
               string.Equals(queryValues.ToString(), options.AccessToken, StringComparison.Ordinal);
    }

    private static object BuildSummary(IReadOnlyList<string> trafficLines, IReadOnlyList<string> chatLines)
    {
        var traffic = trafficLines
            .Select(ParseTraffic)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .Where(static item => IsPageViewRoute(item.Path))
            .ToArray();
        var chats = chatLines
            .Select(ParseChat)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        return new
        {
            generatedUtc = now,
            traffic = new
            {
                totalPageViews = traffic.Length,
                uniqueVisitors = traffic.Select(static item => item.VisitorHash).Distinct(StringComparer.Ordinal).Count(),
                last24hPageViews = traffic.Count(item => item.TimestampUtc >= now.AddHours(-24)),
                last24hUniqueVisitors = traffic
                    .Where(item => item.TimestampUtc >= now.AddHours(-24))
                    .Select(static item => item.VisitorHash)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                topRoutes = traffic
                    .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static group => group.Count())
                    .Take(10)
                    .Select(static group => new { path = group.Key, count = group.Count() })
            },
            chat = new
            {
                totalTurns = chats.Length,
                uniqueChatSessions = chats.Select(static item => item.SessionHash).Distinct(StringComparer.Ordinal).Count(),
                last24hTurns = chats.Count(item => item.TimestampUtc >= now.AddHours(-24)),
                approvalTurns = chats.Count(static item => item.RequiresApproval),
                failedExecutionTurns = chats.Count(static item => item.FailedExecutionCount > 0),
                averageDurationMs = chats.Length == 0 ? 0 : Math.Round(chats.Average(static item => item.DurationMs), 1),
                topRoutes = chats
                    .Where(static item => !string.IsNullOrWhiteSpace(item.Route))
                    .GroupBy(static item => item.Route!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(static group => group.Count())
                    .Take(10)
                    .Select(static group => new { route = group.Key, count = group.Count() })
            }
        };
    }

    private static TrafficSummaryItem? ParseTraffic(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return new TrafficSummaryItem(
                root.GetProperty("timestampUtc").GetDateTimeOffset(),
                root.GetProperty("path").GetString() ?? "/",
                root.GetProperty("visitorHash").GetString() ?? "unknown");
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static ChatSummaryItem? ParseChat(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            return new ChatSummaryItem(
                root.GetProperty("timestampUtc").GetDateTimeOffset(),
                root.TryGetProperty("route", out var route) ? route.GetString() : null,
                root.GetProperty("sessionHash").GetString() ?? "unknown",
                root.GetProperty("durationMs").GetInt64(),
                root.TryGetProperty("requiresApproval", out var requiresApproval) && requiresApproval.GetBoolean(),
                root.TryGetProperty("failedExecutionCount", out var failedExecutionCount) ? failedExecutionCount.GetInt32() : 0);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private sealed record TrafficSummaryItem(DateTimeOffset TimestampUtc, string Path, string VisitorHash);

    private static bool IsPageViewRoute(string path)
    {
        return !path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) &&
               !path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) &&
               !path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) &&
               !path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase) &&
               !Path.HasExtension(path);
    }

    private sealed record ChatSummaryItem(
        DateTimeOffset TimestampUtc,
        string? Route,
        string SessionHash,
        long DurationMs,
        bool RequiresApproval,
        int FailedExecutionCount);
}
