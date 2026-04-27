using AgentBlazor.Components;
using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Paid.Analytics;
using AgentBlazor.Core.Paid.Audit;
using AgentBlazor.Core.Paid.Suggestions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

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
                .Add(component => component.DaysRange, 30)
                .Add(component => component.RequireAuthenticatedUser, false));

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

    [Fact]
    public void Render_WithoutAuthenticationState_ShowsUnauthorizedMessage_ByDefault()
    {
        Services.AddSingleton<IUsageAnalyticsService, NullUsageAnalyticsService>();
        Services.AddSingleton<IAuditLogService, NullAuditLogService>();
        Services.AddSingleton<ISmartSuggestionService, NullSmartSuggestionService>();

        var cut = RenderComponent<AgentProDashboard>(parameters => parameters
            .Add(component => component.Title, "Agent Intelligence Dashboard"));

        Assert.Contains("Restricted Pro Dashboard", cut.Markup);
        Assert.Contains("authorized operators or administrators", cut.Markup);
        Assert.DoesNotContain("Refresh", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_WithAllowedRole_RendersDashboardContent()
    {
        var dataDirectory = CreateTempDataDirectory();
        SqliteUsageAnalyticsService? analyticsService = null;
        SqliteAuditLogService? auditService = null;
        SqliteSmartSuggestionService? suggestionService = null;

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

            var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "ops@example.com"),
                new Claim(ClaimTypes.Role, "AgentBlazor.ProOperator")
            ], "TestAuth"));

            var authState = Task.FromResult(new AuthenticationState(user));
            RenderFragment fragment = builder =>
            {
                builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
                builder.AddAttribute(1, "Value", authState);
                builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
                {
                    childBuilder.OpenComponent<AgentProDashboard>(0);
                    childBuilder.AddAttribute(1, "Title", "Agent Intelligence Dashboard");
                    childBuilder.AddAttribute(2, "DaysRange", 30);
                    childBuilder.AddAttribute(3, "AllowedRoles", "AgentBlazor.ProOperator");
                    childBuilder.CloseComponent();
                }));
                builder.CloseComponent();
            };

            var cut = Render(fragment);

            cut.WaitForAssertion(() =>
            {
                Assert.Contains("Agent Intelligence Dashboard", cut.Markup);
                Assert.Contains("Total Actions", cut.Markup);
                Assert.DoesNotContain("Restricted Pro Dashboard", cut.Markup, StringComparison.Ordinal);
            });
        }
        finally
        {
            if (analyticsService is not null)
            {
                await analyticsService.DisposeAsync();
            }

            if (auditService is not null)
            {
                await auditService.DisposeAsync();
            }

            if (suggestionService is not null)
            {
                await suggestionService.DisposeAsync();
            }

            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task Render_WithFreshPaidDatabases_ShowsEmptyStateWithoutSqliteErrors()
    {
        var dataDirectory = CreateTempDataDirectory();
        SqliteUsageAnalyticsService? analyticsService = null;
        SqliteAuditLogService? auditService = null;
        SqliteSmartSuggestionService? suggestionService = null;

        try
        {
            analyticsService = SqliteUsageAnalyticsService.CreateWithPath(
                Path.Combine(dataDirectory, "agentblazor-history.db"));
            auditService = SqliteAuditLogService.CreateWithPath(
                Path.Combine(dataDirectory, "agentblazor-audit.db"));
            suggestionService = SqliteSmartSuggestionService.CreateWithPath(
                Path.Combine(dataDirectory, "agentblazor-history.db"));

            Services.AddSingleton<IUsageAnalyticsService>(analyticsService);
            Services.AddSingleton<IAuditLogService>(auditService);
            Services.AddSingleton<ISmartSuggestionService>(suggestionService);

            var cut = RenderComponent<AgentProDashboard>(parameters => parameters
                .Add(component => component.Title, "Agent Intelligence Dashboard")
                .Add(component => component.DaysRange, 30)
                .Add(component => component.RequireAuthenticatedUser, false));

            cut.WaitForAssertion(() =>
            {
                Assert.Contains("Agent Intelligence Dashboard", cut.Markup);
                Assert.DoesNotContain("Failed to load dashboard data", cut.Markup, StringComparison.Ordinal);
                Assert.True(
                    cut.Markup.Contains("Total Actions", StringComparison.Ordinal) ||
                    cut.Markup.Contains("No usage data available yet.", StringComparison.Ordinal));
            });
        }
        finally
        {
            if (analyticsService is not null)
            {
                await analyticsService.DisposeAsync();
            }

            if (auditService is not null)
            {
                await auditService.DisposeAsync();
            }

            if (suggestionService is not null)
            {
                await suggestionService.DisposeAsync();
            }

            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task Render_WithAuthenticatedUserMissingRequiredRole_ShowsUnauthorizedMessage()
    {
        Services.AddSingleton<IUsageAnalyticsService, NullUsageAnalyticsService>();
        Services.AddSingleton<IAuditLogService, NullAuditLogService>();
        Services.AddSingleton<ISmartSuggestionService, NullSmartSuggestionService>();

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "viewer@example.com"),
            new Claim(ClaimTypes.Role, "Viewer")
        ], "TestAuth"));

        var authState = Task.FromResult(new AuthenticationState(user));
        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authState);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<AgentProDashboard>(0);
                childBuilder.AddAttribute(1, "AllowedRoles", "AgentBlazor.ProOperator");
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        };

        var cut = Render(fragment);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Restricted Pro Dashboard", cut.Markup);
            Assert.Contains("authorized operators or administrators", cut.Markup);
            Assert.DoesNotContain("Total Actions", cut.Markup, StringComparison.Ordinal);
        });

        await Task.CompletedTask;
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
