using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AgentBlazor.Demo.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoTrafficLoggingMiddleware(
    RequestDelegate next,
    IDemoTrafficLog trafficLog,
    IOptions<DemoLoggingOptions> options,
    ILogger<DemoTrafficLoggingMiddleware> logger)
{
    private readonly DemoLoggingOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || !ShouldLog(context.Request))
        {
            await next(context);
            return;
        }

        var requestId = Guid.NewGuid().ToString("n");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            var entry = new DemoTrafficLogEntry
            {
                RequestId = requestId,
                Path = context.Request.Path.Value ?? "/",
                Method = context.Request.Method,
                StatusCode = context.Response.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                VisitorHash = HashIdentifier(BuildVisitorFingerprint(context)),
                UserAgentHash = HashOptional(context.Request.Headers.UserAgent.ToString()),
                ReferrerHost = GetReferrerHost(context.Request)
            };

            await trafficLog.AppendAsync(entry, CancellationToken.None);
            logger.LogInformation(
                "Demo traffic {RequestId}: path={Path} status={StatusCode} durationMs={DurationMs} visitor={VisitorHash}",
                entry.RequestId,
                entry.Path,
                entry.StatusCode,
                entry.DurationMs,
                entry.VisitorHash);
        }
    }

    private static bool ShouldLog(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? "/";
        if (path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Path.HasExtension(path))
        {
            return false;
        }

        var accept = request.Headers.Accept.ToString();
        return string.IsNullOrWhiteSpace(accept) ||
               accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
               accept.Contains("*/*", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVisitorFingerprint(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        var ip = !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        return $"{ip}|{userAgent}";
    }

    private static string HashIdentifier(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string? HashOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : HashIdentifier(value);
    }

    private static string? GetReferrerHost(HttpRequest request)
    {
        var referrer = request.Headers[HeaderNames.Referer].ToString();
        return Uri.TryCreate(referrer, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
    }
}
