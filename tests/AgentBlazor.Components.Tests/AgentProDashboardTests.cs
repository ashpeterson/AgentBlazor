using AgentBlazor.Components;
using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Paid.Analytics;
using AgentBlazor.Core.Paid.Audit;
using AgentBlazor.Core.Paid.Suggestions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Components.Tests;

public sealed class AgentProDashboardTests : TestContext
{
    [Fact]
    public async Task Render_ShowsPersistedPaidDataAcrossOverviewAuditAndPatternsTabs()
    {
        var dataDirectory = CreateTempDataDirectory();
        SqliteUsageAnalyticsService? analyticsService = null;
        SqliteAuditLogService? auditService = null;
        SqliteSmartSuggestionService? suggestionService = null;
        IRenderedComponent<AgentProDashboard>? cut = null;

        try
        {
            await SeedPersistedDataAsync(dataDirectory);

            analyticsService = SqliteUsageAnalyticsService.CreateWithPath(
                Path.Combine(dataDirectory, "agentblazor-history.db"));
            auditService = SqliteAuditLogService.CreateWithPath(
                Path.Combine(dataDirectory, "agentblazor-audit.db"));
            suggestionService = SqliteSmartSuggestionService.CreateWithPath(
                Path.Combine(dataDirectory, "agentblazor-history.db"));

            Services.AddSingleton<IUsageAnalyticsService>(analyticsService);
            Services.AddSingleton<IAuditLogService>(auditService);
            Services.AddSingleton<ISmartSuggestionService>(suggestionService);

            cut = RenderComponent<AgentProDashboard>(parameters => parameters
                .Add(component => component.Title, "Agent Intelligence Dashboard")
                .Add(component => component.DaysRange, 30));

            cut.WaitForAssertion(() =>
            {
                Assert.Contains("Agent Intelligence Dashboard", cut.Markup);
                Assert.Contains("Total Actions", cut.Markup);
                Assert.Contains(">6<", cut.Markup);
                Assert.Contains("load_dashboard", cut.Markup);
            });

            cut.FindAll("button.ab-dashboard__tab")[1].Click();
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("create_report", cut.Markup);
                Assert.Contains("analytics-agent", cut.Markup);
            });

            cut.FindAll("button.ab-dashboard__tab")[2].Click();
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("ActionApproved", cut.Markup);
                Assert.Contains("paid.user@example.com", cut.Markup);
                Assert.Contains("Approved create_report for export", cut.Markup);
            });

            cut.FindAll("button.ab-dashboard__tab")[3].Click();
            cut.WaitForAssertion(() =>
            {
                Assert.Contains("load_dashboard", cut.Markup);
                Assert.Contains("create_report", cut.Markup);
                Assert.Contains("3 occurrences", cut.Markup);
            });
        }
        finally
        {
            cut?.Dispose();

            if (suggestionService is not null)
            {
                await suggestionService.DisposeAsync();
            }

            if (auditService is not null)
            {
                await auditService.DisposeAsync();
            }

            if (analyticsService is not null)
            {
                await analyticsService.DisposeAsync();
            }

            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    private static async Task SeedPersistedDataAsync(string dataDirectory)
    {
        var historyPath = Path.Combine(dataDirectory, "agentblazor-history.db");
        var auditPath = Path.Combine(dataDirectory, "agentblazor-audit.db");

        await using var historyStore = SqliteActionHistoryStore.CreateWithPath(historyPath);
        await using var auditService = SqliteAuditLogService.CreateWithPath(auditPath);

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        for (var sessionIndex = 0; sessionIndex < 3; sessionIndex++)
        {
            var sessionId = sessionIndex switch
            {
                0 => "session-alpha",
                1 => "session-beta",
                _ => "session-gamma"
            };

            var sessionOffset = TimeSpan.FromMinutes(sessionIndex);
            await historyStore.RecordAsync(new ActionHistoryEntry(
                sessionId,
                "paid-user",
                startedAt + sessionOffset,
                "open dashboard",
                "load_dashboard",
                "analytics-agent",
                new Dictionary<string, object?> { ["source"] = "dashboard" },
                Succeeded: true,
                Duration: TimeSpan.FromMilliseconds(120),
                Route: "/reports"));

            await historyStore.RecordAsync(new ActionHistoryEntry(
                sessionId,
                "paid-user",
                startedAt + sessionOffset + TimeSpan.FromSeconds(10),
                "create report",
                "create_report",
                "analytics-agent",
                new Dictionary<string, object?> { ["format"] = "csv" },
                Succeeded: true,
                Duration: TimeSpan.FromMilliseconds(240),
                Route: "/reports"));
        }

        await auditService.LogAsync(new AuditEvent(
            Guid.NewGuid().ToString("N"),
            startedAt + TimeSpan.FromMinutes(5),
            "paid-user",
            "paid.user@example.com",
            AuditEventType.ActionApproved,
            "action",
            "create_report",
            "Approved create_report for export",
            "127.0.0.1",
            new Dictionary<string, object?> { ["agentId"] = "analytics-agent" }));
    }

    private static string CreateTempDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentblazor-dashboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
