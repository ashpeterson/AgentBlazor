using System.Text.Json;
using System.Text.Encodings.Web;
using AgentBlazor.Demo.Configuration;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

internal static class DemoLogEndpointMapper
{
    private const string AccessCookieName = "AgentBlazorDemoLogAccess";
    private const int DefaultViewLines = 50;

    public static void MapDemoLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/internal/demo-logs");

        group.MapGet("/login", (
            HttpContext httpContext,
            IOptions<DemoLoggingOptions> options,
            string? token) =>
        {
            if (!IsTokenValid(token, options.Value))
            {
                return Results.NotFound();
            }

            httpContext.Response.Cookies.Append(
                AccessCookieName,
                token!,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromDays(30),
                    IsEssential = true
                });

            return Results.Redirect("/internal/demo-logs/view");
        });

        group.MapGet("/logout", (HttpContext httpContext) =>
        {
            httpContext.Response.Cookies.Delete(AccessCookieName);
            return Results.Redirect("/");
        });

        group.MapGet("/view", async (
            HttpContext httpContext,
            IDemoChatRequestLog requestLog,
            IDemoTrafficLog trafficLog,
            IOptions<DemoLoggingOptions> options,
            int? lines,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(httpContext, options.Value))
            {
                return Results.NotFound();
            }

            var lineCount = ClampLineCount(lines ?? DefaultViewLines, options.Value);
            var trafficTail = await trafficLog.ReadTailAsync(lineCount, cancellationToken);
            var chatTail = await requestLog.ReadTailAsync(lineCount, cancellationToken);
            var trafficLines = await trafficLog.ReadAllAsync(cancellationToken);
            var chatLines = await requestLog.ReadAllAsync(cancellationToken);

            return Results.Content(
                BuildViewerHtml(BuildSummary(trafficLines, chatLines), trafficTail, chatTail, lineCount),
                "text/html; charset=utf-8");
        });

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

            var tail = await requestLog.ReadTailAsync(ClampLineCount(lines ?? 200, options.Value), cancellationToken);
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

            var tail = await trafficLog.ReadTailAsync(ClampLineCount(lines ?? 200, options.Value), cancellationToken);
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
            IsTokenValid(headerValues.ToString(), options))
        {
            return true;
        }

        if (httpContext.Request.Query.TryGetValue("token", out var queryValues) &&
            IsTokenValid(queryValues.ToString(), options))
        {
            return true;
        }

        return httpContext.Request.Cookies.TryGetValue(AccessCookieName, out var cookieValue) &&
               IsTokenValid(cookieValue, options);
    }

    private static bool IsTokenValid(string? token, DemoLoggingOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.AccessToken) &&
               string.Equals(token, options.AccessToken, StringComparison.Ordinal);
    }

    private static int ClampLineCount(int lines, DemoLoggingOptions options)
    {
        return Math.Clamp(lines, 1, options.MaxTailLines);
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
                promptTokens = chats.Sum(static item => item.PromptTokens ?? 0),
                completionTokens = chats.Sum(static item => item.CompletionTokens ?? 0),
                totalTokens = chats.Sum(static item => item.TotalTokens ?? 0),
                estimatedCost = Math.Round(chats.Sum(static item => item.EstimatedCost ?? 0), 8, MidpointRounding.AwayFromZero),
                estimatedCostCurrency = chats.Any(static item => item.EstimatedCost is not null) ? "USD" : null,
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
                root.TryGetProperty("failedExecutionCount", out var failedExecutionCount) ? failedExecutionCount.GetInt32() : 0,
                TryGetInt64(root, "prompt_tokens"),
                TryGetInt64(root, "completion_tokens"),
                TryGetInt64(root, "total_tokens"),
                TryGetDecimal(root, "estimated_cost"));
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
        int FailedExecutionCount,
        long? PromptTokens,
        long? CompletionTokens,
        long? TotalTokens,
        decimal? EstimatedCost);

    private static long? TryGetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.Number &&
               property.TryGetInt64(out var value)
            ? value
            : null;
    }

    private static decimal? TryGetDecimal(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.Number &&
               property.TryGetDecimal(out var value)
            ? value
            : null;
    }

    private static string BuildViewerHtml(object summary, IReadOnlyList<string> trafficTail, IReadOnlyList<string> chatTail, int lineCount)
    {
        var summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="robots" content="noindex,nofollow">
                <title>AgentBlazor Demo Logs</title>
                <style>
                    :root {
                        color-scheme: light;
                        --bg: #f6f3ed;
                        --panel: #fffaf0;
                        --ink: #201a14;
                        --muted: #6f6254;
                        --line: #ddcfbd;
                        --accent: #b85c38;
                        --code: #16130f;
                    }
                    * { box-sizing: border-box; }
                    body {
                        margin: 0;
                        background: radial-gradient(circle at top left, #fff6d7, transparent 32rem), var(--bg);
                        color: var(--ink);
                        font: 15px/1.5 ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                    }
                    main {
                        width: min(1180px, calc(100vw - 32px));
                        margin: 32px auto;
                    }
                    header {
                        display: flex;
                        align-items: flex-end;
                        justify-content: space-between;
                        gap: 16px;
                        margin-bottom: 20px;
                    }
                    h1 { margin: 0; font-size: clamp(28px, 4vw, 46px); letter-spacing: -0.04em; }
                    h2 { margin: 0 0 12px; font-size: 18px; }
                    p { margin: 6px 0 0; color: var(--muted); }
                    a, button {
                        color: var(--accent);
                        font: inherit;
                    }
                    .actions {
                        display: flex;
                        flex-wrap: wrap;
                        gap: 8px;
                    }
                    .button {
                        border: 1px solid var(--line);
                        border-radius: 999px;
                        background: var(--panel);
                        padding: 8px 12px;
                        text-decoration: none;
                        font-weight: 700;
                    }
                    .grid {
                        display: grid;
                        grid-template-columns: repeat(2, minmax(0, 1fr));
                        gap: 16px;
                    }
                    section {
                        border: 1px solid var(--line);
                        border-radius: 20px;
                        background: color-mix(in srgb, var(--panel) 92%, white);
                        box-shadow: 0 18px 60px rgb(60 38 20 / 10%);
                        padding: 18px;
                        overflow: hidden;
                    }
                    .wide { grid-column: 1 / -1; }
                    pre {
                        margin: 0;
                        max-height: 460px;
                        overflow: auto;
                        border-radius: 14px;
                        background: var(--code);
                        color: #fff8e8;
                        padding: 14px;
                        white-space: pre-wrap;
                        word-break: break-word;
                        font: 12px/1.55 ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
                    }
                    .raw-line {
                        padding: 10px 0;
                        border-top: 1px solid var(--line);
                        font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
                        font-size: 12px;
                        overflow-wrap: anywhere;
                    }
                    .raw-line:first-of-type { border-top: 0; }
                    .toolbar {
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        gap: 12px;
                        margin-bottom: 12px;
                    }
                    input {
                        width: 5.5rem;
                        border: 1px solid var(--line);
                        border-radius: 10px;
                        padding: 7px 9px;
                        background: white;
                    }
                    @media (max-width: 760px) {
                        header { align-items: flex-start; flex-direction: column; }
                        .grid { grid-template-columns: 1fr; }
                    }
                </style>
            </head>
            <body>
                <main>
                    <header>
                        <div>
                            <h1>Demo Logs</h1>
                            <p>Private viewer for traffic and chat diagnostics. Raw prompts, IPs, and user agents are not logged.</p>
                        </div>
                        <nav class="actions" aria-label="Log actions">
                            <a class="button" href="/internal/demo-logs/summary">Summary JSON</a>
                            <a class="button" href="/internal/demo-logs/traffic/download">Download traffic</a>
                            <a class="button" href="/internal/demo-logs/download">Download chat</a>
                            <a class="button" href="/internal/demo-logs/logout">Log out</a>
                        </nav>
                    </header>

                    <div class="grid">
                        <section class="wide">
                            <div class="toolbar">
                                <h2>Summary</h2>
                                <form method="get" action="/internal/demo-logs/view">
                                    <label>Lines <input name="lines" type="number" min="1" max="500" value="{{lineCount}}"></label>
                                    <button class="button" type="submit">Refresh</button>
                                </form>
                            </div>
                            <pre>{{Encode(summaryJson)}}</pre>
                        </section>

                        <section>
                            <h2>Recent Page Traffic</h2>
                            {{RenderLines(trafficTail)}}
                        </section>

                        <section>
                            <h2>Recent Chat Turns</h2>
                            {{RenderLines(chatTail)}}
                        </section>
                    </div>
                </main>
            </body>
            </html>
            """;
    }

    private static string RenderLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return "<p>No entries yet.</p>";
        }

        return string.Join(
            Environment.NewLine,
            lines.Reverse().Select(static line => $"""<div class="raw-line">{Encode(PrettyJsonLine(line))}</div>"""));
    }

    private static string PrettyJsonLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return line;
        }
    }

    private static string Encode(string value)
    {
        return HtmlEncoder.Default.Encode(value);
    }
}
