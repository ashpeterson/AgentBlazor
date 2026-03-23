using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.State;
using AgentBlazor.Options;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentBlazor.Core.Tests;

public class SharedStateStoreTests
{
    [Fact]
    public void SaveSnapshot_PersistsPerRun_AndReturnsLatestByDefault()
    {
        IAgentSharedStateStore store = new InMemoryAgentSharedStateStore();

        store.SaveSnapshot(
            "demo-agent",
            "thread-1",
            "run-1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Classic Scrambled Eggs"
            });

        store.SaveSnapshot(
            "demo-agent",
            "thread-1",
            "run-2",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Test"
            });

        var latest = store.GetSnapshot("demo-agent", "thread-1");
        var firstRun = store.GetSnapshot("demo-agent", "thread-1", "run-1");

        Assert.Equal("run-2", latest.RunId);
        Assert.Equal("Test", latest.Values["component.recipe.state.title"]);
        Assert.Equal("run-1", firstRun.RunId);
        Assert.Equal("Classic Scrambled Eggs", firstRun.Values["component.recipe.state.title"]);
    }

    [Fact]
    public void ApplyDelta_UpdatesAndRemovesKeys()
    {
        IAgentSharedStateStore store = new InMemoryAgentSharedStateStore();
        store.SaveSnapshot(
            "demo-agent",
            "thread-2",
            "run-7",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Classic Scrambled Eggs",
                ["component.recipe.state.minutes"] = "45",
                ["route.current"] = "/demo/workflows/recipe-release"
            });

        store.ApplyDelta(
            "demo-agent",
            "thread-2",
            "run-7",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Test",
                ["component.recipe.state.minutes"] = null
            });

        var snapshot = store.GetSnapshot("demo-agent", "thread-2", "run-7");

        Assert.Equal("Test", snapshot.Values["component.recipe.state.title"]);
        Assert.False(snapshot.Values.ContainsKey("component.recipe.state.minutes"));
        Assert.Equal("/demo/workflows/recipe-release", snapshot.Values["route.current"]);
    }

    [Fact]
    public void AssociateMessageWithRun_TracksMessageRunCorrelation()
    {
        IAgentSharedStateStore store = new InMemoryAgentSharedStateStore();
        store.SaveSnapshot(
            "demo-agent",
            "thread-3",
            "run-11",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Test"
            });

        store.AssociateMessageWithRun("demo-agent", "thread-3", "run-11:assistant:1", "run-11");

        var runId = store.GetRunIdForMessage("demo-agent", "thread-3", "run-11:assistant:1");
        var runIds = store.GetRunIdsForSession("demo-agent", "thread-3");

        Assert.Equal("run-11", runId);
        Assert.Contains("run-11", runIds);
    }

    [Fact]
    public void InMemoryStore_RejectsStaleWrites_WhenConfigured()
    {
        IAgentSharedStateStore store = new InMemoryAgentSharedStateStore(
            Microsoft.Extensions.Options.Options.Create(new SharedStateOptions
            {
                MergeMode = SharedStateMergeMode.RejectStaleWrites
            }));

        var now = DateTimeOffset.UtcNow;
        store.SaveSnapshot(
            "demo-agent",
            "thread-stale",
            "run-a",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Newer"
            },
            updatedAt: now);

        store.ApplyDelta(
            "demo-agent",
            "thread-stale",
            "run-a",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["component.recipe.state.title"] = "Older"
            },
            updatedAt: now.AddMinutes(-5));

        var snapshot = store.GetSnapshot("demo-agent", "thread-stale", "run-a");
        Assert.Equal("Newer", snapshot.Values["component.recipe.state.title"]);
    }

    [Fact]
    public async Task InMemoryStore_RejectStaleWrites_WithConcurrentDeltas_KeepsLatestTimestampValue()
    {
        IAgentSharedStateStore store = new InMemoryAgentSharedStateStore(
            Microsoft.Extensions.Options.Options.Create(new SharedStateOptions
            {
                MergeMode = SharedStateMergeMode.RejectStaleWrites
            }));

        var startedAt = DateTimeOffset.UtcNow;
        store.SaveSnapshot(
            "demo-agent",
            "thread-concurrency",
            "run-z",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["counter"] = "0"
            },
            updatedAt: startedAt);

        var work = Enumerable.Range(1, 40)
            .Select(i => Task.Run(() => store.ApplyDelta(
                "demo-agent",
                "thread-concurrency",
                "run-z",
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["counter"] = i.ToString(CultureInfo.InvariantCulture)
                },
                updatedAt: startedAt.AddMilliseconds(i))))
            .ToArray();
        await Task.WhenAll(work);

        var snapshot = store.GetSnapshot("demo-agent", "thread-concurrency", "run-z");
        Assert.Equal("40", snapshot.Values["counter"]);
    }

    [Fact]
    public void JsonFileStore_PersistsSnapshotAndMessageRunMappings()
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "agentblazor-tests",
            $"{Guid.NewGuid():N}",
            "shared-state.json");

        try
        {
            using (var store = new JsonFileAgentSharedStateStore(tempPath))
            {
                store.SaveSnapshot(
                    "demo-agent",
                    "thread-persist",
                    "run-1",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["component.recipe.state.title"] = "Persisted"
                    });
                store.AssociateMessageWithRun("demo-agent", "thread-persist", "run-1:assistant:1", "run-1");
            }

            using var reloaded = new JsonFileAgentSharedStateStore(tempPath);
            var snapshot = reloaded.GetSnapshot("demo-agent", "thread-persist", "run-1");
            var runId = reloaded.GetRunIdForMessage("demo-agent", "thread-persist", "run-1:assistant:1");

            Assert.Equal("Persisted", snapshot.Values["component.recipe.state.title"]);
            Assert.Equal("run-1", runId);
        }
        finally
        {
            var directory = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonFileStore_RejectStaleWrites_AcrossReloads()
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "agentblazor-tests",
            $"{Guid.NewGuid():N}",
            "shared-state-stale.json");

        var options = Microsoft.Extensions.Options.Options.Create(new SharedStateOptions
        {
            MergeMode = SharedStateMergeMode.RejectStaleWrites
        });
        var baseline = DateTimeOffset.UtcNow;

        try
        {
            using (var store = new JsonFileAgentSharedStateStore(tempPath, options))
            {
                store.SaveSnapshot(
                    "demo-agent",
                    "thread-reconnect",
                    "run-9",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = "initial"
                    },
                    updatedAt: baseline);
            }

            using (var reloaded = new JsonFileAgentSharedStateStore(tempPath, options))
            {
                reloaded.ApplyDelta(
                    "demo-agent",
                    "thread-reconnect",
                    "run-9",
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = "stale"
                    },
                    updatedAt: baseline.AddMinutes(-1));

                reloaded.ApplyDelta(
                    "demo-agent",
                    "thread-reconnect",
                    "run-9",
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = "fresh"
                    },
                    updatedAt: baseline.AddMinutes(1));
            }

            using var verify = new JsonFileAgentSharedStateStore(tempPath, options);
            var snapshot = verify.GetSnapshot("demo-agent", "thread-reconnect", "run-9");
            Assert.Equal("fresh", snapshot.Values["status"]);
        }
        finally
        {
            var directory = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Runtime_AppliesContextSharedStateSnapshotAndDelta()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new StaticResponseChatClient("Done."));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var sharedStore = provider.GetRequiredService<IAgentSharedStateStore>();

        var snapshotJson = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user.preference.theme"] = "light",
            ["recipe.title"] = "Classic Scrambled Eggs"
        });
        var deltaJson = JsonSerializer.Serialize(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["user.preference.theme"] = "dark",
            ["recipe.title"] = null
        });

        await runtime.RunTurnAsync(new AgentTurnRequest(
            UserMessage: "continue",
            AgentName: "AgentBlazor UI Agent",
            SessionId: "session-shared-context",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.SharedStateSnapshot] = snapshotJson,
                [AgentRuntimeContextKeys.SharedStateDelta] = deltaJson
            }));

        var latest = sharedStore.GetSnapshot("AgentBlazor UI Agent", "session-shared-context");
        Assert.Equal("dark", latest.Values["user.preference.theme"]);
        Assert.False(latest.Values.ContainsKey("recipe.title"));
    }

    private sealed class StaticResponseChatClient(string responseText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
