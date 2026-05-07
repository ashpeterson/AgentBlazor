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
}
