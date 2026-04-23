using AgentBlazor;
using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Paid.Analytics;
using AgentBlazor.Core.Paid.Audit;
using AgentBlazor.Core.Paid.Suggestions;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Tests for UseProLicense() license key activation (2a).
/// </summary>
public class UseProLicenseTests
{
    [Fact]
    public void UseProLicense_ValidPaidKey_SetsTierToPaid()
    {
        var services = new ServiceCollection();

        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-PRO-VALID-KEY-12345678");
        });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.Equal(AgentBlazorTier.Paid, opts.LicensedTier);
    }

    [Fact]
    public void UseProLicense_ValidEnterpriseKey_SetsTierToPremium()
    {
        var services = new ServiceCollection();

        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-ENT-VALID-KEY-12345678");
        });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.Equal(AgentBlazorTier.Premium, opts.LicensedTier);
    }

    [Fact]
    public void UseProLicense_InvalidPrefix_Throws()
    {
        var options = new AgentBlazorRegistrationOptions();

        var ex = Assert.Throws<ArgumentException>(() =>
            options.UseProLicense("INVALID-KEY-1234567890"));

        Assert.Contains("AB-PRO-", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UseProLicense_TooShort_Throws()
    {
        var options = new AgentBlazorRegistrationOptions();

        Assert.Throws<ArgumentException>(() =>
            options.UseProLicense("AB-PRO-SHORT"));
    }

    [Fact]
    public void UseProLicense_EmptyKey_Throws()
    {
        var options = new AgentBlazorRegistrationOptions();

        Assert.Throws<ArgumentException>(() =>
            options.UseProLicense(""));
    }

    [Fact]
    public void NoLicense_DefaultTier_IsFree()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services);

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.Equal(AgentBlazorTier.Free, opts.LicensedTier);
    }

    [Fact]
    public async Task UseProLicense_OverridesDiWithPaidHistoryStore()
    {
        var services = new ServiceCollection();

        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-PRO-VALID-KEY-12345678");
        });

        var provider = services.BuildServiceProvider();
        try
        {
            var store = provider.GetRequiredService<IActionHistoryStore>();

            Assert.IsType<SqliteActionHistoryStore>(store);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task UseProLicense_PersistsPaidDataAcrossServiceProviderRestart()
    {
        var dataDirectory = CreateTempDataDirectory();
        ServiceProvider? restartedProvider = null;

        try
        {
            await using (var provider = CreatePaidProvider(dataDirectory))
            {
                var historyStore = provider.GetRequiredService<IActionHistoryStore>();
                var inspectorStore = provider.GetRequiredService<IAgentInspectorStore>();
                var analyticsService = provider.GetRequiredService<IUsageAnalyticsService>();
                var auditService = provider.GetRequiredService<IAuditLogService>();
                var suggestionService = provider.GetRequiredService<ISmartSuggestionService>();

                Assert.IsType<SqliteActionHistoryStore>(historyStore);
                Assert.IsType<SqliteAgentInspectorStore>(inspectorStore);
                Assert.IsType<SqliteUsageAnalyticsService>(analyticsService);
                Assert.IsType<SqliteAuditLogService>(auditService);
                Assert.IsType<SqliteSmartSuggestionService>(suggestionService);

                await SeedPaidDataAsync(historyStore, inspectorStore, auditService);
            }

            Assert.True(File.Exists(Path.Combine(dataDirectory, "agentblazor-history.db")));
            Assert.True(File.Exists(Path.Combine(dataDirectory, "agentblazor-inspector.db")));
            Assert.True(File.Exists(Path.Combine(dataDirectory, "agentblazor-audit.db")));

            restartedProvider = CreatePaidProvider(dataDirectory);
            var restartedHistoryStore = restartedProvider.GetRequiredService<IActionHistoryStore>();
            var restartedInspectorStore = restartedProvider.GetRequiredService<IAgentInspectorStore>();
            var restartedAnalyticsService = restartedProvider.GetRequiredService<IUsageAnalyticsService>();
            var restartedAuditService = restartedProvider.GetRequiredService<IAuditLogService>();
            var restartedSuggestionService = restartedProvider.GetRequiredService<ISmartSuggestionService>();

            var recentEntries = await restartedHistoryStore.GetRecentAsync("session-alpha", limit: 10);
            Assert.Equal(2, recentEntries.Count);
            Assert.Contains(recentEntries, static entry => entry.ActionId == "load_dashboard");
            Assert.Contains(recentEntries, static entry => entry.ActionId == "create_report");

            var inspectorRuns = restartedInspectorStore.GetRecentRuns("session-alpha", limit: 10);
            var run = Assert.Single(inspectorRuns);
            Assert.Equal("run-alpha", run.RunId);
            Assert.True(run.Succeeded);

            var auditEvents = await restartedAuditService.GetRecentAsync(limit: 10);
            var auditEvent = Assert.Single(auditEvents);
            Assert.Equal(AuditEventType.ActionApproved, auditEvent.EventType);
            Assert.Equal("create_report", auditEvent.TargetId);
            Assert.Equal("paid.user@example.com", auditEvent.UserEmail);

            var summary = await restartedAnalyticsService.GetSummaryAsync(DateRange.Last30Days);
            Assert.Equal(6, summary.TotalActions);
            Assert.Equal(1, summary.UniqueUsers);
            Assert.Equal(3, summary.UniqueSessions);

            var topActions = await restartedAnalyticsService.GetTopActionsAsync(10);
            Assert.Contains(topActions, static action => action.ActionId == "create_report" && action.ExecutionCount == 3);

            var patterns = await restartedSuggestionService.GetPatternsAsync("paid-user", limit: 10);
            Assert.Contains(
                patterns,
                static pattern => pattern.PrecedingActions.SequenceEqual(["load_dashboard"]) &&
                                  pattern.NextAction == "create_report" &&
                                  pattern.Occurrences == 3);

            var routeSuggestions = await restartedSuggestionService.GetPopularForRouteAsync("/reports", limit: 5);
            Assert.Contains(routeSuggestions, static suggestion => suggestion.ActionId == "create_report");
        }
        finally
        {
            if (restartedProvider is not null)
            {
                await restartedProvider.DisposeAsync();
            }

            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task UseProLicense_HandlesConcurrentMultiUserPaidStorage()
    {
        var dataDirectory = CreateTempDataDirectory();

        try
        {
            await using var provider = CreatePaidProvider(dataDirectory);
            var historyStore = provider.GetRequiredService<IActionHistoryStore>();
            var inspectorStore = provider.GetRequiredService<IAgentInspectorStore>();
            var analyticsService = provider.GetRequiredService<IUsageAnalyticsService>();
            var auditService = provider.GetRequiredService<IAuditLogService>();
            var suggestionService = provider.GetRequiredService<ISmartSuggestionService>();

            const int userCount = 4;
            const int sessionsPerUser = 3;
            const int actionsPerSession = 4;
            var totalSessions = userCount * sessionsPerUser;
            var totalActions = totalSessions * actionsPerSession;

            var tasks = Enumerable.Range(1, userCount)
                .SelectMany(userIndex => Enumerable.Range(1, sessionsPerUser)
                    .Select(sessionIndex => SeedConcurrentPaidSessionAsync(
                        userIndex,
                        sessionIndex,
                        historyStore,
                        inspectorStore,
                        auditService)))
                .ToArray();

            await Task.WhenAll(tasks);

            for (var userIndex = 1; userIndex <= userCount; userIndex++)
            {
                var userId = $"paid-user-{userIndex}";
                var userEntries = await historyStore.GetByUserAsync(userId, limit: 100);
                Assert.Equal(sessionsPerUser * actionsPerSession, userEntries.Count);
                Assert.All(userEntries, entry => Assert.Equal(userId, entry.UserId));

                var userAuditEvents = await auditService.GetByUserAsync(userId, limit: 100);
                Assert.Equal(sessionsPerUser * actionsPerSession, userAuditEvents.Count);
                Assert.All(userAuditEvents, evt => Assert.Equal(userId, evt.UserId));
            }

            var summary = await analyticsService.GetSummaryAsync(DateRange.Last30Days);
            Assert.Equal(totalActions, summary.TotalActions);
            Assert.Equal(userCount, summary.UniqueUsers);
            Assert.Equal(totalSessions, summary.UniqueSessions);
            Assert.Equal(0.75, summary.SuccessRate, precision: 3);

            var topActions = await analyticsService.GetTopActionsAsync(limit: 10);
            Assert.Contains(topActions, action => action.ActionId == "load_dashboard" && action.ExecutionCount == totalSessions);
            Assert.Contains(topActions, action => action.ActionId == "create_report" && action.ExecutionCount == totalSessions);

            var agentPerformance = await analyticsService.GetAgentPerformanceAsync();
            var analyticsAgent = Assert.Single(agentPerformance);
            Assert.Equal("analytics-agent", analyticsAgent.AgentId);
            Assert.Equal(totalActions, analyticsAgent.TotalActions);
            Assert.Equal(userCount, analyticsAgent.UniqueUsers);

            var patterns = await suggestionService.GetPatternsAsync("paid-user-1", limit: 10);
            Assert.Contains(
                patterns,
                pattern => pattern.PrecedingActions.SequenceEqual(["load_dashboard"]) &&
                           pattern.NextAction == "create_report" &&
                           pattern.Occurrences == sessionsPerUser);

            var routeSuggestions = await suggestionService.GetPopularForRouteAsync("/reports", limit: 5);
            Assert.Contains(routeSuggestions, suggestion => suggestion.ActionId == "load_dashboard");

            var inspectedSession = inspectorStore.GetRecentRuns("paid-user-1-session-1", limit: 10);
            var inspectedRun = Assert.Single(inspectedSession);
            Assert.Equal("paid-user-1-session-1-run", inspectedRun.RunId);
            Assert.True(inspectedRun.Succeeded);
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public async Task RemovingProLicense_UsesFreeServices_WithoutMutatingExistingPaidData()
    {
        var dataDirectory = CreateTempDataDirectory();

        try
        {
            await using (var paidProvider = CreatePaidProvider(dataDirectory))
            {
                var historyStore = paidProvider.GetRequiredService<IActionHistoryStore>();
                var inspectorStore = paidProvider.GetRequiredService<IAgentInspectorStore>();
                var auditService = paidProvider.GetRequiredService<IAuditLogService>();

                await SeedPaidDataAsync(historyStore, inspectorStore, auditService);
            }

            var historyPath = Path.Combine(dataDirectory, "agentblazor-history.db");
            var inspectorPath = Path.Combine(dataDirectory, "agentblazor-inspector.db");
            var auditPath = Path.Combine(dataDirectory, "agentblazor-audit.db");

            Assert.True(File.Exists(historyPath));
            Assert.True(File.Exists(inspectorPath));
            Assert.True(File.Exists(auditPath));

            var historyCountBefore = await CountRowsAsync(historyPath, "action_history");
            var inspectorCountBefore = await CountRowsAsync(inspectorPath, "inspector_runs");
            var auditCountBefore = await CountRowsAsync(auditPath, "audit_log");

            await using (var freeProvider = CreateFreeProvider())
            {
                var options = freeProvider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
                Assert.Equal(AgentBlazorTier.Free, options.LicensedTier);

                var historyStore = freeProvider.GetRequiredService<IActionHistoryStore>();
                var inspectorStore = freeProvider.GetRequiredService<IAgentInspectorStore>();
                var analyticsService = freeProvider.GetRequiredService<IUsageAnalyticsService>();
                var auditService = freeProvider.GetRequiredService<IAuditLogService>();
                var suggestionService = freeProvider.GetRequiredService<ISmartSuggestionService>();

                Assert.IsType<NullActionHistoryStore>(historyStore);
                Assert.IsType<NullAgentInspectorStore>(inspectorStore);
                Assert.IsType<NullUsageAnalyticsService>(analyticsService);
                Assert.IsType<NullAuditLogService>(auditService);
                Assert.IsType<NullSmartSuggestionService>(suggestionService);

                await historyStore.RecordAsync(new ActionHistoryEntry(
                    "free-session",
                    "free-user",
                    DateTimeOffset.UtcNow,
                    "open dashboard",
                    "load_dashboard",
                    "analytics-agent",
                    new Dictionary<string, object?>()));
                Assert.Empty(await historyStore.GetRecentAsync("free-session"));
                Assert.Empty(inspectorStore.GetRecentRuns("session-alpha"));
                Assert.Empty(await auditService.GetRecentAsync(limit: 10));
                Assert.Equal(0, (await analyticsService.GetSummaryAsync(DateRange.Last30Days)).TotalActions);
                Assert.Empty(await suggestionService.GetSuggestionsAsync(
                    new SuggestionContext("free-session", "free-user", "/reports", ["load_dashboard"])));
            }

            var historyCountAfter = await CountRowsAsync(historyPath, "action_history");
            var inspectorCountAfter = await CountRowsAsync(inspectorPath, "inspector_runs");
            var auditCountAfter = await CountRowsAsync(auditPath, "audit_log");

            Assert.Equal(historyCountBefore, historyCountAfter);
            Assert.Equal(inspectorCountBefore, inspectorCountAfter);
            Assert.Equal(auditCountBefore, auditCountAfter);

            await using var paidProviderAgain = CreatePaidProvider(dataDirectory);
            var restartedHistoryStore = paidProviderAgain.GetRequiredService<IActionHistoryStore>();
            var restartedInspectorStore = paidProviderAgain.GetRequiredService<IAgentInspectorStore>();
            var restartedAuditService = paidProviderAgain.GetRequiredService<IAuditLogService>();

            Assert.Equal(6, (await restartedHistoryStore.GetByUserAsync("paid-user")).Count);
            Assert.Single(restartedInspectorStore.GetRecentRuns("session-alpha", limit: 10));
            Assert.Single(await restartedAuditService.GetRecentAsync(limit: 10));
        }
        finally
        {
            DeleteDirectoryIfPresent(dataDirectory);
        }
    }

    [Fact]
    public void NoLicense_UsesNullHistoryStore()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services);

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IActionHistoryStore>();

        // Free tier uses the no-op store
        Assert.IsNotType<InMemoryActionHistoryStore>(store);
    }

    [Fact]
    public void UseDevTools_EnablesDevToolsOptions()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseDevTools(autoShow: true);
        });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.True(opts.EnableDevTools);
        Assert.True(opts.AutoShowDevTools);
    }

    [Fact]
    public void UseDevTools_RegistersInMemoryInspectorStore()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseDevTools();
        });

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAgentInspectorStore>();

        Assert.IsType<InMemoryAgentInspectorStore>(store);
    }

    private static ServiceProvider CreatePaidProvider(string dataDirectory)
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-PRO-VALID-KEY-12345678", dataDirectory);
        });

        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreateFreeProvider()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services);
        return services.BuildServiceProvider();
    }

    private static async Task<long> CountRowsAsync(string dbPath, string tableName)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName}";

        var result = await command.ExecuteScalarAsync();
        return result switch
        {
            long value => value,
            int value => value,
            _ => 0
        };
    }

    private static async Task SeedPaidDataAsync(
        IActionHistoryStore historyStore,
        IAgentInspectorStore inspectorStore,
        IAuditLogService auditService)
    {
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

        inspectorStore.RecordRun(new InspectorRunRecord(
            "run-alpha",
            "session-alpha",
            "analytics-agent",
            startedAt,
            startedAt + TimeSpan.FromSeconds(30),
            "System prompt",
            "Plan response",
            [new InspectorEvent(startedAt, "tool", "AgentGrid", "filter", "Filtered reports")],
            true,
            null));

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

    private static async Task SeedConcurrentPaidSessionAsync(
        int userIndex,
        int sessionIndex,
        IActionHistoryStore historyStore,
        IAgentInspectorStore inspectorStore,
        IAuditLogService auditService)
    {
        var userId = $"paid-user-{userIndex}";
        var sessionId = $"{userId}-session-{sessionIndex}";
        var startedAt = DateTimeOffset.UtcNow
            .AddMinutes(-(userIndex * 10 + sessionIndex));

        var actions = new[]
        {
            new SeededAction("open dashboard", "load_dashboard", true, 120),
            new SeededAction("create report", "create_report", true, 240),
            new SeededAction("approve export", "approve_export", true, 180),
            new SeededAction("sync external report", "sync_report", false, 320)
        };

        for (var actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            var action = actions[actionIndex];
            await historyStore.RecordAsync(new ActionHistoryEntry(
                sessionId,
                userId,
                startedAt + TimeSpan.FromSeconds(actionIndex * 10),
                action.UserMessage,
                action.ActionId,
                "analytics-agent",
                new Dictionary<string, object?>
                {
                    ["userIndex"] = userIndex,
                    ["sessionIndex"] = sessionIndex,
                    ["actionIndex"] = actionIndex
                },
                action.Succeeded,
                TimeSpan.FromMilliseconds(action.DurationMs),
                "/reports",
                action.Succeeded ? null : "External report service timed out"));

            await auditService.LogActionAsync(
                userId,
                $"{userId}@example.com",
                action.ActionId,
                "analytics-agent",
                action.Succeeded,
                action.Succeeded ? null : "External report service timed out",
                "127.0.0.1");
        }

        inspectorStore.RecordRun(new InspectorRunRecord(
            $"{sessionId}-run",
            sessionId,
            "analytics-agent",
            startedAt,
            startedAt + TimeSpan.FromSeconds(45),
            "System prompt",
            "Plan response",
            [new InspectorEvent(startedAt, "tool", "AgentGrid", "load_dashboard", "Loaded reports")],
            true,
            null));
    }

    private sealed record SeededAction(
        string UserMessage,
        string ActionId,
        bool Succeeded,
        int DurationMs);

    private static string CreateTempDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentblazor-paid-tests", Guid.NewGuid().ToString("N"));
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
