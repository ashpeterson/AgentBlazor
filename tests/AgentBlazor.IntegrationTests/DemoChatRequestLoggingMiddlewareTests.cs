using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Middleware;
using AgentBlazor.Demo.Configuration;
using AgentBlazor.Demo.Services;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentBlazor.IntegrationTests;

public class DemoChatRequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenDailyCostLimitExceeded_ShortCircuitsWithoutCallingNext()
    {
        using var directory = new TempLogDirectory();
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToString("O");
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "chat-requests.jsonl"),
            $$"""
            {"timestampUtc":"{{today}}","estimated_cost":0.03}

            """);
        var middleware = CreateMiddleware(directory.Path, dailyCostLimitUsd: 0.01m);
        var context = new AgentTurnContext(new AgentTurnRequest("hello", AgentName: "Support Inbox Agent"));
        var nextCalled = false;

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

        Assert.False(nextCalled);
        Assert.NotNull(context.Response);
        Assert.Contains("usage cap", context.Response!.ResponseText, StringComparison.OrdinalIgnoreCase);
        var lines = await File.ReadAllLinesAsync(Path.Combine(directory.Path, "chat-requests.jsonl"));
        Assert.Contains(lines, static line => line.Contains("\"status\":\"cost-limit\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_WhenDailyCostLimitNotExceeded_CallsNextAndLogsTurn()
    {
        using var directory = new TempLogDirectory();
        var middleware = CreateMiddleware(directory.Path, dailyCostLimitUsd: 1.00m);
        var context = new AgentTurnContext(new AgentTurnRequest("hello", AgentName: "Support Inbox Agent"));

        await middleware.InvokeAsync(
            context,
            _ =>
            {
                context.Response = new AgentTurnResponse("Support Inbox Agent", "hello back", [], []);
                return Task.CompletedTask;
            });

        Assert.NotNull(context.Response);
        var line = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(directory.Path, "chat-requests.jsonl")));
        Assert.Contains("\"status\":\"ok\"", line, StringComparison.Ordinal);
    }

    private static DemoChatRequestLoggingMiddleware CreateMiddleware(string directoryPath, decimal dailyCostLimitUsd)
    {
        var options = new DemoLoggingOptions
        {
            DirectoryPath = directoryPath,
            DailyCostLimitEnabled = true,
            DailyCostLimitUsd = dailyCostLimitUsd
        };

        var requestLog = new JsonlDemoChatRequestLog(Microsoft.Extensions.Options.Options.Create(options));
        return new DemoChatRequestLoggingMiddleware(
            requestLog,
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Options.Options.Create(new AgentBlazorOptions()),
            NullLogger<DemoChatRequestLoggingMiddleware>.Instance);
    }

    private sealed class TempLogDirectory : IDisposable
    {
        public TempLogDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentblazor-demo-tests-{Guid.NewGuid():n}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
