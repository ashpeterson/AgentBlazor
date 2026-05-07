using AgentBlazor;
using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Adapters;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.State;
using AgentBlazor.Core.Runtime.Tools;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Core.Paid;
using AgentBlazor.Execution;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using AgentBlazor.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddAgentBlazorServices_DoesNotRegisterBuiltInDefaultAgentByDefault()
    {
        var services = new ServiceCollection();

        services.AddAgentBlazorServices(options =>
        {
            options.Provider.Kind = AgentProviderKind.OpenAI;
            options.Provider.Model = "gpt-4o-mini";
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal("gpt-4o-mini", options.Provider.Model);

        var registry = provider.GetRequiredService<IAgentRegistry>();
        Assert.False(registry.TryGet("AgentBlazor UI Agent", out _));

        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.NotNull(runtimeAdapter);
        var sharedStateStore = provider.GetRequiredService<IAgentSharedStateStore>();
        Assert.NotNull(sharedStateStore);
        var componentRegistry = provider.GetRequiredService<IAgentComponentRegistry>();
        Assert.NotNull(componentRegistry);

    }

    [Fact]
    public void AddAgentBlazorServices_UsesChatClientRuntimeAdapterByDefault_WhenChatClientIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.IsType<AgentBlazor.Core.Runtime.Adapters.ChatClientRuntimeAdapter>(adapter);
    }

    [Fact]
    public void AddAgentBlazor_CustomRuntimeAdapterRegistration_OverridesLegacyAdapter()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .UseRuntimeAdapter<StubRuntimeAdapter>();

        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.IsType<StubRuntimeAdapter>(adapter);
    }

    [Fact]
    public async Task AddAgentBlazor_UseChatClientRuntimeAdapter_RoutesThroughExternalChatAgent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("chat-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        Assert.IsType<AgentBlazor.Core.Runtime.Adapters.ChatClientRuntimeAdapter>(adapter);
        Assert.True(adapter.SupportsStreaming);
        Assert.True(adapter.SupportsReconnect);
        Assert.True(adapter.SupportsCancellation);

        var first = await adapter.RunTurnAsync(new AgentTurnRequest("hello", SessionId: "session-a"));
        var second = await adapter.RunTurnAsync(new AgentTurnRequest("continue", SessionId: "session-a"));

        Assert.Equal("response-1", first.ResponseText);
        Assert.Equal("response-2", second.ResponseText);
        Assert.Equal(2, chatClient.Requests.Count);
        Assert.True(chatClient.Requests[1].Count > chatClient.Requests[0].Count);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StreamsTextUpdates()
    {
        var services = new ServiceCollection();
        services.AddSingleton<StreamingRecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<StreamingRecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("chat-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest("stream please", SessionId: "session-b")))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.RunStarted);
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageStart);
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent && e.TextDelta == "hello ");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent && e.TextDelta == "world");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageEnd);
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.RunFinished);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ReconnectsAndReplaysBufferedStreamEvents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<StreamingRecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<StreamingRecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("chat-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        const string runId = "replay-run-1";
        var original = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "stream please",
                           SessionId: "session-replay",
                           Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                           {
                               [AgentRuntimeContextKeys.RunId] = runId
                           })))
        {
            original.Add(streamEvent);
        }

        var replayed = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.ConnectRunStreamAsync(runId))
        {
            replayed.Add(streamEvent);
        }

        Assert.NotEmpty(original);
        Assert.Equal(original.Count, replayed.Count);
        Assert.All(replayed, static e => Assert.True(e.IsReplay));
        Assert.Equal(
            original.Select(static e => (e.Kind, e.TextDelta)),
            replayed.Select(static e => (e.Kind, e.TextDelta)));
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ConcurrentRuns_ForDifferentSessions_CanOverlap()
    {
        var chatClient = new ParallelProbeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton(chatClient);
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ParallelProbeChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("chat-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var firstTask = Task.Run(() => adapter.RunTurnAsync(new AgentTurnRequest("first", SessionId: "parallel-a")));
        var secondTask = Task.Run(() => adapter.RunTurnAsync(new AgentTurnRequest("second", SessionId: "parallel-b")));

        await chatClient.TwoCallsObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        chatClient.ReleaseAll.TrySetResult(true);

        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(2, responses.Length);
        Assert.True(chatClient.MaxConcurrentCalls >= 2, $"Expected overlap but max concurrency was {chatClient.MaxConcurrentCalls}.");
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ConcurrentRuns_ForSameSession_AreSerialized()
    {
        var chatClient = new ParallelProbeChatClient();
        var services = new ServiceCollection();
        services.AddSingleton(chatClient);
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ParallelProbeChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("chat-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var firstTask = Task.Run(() => adapter.RunTurnAsync(new AgentTurnRequest("first", SessionId: "shared-serial")));
        await chatClient.FirstCallObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondTask = Task.Run(() => adapter.RunTurnAsync(new AgentTurnRequest("second", SessionId: "shared-serial")));
        await Task.Delay(200);
        Assert.Equal(1, chatClient.ActiveCalls);
        Assert.Equal(1, chatClient.MaxConcurrentCalls);

        chatClient.ReleaseAll.TrySetResult(true);
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, chatClient.MaxConcurrentCalls);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ConnectRunStreamAsync_AllowsMultipleSubscribers()
    {
        var chatClient = new MultiReconnectStreamingChatClient();
        var services = new ServiceCollection();
        services.AddSingleton(chatClient);
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<MultiReconnectStreamingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("chat-agent");

        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        const string runId = "multi-reconnect-run";

        var originalTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                               "stream please",
                               SessionId: "multi-reconnect-session",
                               Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                               {
                                   [AgentRuntimeContextKeys.RunId] = runId
                               })))
            {
                events.Add(streamEvent);
            }

            return events;
        });

        await chatClient.FirstChunkDelivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var reconnectOneTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in adapter.ConnectRunStreamAsync(runId))
            {
                events.Add(streamEvent);
            }

            return events;
        });

        var reconnectTwoTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in adapter.ConnectRunStreamAsync(runId))
            {
                events.Add(streamEvent);
            }

            return events;
        });

        chatClient.AllowCompletion.TrySetResult(true);

        var original = await originalTask;
        var reconnectOne = await reconnectOneTask;
        var reconnectTwo = await reconnectTwoTask;

        Assert.NotEmpty(original);
        Assert.NotEmpty(reconnectOne);
        Assert.NotEmpty(reconnectTwo);
        Assert.Contains(reconnectOne, static e => e.IsReplay);
        Assert.Contains(reconnectTwo, static e => e.IsReplay);

        var originalText = string.Concat(original.Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent).Select(static e => e.TextDelta));
        var reconnectOneText = string.Concat(reconnectOne.Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent).Select(static e => e.TextDelta));
        var reconnectTwoText = string.Concat(reconnectTwo.Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent).Select(static e => e.TextDelta));

        Assert.Equal(originalText, reconnectOneText);
        Assert.Equal(originalText, reconnectTwoText);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ExecutesProjectedComponentTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolInvokingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("grid-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var executor = provider.GetRequiredService<RecordingComponentActionExecutor>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Filter the grid",
            AgentName: "grid-agent",
            SessionId: "session-tools"));

        Assert.False(response.UsesLegacyCompatibilityPayload);
        Assert.Empty(response.LegacyPlannedActions);
        Assert.Empty(response.LegacyExecutionResults);
        Assert.False(response.RequiresApproval);
        var plannedStep = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal("AgentGrid", plannedStep.TargetId);
        Assert.Equal("filter", plannedStep.ActionId);
        Assert.Equal("filter-applied", plannedStep.Message);

        var executed = Assert.Single(executor.ExecutedActions);
        Assert.Equal("AgentGrid", executed.ComponentId);
        Assert.Equal("filter", executed.ActionId);
        Assert.Equal("Risk", executed.Arguments!["column"]);
        Assert.Equal("High", executed.Arguments["value"]);
        Assert.Equal("filter-applied", plannedStep.Message);
    }

    [Fact]
    public void AddWorkflow_RegistersCapabilityScopedAgent()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddWorkflow<WorkflowScaffoldCapabilities>("ops-agent", agent =>
            {
                agent.WithDescription("Operations workflow agent.");
                agent.WithRoutePrefixes("/ops", "/ops/review");
            });

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAgentRegistry>();
        Assert.True(registry.TryGet("ops-agent", out var registration));
        Assert.Equal("Operations workflow agent.", registration.Description);
        Assert.Empty(registration.AllowedActions);
        Assert.Contains("workflow_scaffold.assess_case", registration.AllowedCapabilityActions);
        Assert.Contains("workflow_scaffold.prepare_case", registration.AllowedCapabilityActions);
        Assert.Equal("/ops,/ops/review", registration.Metadata["route_prefixes"]);

        var capabilityRegistry = provider.GetRequiredService<global::AgentBlazor.App.IAgentCapabilityRegistry>();
        var capabilities = capabilityRegistry.GetCapabilities(provider);
        Assert.Contains(capabilities, static capability => capability.CapabilityId == "workflow_scaffold");
    }

    [Fact]
    public void CapabilityResult_HelperMethods_MergeStructuredOutcomeData()
    {
        var result = global::AgentBlazor.App.CapabilityResult.Success("Prepared the workflow.")
            .WithWarning("Manual review still required.")
            .WithWarnings("Policy sign-off pending.")
            .WithNextAction("Review the draft")
            .WithNextActions("Approve the submission")
            .WithOutput("supplierCount", 3);

        Assert.Equal(
            ["Manual review still required.", "Policy sign-off pending."],
            result.Warnings);
        Assert.Equal(
            ["Review the draft", "Approve the submission"],
            result.NextActions);
        Assert.Equal(3, result.Outputs["supplierCount"]);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_PrefersExplicitAgents_OverBuiltInDefaultFallback()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("Workflow Agent", agent => agent.WithAllowedComponents("AgentDialog"));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            SessionId: "implicit-explicit-agent"));

        Assert.Equal("Workflow Agent", response.AgentName);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_UsesLegacyDefaultFallback_WhenEnabledExplicitly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            SessionId: "legacy-default-agent"));

        Assert.Equal("AgentBlazor UI Agent", response.AgentName);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_RefreshesProjectedToolsAcrossTurnsInSameSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolCatalogRecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolCatalogRecordingChatClient>());
        services.AddSingleton<MutableServiceToolRegistry>();
        services.AddSingleton<IAgentServiceToolRegistry>(static sp => sp.GetRequiredService<MutableServiceToolRegistry>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("tool-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<ToolCatalogRecordingChatClient>();
        var toolRegistry = provider.GetRequiredService<MutableServiceToolRegistry>();

        toolRegistry.SetTools(
        [
            CreateTestServiceTool("risk_lookup")
        ]);

        _ = await adapter.RunTurnAsync(new AgentTurnRequest("first turn", SessionId: "shared-session"));

        toolRegistry.SetTools(
        [
            CreateTestServiceTool("risk_lookup"),
            CreateTestServiceTool("audit_lookup")
        ]);

        _ = await adapter.RunTurnAsync(new AgentTurnRequest("second turn", SessionId: "shared-session"));

        Assert.Collection(
            chatClient.ToolSnapshots,
            snapshot =>
            {
                Assert.Contains("risk_lookup", snapshot);
                Assert.DoesNotContain("audit_lookup", snapshot);
            },
            snapshot =>
            {
                Assert.Contains("risk_lookup", snapshot);
                Assert.Contains("audit_lookup", snapshot);
                Assert.True(snapshot.Count >= 2);
            });
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_TruncatesLongCapabilityToolNames_ToProviderSafeLength()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolCatalogRecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolCatalogRecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LongCapabilityNameCapabilities>("long-capability-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<ToolCatalogRecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Inspect the available workflow tools",
            AgentName: "long-capability-agent",
            SessionId: "long-capability-tool-name"));

        var toolSnapshot = Assert.Single(chatClient.ToolSnapshots);
        var toolName = Assert.Single(
            toolSnapshot,
            static name => name.StartsWith("capability_", StringComparison.Ordinal) &&
                           name.Contains("prepare_release_packet_for_final_review", StringComparison.Ordinal));
        Assert.True(toolName.Length <= 64, $"Expected tool name length <= 64 but got '{toolName}' ({toolName.Length}).");
        Assert.StartsWith("capability_", toolName, StringComparison.Ordinal);
        Assert.Contains("prepare_release_packet_for_final_review", toolName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StreamsProjectedToolLifecycle()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolInvokingStreamingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolInvokingStreamingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("grid-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "Filter the grid",
                           AgentName: "grid-agent",
                           SessionId: "session-stream-tools")))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.StepStarted &&
                                            e.PlannedAction?.ComponentId == "AgentGrid" &&
                                            e.PlannedAction.ActionId == "filter");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallStart &&
                                            e.PlannedAction?.ComponentId == "AgentGrid");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallArgs &&
                                            e.ToolArguments is not null &&
                                            Equals(e.ToolArguments["column"], "Risk") &&
                                            Equals(e.ToolArguments["value"], "High"));
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallResult &&
                                            e.ExecutionResult?.Message == "filter-applied");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallEnd &&
                                            e.ExecutionResult?.Message == "filter-applied");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.StepFinished &&
                                            e.StepSucceeded == true &&
                                            e.ExecutionResult?.Message == "filter-applied");
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StreamsApprovalRequiredToolLifecycle()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ApprovalToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalToolInvokingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("form-agent", agent =>
            {
                agent.WithAllowedComponents("AgentForm");
                agent.WithAllowedActions("AgentForm.submit");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentForm",
                "Form used for supplier updates.",
                new ComponentActionCapability(
                    "submit",
                    "Submit the form.",
                    RequiresApproval: true,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "agentId": { "type": "string" }
                          }
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "Submit the form",
                           AgentName: "form-agent",
                           SessionId: "session-stream-approval")))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallStart &&
                                            e.PlannedAction?.ComponentId == "AgentForm");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ApprovalRequired &&
                                            e.PendingApprovals is { Count: 1 } &&
                                            e.PendingApprovals[0].ComponentId == "AgentForm" &&
                                            e.PendingApprovals[0].ActionId == "submit");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallEnd &&
                                            e.PlannedAction?.ComponentId == "AgentForm");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.StepFinished &&
                                            e.PlannedAction?.ComponentId == "AgentForm" &&
                                            e.StepSucceeded is null);
        Assert.DoesNotContain(events, static e => e.Kind == AgentTurnStreamEventKind.ToolCallResult &&
                                                  e.PlannedAction?.ComponentId == "AgentForm");
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ProjectsGeneratedUiToolsOnlyWhenRequested()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolCatalogRecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolCatalogRecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("ui-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<ToolCatalogRecordingChatClient>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest("plain", SessionId: "generated-ui-toggle"));
        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "render ui",
            SessionId: "generated-ui-toggle",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentGenerativeUiSpec.GenerateUiContextKey] = bool.TrueString
            }));

        Assert.Collection(
            chatClient.ToolSnapshots,
            snapshot => Assert.DoesNotContain(snapshot, static name => name.Contains("generated_ui_", StringComparison.OrdinalIgnoreCase)),
            snapshot =>
            {
                Assert.Contains(snapshot, static name => name.Contains("generated_ui_summary_card", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(snapshot, static name => name.Contains("generated_ui_table_view", StringComparison.OrdinalIgnoreCase));
                Assert.True(snapshot.Count >= 5);
            });
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_AttachesGeneratedUiDocumentFromProjectedUiTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GeneratedUiToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<GeneratedUiToolInvokingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("ui-agent");

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Create a generated summary",
            SessionId: "generated-ui-response",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentGenerativeUiSpec.GenerateUiContextKey] = bool.TrueString
            }));

        var document = Assert.IsType<AgentUiDocument>(response.GeneratedUi);
        var block = Assert.Single(document.Blocks);
        Assert.Equal(AgentUiBlockKind.Card, block.Kind);
        Assert.Equal("High Risk Suppliers", block.Title);
        Assert.Equal("generated-summary", block.Id);
        Assert.Equal("generated-ui-ready", response.ResponseText);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_SuppressesGeneratedUi_WhenRuntimeApprovalIsPending()
    {
        var services = new ServiceCollection();
        services.AddSingleton<GeneratedUiAndApprovalToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<GeneratedUiAndApprovalToolInvokingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ApprovalUiContractCapabilities>()
            .AddAgent("approval-ui-agent", agent =>
            {
                agent.WithAllowedActions("approval_ui_contract.draft_reply");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Draft a reply for ticket TCK-1042",
            AgentName: "approval-ui-agent",
            SessionId: "approval-ui-contract",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentGenerativeUiSpec.GenerateUiContextKey] = bool.TrueString
            }));

        Assert.True(response.RequiresApproval);
        Assert.Null(response.GeneratedUi);
        Assert.Equal("Approval required for approval_ui_contract.draft_reply.", response.ResponseText);

        var approval = Assert.Single(response.PendingApprovals);
        Assert.Equal("approval_ui_contract", approval.ComponentId);
        Assert.Equal("draft_reply", approval.ActionId);
        Assert.Equal("TCK-1042", approval.Parameters["ticketId"]?.ToString());
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_BlocksImplicitFormSubmitFromGeneratedUiAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FormSubmitToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<FormSubmitToolInvokingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("form-agent", agent =>
            {
                agent.WithAllowedComponents("AgentForm");
                agent.WithAllowedActions("AgentForm.submit");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentForm",
                "Form used for supplier updates.",
                new ComponentActionCapability(
                    "submit",
                    "Submit the form.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "agentId": { "type": "string" }
                          }
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var executor = provider.GetRequiredService<RecordingComponentActionExecutor>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "apply the drafted changes",
            AgentName: "form-agent",
            SessionId: "generated-ui-submit-guard",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentGenerativeUiSpec.GenerateUiContextKey] = bool.TrueString
            },
            GeneratedUiAction: new GeneratedUiActionInvocation(
                "supplier-form",
                "apply_changes",
                "Apply the drafted changes",
                new Dictionary<string, object?>())));

        Assert.False(response.UsesLegacyCompatibilityPayload);
        Assert.Empty(response.LegacyExecutionResults);
        var blocked = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Blocked, blocked.Status);
        Assert.Equal("AgentForm", blocked.TargetId);
        Assert.Equal("submit", blocked.ActionId);
        Assert.Empty(executor.ExecutedActions);
        Assert.Equal("Explicit submit intent is required before submitting a generated form action.", blocked.Message);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_BlocksGeneratedUiActionApprovalCapabilityBeforePendingApproval()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ApprovalToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalToolInvokingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ApprovalUiContractCapabilities>()
            .AddAgent("approval-ui-agent", agent =>
            {
                agent.WithAllowedActions("approval_ui_contract.draft_reply");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Generated UI action invoked: approval-confirmation.approve-draft.",
            AgentName: "approval-ui-agent",
            SessionId: "generated-ui-approval-guard",
            GeneratedUiAction: new GeneratedUiActionInvocation(
                "approval-confirmation",
                "approve-draft",
                "Approve draft reply",
                new Dictionary<string, object?> { ["ticketId"] = "TCK-1042" })));

        Assert.False(response.RequiresApproval);
        Assert.Empty(response.PendingApprovals);
        var blocked = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Blocked, blocked.Status);
        Assert.False(blocked.RequiresApproval);
        Assert.Equal("approval_ui_contract", blocked.TargetId);
        Assert.Equal("draft_reply", blocked.ActionId);
        Assert.Equal(
            "Generated UI actions cannot request approval-gated action 'approval_ui_contract.draft_reply'. Ask for the action in chat to review and approve it.",
            blocked.Message);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_PersistsConversationTurnUsingScopedSessionKey()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices(options => options.IsolateConversationsByAgent = true)
            .UseChatClientRuntimeAdapter()
            .AddAgent("Agent A", agent => agent.WithAllowedComponents("AgentDialog"));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var conversationStore = provider.GetRequiredService<IConversationStore>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello",
            AgentName: "Agent A",
            SessionId: "scoped-session"));

        var scopedSessionId = AgentConversationScope.BuildSessionKey("scoped-session", "Agent A", isolateByAgent: true);
        var history = await conversationStore.GetHistoryAsync(scopedSessionId);

        var turn = Assert.Single(Assert.IsType<ConversationHistory>(history).Turns);
        Assert.Equal("hello", turn.UserMessage);
        Assert.Equal("response-1", turn.AgentResponse);
        Assert.Null(await conversationStore.GetHistoryAsync("scoped-session"));
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_RecordsInspectorRun()
    {
        var services = new ServiceCollection();
        var inspectorStore = new InMemoryAgentInspectorStore();

        services.AddSingleton<IAgentInspectorStore>(inspectorStore);
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("Agent B", agent => agent.WithAllowedComponents("AgentDialog"));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "hello from handoff",
            AgentName: "Agent B",
            SessionId: "handoff-session",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.AgentHandoffFrom] = "Agent A",
                [AgentRuntimeContextKeys.AgentHandoffTo] = "Agent B",
                [AgentRuntimeContextKeys.AgentHandoffAt] = DateTimeOffset.UtcNow.ToString("O")
            }));

        var run = Assert.Single(inspectorStore.GetRecentRuns("handoff-session"));
        Assert.Equal("Agent B", run.AgentName);
        Assert.True(run.Succeeded);
        Assert.Contains(run.Events, static e => e.Kind == "RunStarted");
        Assert.Contains(run.Events, static e => e.Kind == "RunFinished");
        Assert.Contains(run.Events, static e => e.Kind == "AgentHandoff" && e.Detail == "Agent A -> Agent B");
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_RecordsSuccessfulActionsInActionHistory()
    {
        var services = new ServiceCollection();
        var historyStore = new InMemoryActionHistoryStore();

        services.AddSingleton<IActionHistoryStore>(historyStore);
        services.AddSingleton<ToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolInvokingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("grid-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Filter the grid",
            AgentName: "grid-agent",
            SessionId: "history-session",
            UserId: "user-123"));

        var history = await historyStore.GetRecentAsync("history-session");
        var entry = Assert.Single(history);
        Assert.Equal("user-123", entry.UserId);
        Assert.Equal("Filter the grid", entry.UserMessage);
        Assert.Equal("filter", entry.ActionId);
        Assert.Equal("AgentGrid", entry.AgentId);
        Assert.Equal("Risk", entry.Args["column"]);
        Assert.Equal("High", entry.Args["value"]);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_WithTracingEnabled_StoresPromptTrace()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolInvokingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .EnablePromptTracing()
            .UseChatClientRuntimeAdapter()
            .AddAgent("grid-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        _ = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Filter the grid",
            AgentName: "grid-agent",
            SessionId: "trace-session"));

        Assert.Equal(1, traceStore.Count);

        var trace = Assert.Single(await traceStore.GetBySessionAsync("trace-session", 1));
        Assert.NotNull(trace.Planning);
        Assert.Contains(trace.Planning.WorkflowSteps, action =>
            string.Equals(action.ComponentId, "AgentGrid", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.ActionId, "filter", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(trace.Execution);
        Assert.Contains(trace.Execution.ExecutionSteps, result =>
            string.Equals(result.ComponentId, "AgentGrid", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, "filter", StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded);
        Assert.NotNull(trace.Response);
        Assert.Equal(PromptTraceOutcome.Succeeded, trace.Response.Outcome);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ReturnsExplicitNoAgentResponse_WhenNoAgentsAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter();

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Hello",
            SessionId: "session-no-agent"));

        Assert.Equal("none", response.AgentName);
        Assert.Equal("No agents are registered.", response.ResponseText);
        Assert.Empty(chatClient.Requests);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StreamsExplicitNoAgentResponse_WhenAgentLockCannotResolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter();

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "Hello",
                           SessionId: "session-no-agent-stream",
                           Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                           {
                               [AgentRuntimeContextKeys.AgentLock] = "true",
                               [AgentRuntimeContextKeys.CurrentRoute] = "/dashboard"
                           })))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent &&
                                            e.TextDelta == "No agents are registered.");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.RunFinished &&
                                            e.Response is not null &&
                                            e.Response.AgentName == "none" &&
                                            e.Response.ResponseText == "No agents are registered.");
        Assert.Empty(chatClient.Requests);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ReturnsExplicitNoActionsResponse_WhenResolvedAgentHasNoProjectedTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("restricted-agent", agent =>
            {
                agent.WithAllowedComponents("MissingComponent");
                agent.WithAllowedActions("MissingComponent.noop");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Hello",
            AgentName: "restricted-agent",
            SessionId: "session-no-actions"));

        Assert.Equal("restricted-agent", response.AgentName);
        Assert.Contains("No allowed actions are available for this agent policy.", response.ResponseText);
        Assert.Empty(chatClient.Requests);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StreamsExplicitNoActionsResponse_WhenResolvedAgentHasNoProjectedTools()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<RecordingChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("restricted-agent", agent =>
            {
                agent.WithAllowedComponents("MissingComponent");
                agent.WithAllowedActions("MissingComponent.noop");
            });

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var chatClient = provider.GetRequiredService<RecordingChatClient>();
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "Hello",
                           AgentName: "restricted-agent",
                           SessionId: "session-no-actions-stream")))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent &&
                                            e.TextDelta is not null &&
                                            e.TextDelta.Contains("No allowed actions are available for this agent policy.", StringComparison.Ordinal));
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.RunFinished &&
                                            e.Response is not null &&
                                            e.Response.AgentName == "restricted-agent" &&
                                            e.Response.ResponseText.Contains("No allowed actions are available for this agent policy.", StringComparison.Ordinal));
        Assert.Empty(chatClient.Requests);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_SurfacesClarificationOutcomeFromComponentExecution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolInvokingChatClient>());
        services.AddSingleton<ClarificationComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<ClarificationComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("grid-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Filter the grid",
            AgentName: "grid-agent",
            SessionId: "session-clarification"));

        Assert.True(response.RequiresClarification);
        Assert.Equal("Which risk level should I use?", response.ClarificationQuestion);
        Assert.Equal("Which risk level should I use?", response.ResponseText);
        var clarificationStep = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.NeedsClarification, clarificationStep.Status);
        Assert.Equal("Which risk level should I use?", clarificationStep.Message);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StreamsClarificationRequiredFromComponentExecution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ToolInvokingStreamingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ToolInvokingStreamingChatClient>());
        services.AddSingleton<ClarificationComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<ClarificationComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("grid-agent", agent =>
            {
                agent.WithAllowedComponents("AgentGrid");
                agent.WithAllowedActions("AgentGrid.filter");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentGrid",
                "Grid for supplier records.",
                new ComponentActionCapability(
                    "filter",
                    "Apply a filter to the grid.",
                    RequiresApproval: false,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "column": { "type": "string" },
                            "value": { "type": "string" }
                          },
                          "required": ["column", "value"]
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in adapter.RunTurnStreamingAsync(new AgentTurnRequest(
                           "Filter the grid",
                           AgentName: "grid-agent",
                           SessionId: "session-stream-clarification")))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.ClarificationRequired &&
                                            e.ClarificationQuestion == "Which risk level should I use?");
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.RunFinished &&
                                            e.Response is not null &&
                                            e.Response.RequiresClarification &&
                                            e.Response.ClarificationQuestion == "Which risk level should I use?");
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_ProjectsApprovalRequiredComponentTool_AsPendingApproval()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ApprovalToolInvokingChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalToolInvokingChatClient>());
        services.AddSingleton<RecordingComponentActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(static sp => sp.GetRequiredService<RecordingComponentActionExecutor>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("form-agent", agent =>
            {
                agent.WithAllowedComponents("AgentForm");
                agent.WithAllowedActions("AgentForm.submit");
            })
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                "AgentForm",
                "Form used for supplier updates.",
                new ComponentActionCapability(
                    "submit",
                    "Submit the form.",
                    RequiresApproval: true,
                    InputSchema: """
                        {
                          "type": "object",
                          "additionalProperties": false,
                          "properties": {
                            "agentId": { "type": "string" }
                          }
                        }
                        """)));

        await using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var executor = provider.GetRequiredService<RecordingComponentActionExecutor>();

        var response = await adapter.RunTurnAsync(new AgentTurnRequest(
            "Submit the form",
            AgentName: "form-agent",
            SessionId: "session-approval"));

        Assert.True(response.RequiresApproval);
        Assert.Single(response.PendingApprovals);
        Assert.False(response.UsesLegacyCompatibilityPayload);
        Assert.Empty(response.LegacyPlannedActions);
        Assert.Empty(response.LegacyExecutionResults);
        Assert.Empty(executor.ExecutedActions);
        var plannedStep = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal("AgentForm", plannedStep.TargetId);
        Assert.Equal("submit", plannedStep.ActionId);
        Assert.Equal(AgentExecutionStepStatus.ApprovalRequired, plannedStep.Status);

        var approval = Assert.Single(response.PendingApprovals);
        Assert.Equal("AgentForm", approval.ComponentId);
        Assert.Equal("submit", approval.ActionId);
        Assert.NotNull(approval.PolicyDecision);
        Assert.Equal(AgentRiskClass.SignificantMutation, approval.PolicyDecision!.RiskClass);
        Assert.Equal(AgentApprovalMode.ExplicitPlanApproval, approval.PolicyDecision.ApprovalMode);
    }

    [Fact]
    public void AgentComponentRegistry_RegisterAndUnregister_Works()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        var component = new StubAgentControllable("supplier-grid");

        registry.Register(component);

        Assert.True(registry.TryGet("supplier-grid", out var resolved));
        Assert.Same(component, resolved);

        Assert.True(registry.Unregister("supplier-grid"));
        Assert.False(registry.TryGet("supplier-grid", out _));
    }

    [Fact]
    public void AddAgent_AndCatalogConfiguration_AreApplied()
    {
        var services = new ServiceCollection();

        services
            .AddAgentBlazorServices()
            .AddAgent("supplier-risk-agent", agent =>
            {
                agent.WithInstructions("Supplier risk agent instructions.");
                agent.WithAllowedComponents("AgentGrid", "AgentStatePanel");
                agent.WithAllowedActions("AgentGrid.filter", "AgentGrid.sort");
            })
            .ConfigureComponentCatalog(catalog => catalog.Enable("AgentGrid", "filter", "sort"));

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAgentRegistry>();
        Assert.True(registry.TryGet("supplier-risk-agent", out var custom));
        Assert.Contains("AgentGrid", custom.AllowedComponents);
        Assert.Contains("AgentGrid.filter", custom.AllowedActions);
        Assert.Contains("AgentGrid.sort", custom.AllowedActions);

        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();
        Assert.True(componentCatalog.TryGet("AgentGrid", out var grid));
        Assert.Contains(grid.Actions, a => a.ActionId.Equals("filter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(grid.Actions, a => a.ActionId.Equals("sort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultCatalog_ContainsMudBlazorV1Capabilities_WithSchemasAndApprovalFlags()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();

        Assert.Equal("agentblazor.components", AgentComponentCapabilityProfile.ProfileId);

        foreach (var componentId in AgentComponentCapabilityProfile.ComponentIds)
        {
            Assert.True(componentCatalog.TryGet(componentId, out var capability));
            Assert.All(capability.Actions, action =>
                Assert.False(string.IsNullOrWhiteSpace(action.InputSchema)));
        }

        Assert.True(componentCatalog.TryGet(AgentComponentCapabilityProfile.AgentFormComponentId, out var form));
        var submit = Assert.Single(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase));
        Assert.True(submit.RequiresApproval);

        Assert.True(componentCatalog.TryGet(AgentComponentCapabilityProfile.AgentNavMenuComponentId, out var nav));
        var navigateExternal = Assert.Single(nav.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase));
        Assert.True(navigateExternal.RequiresApproval);

        Assert.True(componentCatalog.TryGet(AgentComponentCapabilityProfile.AgentDataGridComponentId, out var grid));
        var filter = Assert.Single(grid.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase));
        Assert.False(filter.RequiresApproval);
    }

    [Fact]
    public void UseAgentCapabilityPreset_Minimal_AddsExpectedSafeSubset()
    {
        var services = new ServiceCollection();
        services
            .AddAgentBlazorServices(options =>
            {
#pragma warning disable CS0618
                options.DefaultAgent.ComponentCatalogMode = ComponentCatalogMode.WhitelistOnly;
#pragma warning restore CS0618
            })
            .UseAgentCapabilityPreset(AgentCapabilityPreset.Minimal);

        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();

        Assert.True(componentCatalog.TryGet(AgentComponentCapabilityProfile.AgentFormComponentId, out var form));
        Assert.Contains(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.FormValidateActionId, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase));

        Assert.True(componentCatalog.TryGet(AgentComponentCapabilityProfile.AgentNavMenuComponentId, out var nav));
        Assert.Contains(nav.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nav.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UseAgentCapabilityPreset_IsAdditiveWithCustomCatalogOverrides()
    {
        var services = new ServiceCollection();
        services
            .AddAgentBlazorServices(options =>
            {
#pragma warning disable CS0618
                options.DefaultAgent.ComponentCatalogMode = ComponentCatalogMode.WhitelistOnly;
#pragma warning restore CS0618
            })
            .UseAgentCapabilityPreset(AgentCapabilityPreset.Minimal)
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                AgentComponentCapabilityProfile.AgentFormComponentId,
                "Custom AgentForm override.",
                new ComponentActionCapability(
                    AgentComponentCapabilityProfile.FormSubmitActionId,
                    "Submit AgentForm via custom override.",
                    RequiresApproval: true,
                    InputSchema: AgentComponentCapabilityProfile.FormSubmitInputSchema)));

        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();

        Assert.True(componentCatalog.TryGet(AgentComponentCapabilityProfile.AgentFormComponentId, out var form));
        Assert.Contains(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.FormValidateActionId, StringComparison.OrdinalIgnoreCase));
        var submit = Assert.Single(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentCapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase));
        Assert.True(submit.RequiresApproval);
    }

    [Fact]
    public void ComponentActionPolicy_EvaluateAllowedCapabilities_TracksBlockedActionKeys()
    {
        var catalog = DefaultShippedComponents.CreateCatalog();
        var allowedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AgentComponentCapabilityProfile.AgentDialogComponentId
        };
        var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{AgentComponentCapabilityProfile.AgentDialogComponentId}.{AgentComponentCapabilityProfile.DialogOpenActionId}"
        };

        var evaluation = ComponentActionPolicy.EvaluateAllowedCapabilities(
            catalog.GetComponents(),
            allowedComponents,
            allowedActions);

        Assert.True(evaluation.HasAllowedActions);
        Assert.Single(evaluation.AllowedComponents);

        var dialog = Assert.Single(evaluation.AllowedComponents);
        Assert.Equal(AgentComponentCapabilityProfile.AgentDialogComponentId, dialog.ComponentId);
        var onlyAction = Assert.Single(dialog.Actions);
        Assert.Equal(AgentComponentCapabilityProfile.DialogOpenActionId, onlyAction.ActionId, ignoreCase: true);

        Assert.Contains(
            $"{AgentComponentCapabilityProfile.AgentDialogComponentId}.{AgentComponentCapabilityProfile.DialogCloseActionId}",
            evaluation.BlockedActionKeys);
        Assert.Contains(
            $"{AgentComponentCapabilityProfile.AgentFormComponentId}.{AgentComponentCapabilityProfile.FormValidateActionId}",
            evaluation.BlockedActionKeys);
    }

    [Fact]
    public void AgentComponentTierBoundaries_MapExpectedActionTiers()
    {
        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridFilterActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentDataGridComponentId,
                AgentComponentCapabilityProfile.DataGridSetPageActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentFormComponentId,
                AgentComponentCapabilityProfile.FormSubmitActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentSelectComponentId,
                AgentComponentCapabilityProfile.SelectSetValueActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentAutocompleteComponentId,
                AgentComponentCapabilityProfile.AutocompleteSelectOptionActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentDatePickerComponentId,
                AgentComponentCapabilityProfile.DatePickerSetDateActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentDateRangePickerComponentId,
                AgentComponentCapabilityProfile.DateRangePickerSetRangeActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentTreeViewComponentId,
                AgentComponentCapabilityProfile.TreeViewSelectNodeActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentStepperComponentId,
                AgentComponentCapabilityProfile.StepperGoToStepActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentCommandBarComponentId,
                AgentComponentCapabilityProfile.CommandBarInvokeCommandActionId));

        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentCapabilityProfile.AgentFileUploadComponentId,
                AgentComponentCapabilityProfile.FileUploadAttachActionId));
    }

    [Fact]
    public async Task Runtime_RequiresFrameworkProvider_WhenNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var response = await runtimeAdapter.RunTurnAsync(new AgentTurnRequest("open the chat widget"));

        Assert.Contains("No AI provider configured", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(response.LegacyPlannedActions);
    }

    [Fact]
    public async Task AddAgentBlazorServices_RegistersTelemetrySink_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var telemetrySink = provider.GetRequiredService<IAgentBlazorTelemetrySink>();

        await telemetrySink.TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
        {
            Kind = AgentBlazorRunEventKind.Started,
            Source = AgentBlazorTelemetrySources.Runtime,
            AgentName = "test-agent"
        });

        Assert.NotNull(telemetrySink);
    }

    [Fact]
    public void AddAgentBlazorServices_RegistersDeferredActionEvents_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var deferredEvents = provider.GetRequiredService<IAgentDeferredActionEvents>();

        Assert.NotNull(deferredEvents);
    }

    [Fact]
    public void DeferredActionEvents_PublishesCompletionNotifications()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var deferredEvents = provider.GetRequiredService<IAgentDeferredActionEvents>();

        DeferredComponentActionEvent? observed = null;
        deferredEvents.DeferredActionCompleted += actionEvent => observed = actionEvent;

        var expected = new DeferredComponentActionEvent(
            ComponentType: "Form",
            AgentId: "supplier-form",
            ActionId: "set_field",
            Succeeded: true,
            Message: "Set SupplierName to ash.",
            OccurredAt: DateTimeOffset.UtcNow,
            SessionId: "session-1",
            RunId: "run-1");

        deferredEvents.Publish(expected);

        Assert.Equal(expected, observed);
    }

    [Fact]
    public void AddRuntimeEventSubscriber_RegistersSubscriberImplementation()
    {
        var services = new ServiceCollection();
        services
            .AddAgentBlazorServices()
            .AddRuntimeEventSubscriber<TestRuntimeEventSubscriber>();

        using var provider = services.BuildServiceProvider();
        var subscribers = provider.GetServices<IAgentRuntimeEventSubscriber>();

        Assert.Contains(subscribers, static s => s is TestRuntimeEventSubscriber);
    }

    [Fact]
    public async Task UseJsonFileConversationStore_ReplacesDefaultInMemoryStore()
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "agentblazor-tests",
            $"{Guid.NewGuid():N}",
            "conversations.json");

        try
        {
            var services = new ServiceCollection();
            services
                .AddAgentBlazorServices()
                .UseJsonFileConversationStore(tempPath, options =>
                {
                    options.EnableAutoCleanup = false;
                });

            using (var provider = services.BuildServiceProvider())
            {
                var store = provider.GetRequiredService<IConversationStore>();
                Assert.IsType<JsonFileConversationStore>(store);

                await store.AppendTurnAsync("session-1", new ConversationTurn
                {
                    Timestamp = DateTime.UtcNow,
                    UserMessage = "hello",
                    AgentResponse = "hi",
                    PlannedActions = [],
                    ExecutionResults = []
                });
            }

            var reloadedServices = new ServiceCollection();
            reloadedServices
                .AddAgentBlazorServices()
                .UseJsonFileConversationStore(tempPath, options =>
                {
                    options.EnableAutoCleanup = false;
                });

            using var reloadedProvider = reloadedServices.BuildServiceProvider();
            var reloadedStore = reloadedProvider.GetRequiredService<IConversationStore>();
            var history = await reloadedStore.GetHistoryAsync("session-1");

            Assert.NotNull(history);
            Assert.Single(history.Turns);
            Assert.Equal("hello", history.Turns[0].UserMessage);
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
    public void UseJsonFileSharedStateStore_ReplacesDefaultInMemoryStore()
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "agentblazor-tests",
            $"{Guid.NewGuid():N}",
            "shared-state.json");

        try
        {
            var services = new ServiceCollection();
            services
                .AddAgentBlazorServices()
                .UseJsonFileSharedStateStore(tempPath, options =>
                {
                    options.MergeMode = SharedStateMergeMode.RejectStaleWrites;
                });

            using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<IAgentSharedStateStore>();
            Assert.IsType<JsonFileAgentSharedStateStore>(store);

            var options = provider.GetRequiredService<IOptions<SharedStateOptions>>().Value;
            Assert.Equal(SharedStateMergeMode.RejectStaleWrites, options.MergeMode);
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
    public async Task AddAgentBlazorServices_RegistersAgentActionExecutors_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var dataGridExecutor = provider.GetRequiredService<IDataGridActionExecutor>();
        var dialogExecutor = provider.GetRequiredService<IDialogActionExecutor>();
        var formExecutor = provider.GetRequiredService<IFormActionExecutor>();
        var navigationExecutor = provider.GetRequiredService<INavigationActionExecutor>();
        var tabsExecutor = provider.GetRequiredService<ITabsActionExecutor>();

        var dataGridResult = await dataGridExecutor.ExecuteAsync(new DataGridActionRequest("filter"));
        var dialogResult = await dialogExecutor.ExecuteAsync(new DialogActionRequest("open"));
        var formResult = await formExecutor.ExecuteAsync(new FormActionRequest("validate"));
        var navigationResult = await navigationExecutor.ExecuteAsync(new NavigationActionRequest("navigate_to"));
        var tabsResult = await tabsExecutor.ExecuteAsync(new TabsActionRequest("switch_tab"));

        Assert.Equal(AgentComponentCapabilityProfile.AgentDataGridComponentId, dataGridResult.ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.AgentDialogComponentId, dialogResult.ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.AgentFormComponentId, formResult.ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.AgentNavMenuComponentId, navigationResult.ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.AgentTabsComponentId, tabsResult.ComponentId);
    }

    [Fact]
    public async Task AddAgentBlazorServices_AllowsReplacingAgentActionExecutors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataGridActionExecutor, StubDataGridActionExecutor>();
        services.AddSingleton<IDialogActionExecutor, StubDialogActionExecutor>();
        services.AddSingleton<IFormActionExecutor, StubFormActionExecutor>();
        services.AddSingleton<INavigationActionExecutor, StubNavigationActionExecutor>();
        services.AddSingleton<ITabsActionExecutor, StubTabsActionExecutor>();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var dataGridResult = await provider.GetRequiredService<IDataGridActionExecutor>()
            .ExecuteAsync(new DataGridActionRequest("filter"));
        var dialogResult = await provider.GetRequiredService<IDialogActionExecutor>()
            .ExecuteAsync(new DialogActionRequest("open"));
        var formResult = await provider.GetRequiredService<IFormActionExecutor>()
            .ExecuteAsync(new FormActionRequest("validate"));
        var navigationResult = await provider.GetRequiredService<INavigationActionExecutor>()
            .ExecuteAsync(new NavigationActionRequest("navigate_to"));
        var tabsResult = await provider.GetRequiredService<ITabsActionExecutor>()
            .ExecuteAsync(new TabsActionRequest("switch_tab"));

        Assert.Equal("custom-grid", dataGridResult.Message);
        Assert.Equal("custom-dialog", dialogResult.Message);
        Assert.Equal("custom-form", formResult.Message);
        Assert.Equal("custom-nav", navigationResult.Message);
        Assert.Equal("custom-tabs", tabsResult.Message);
    }

    [Fact]
    public async Task DefaultTypedExecutors_DispatchToRegisteredComponents_ByAgentId()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new TypedStubAgentControllable(
            agentId: "supplier-grid",
            componentType: "DataGrid",
            supportedActionId: AgentComponentCapabilityProfile.DataGridFilterActionId));

        var executor = provider.GetRequiredService<IDataGridActionExecutor>();
        var result = await executor.ExecuteAsync(new DataGridActionRequest(
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentId"] = "supplier-grid",
                [AgentRuntimeContextKeys.SessionId] = registry.SessionId
            }));

        Assert.True(result.Succeeded);
        Assert.Equal("supplier-grid", result.Message);
    }

    [Fact]
    public async Task DefaultTypedExecutors_QueuePendingIntents_WhenComponentIsNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var tabsExecutor = provider.GetRequiredService<ITabsActionExecutor>();
        var intents = provider.GetRequiredService<IAgentNavigationIntentService>();

        var result = await tabsExecutor.ExecuteAsync(new TabsActionRequest(
            AgentComponentCapabilityProfile.TabsSwitchTabActionId,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentId"] = "workspace-tabs"
            }));

        Assert.False(result.Succeeded);
        Assert.Equal(ActionOutcome.Queued, result.Outcome);
        Assert.Contains("Queued", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(intents.HasPending("Tabs", "workspace-tabs"));
    }

    [Fact]
    public async Task DefaultTypedExecutors_DoNotFallback_WhenTargetAgentIdIsMissing()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new TypedStubAgentControllable(
            agentId: "supplier-grid-a",
            componentType: "DataGrid",
            supportedActionId: AgentComponentCapabilityProfile.DataGridFilterActionId));

        var executor = provider.GetRequiredService<IDataGridActionExecutor>();
        var intents = provider.GetRequiredService<IAgentNavigationIntentService>();
        var result = await executor.ExecuteAsync(new DataGridActionRequest(
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentId"] = "supplier-grid-missing",
                ["column"] = "Region",
                ["operator"] = "eq",
                ["value"] = "EMEA"
            }));

        Assert.False(result.Succeeded);
        Assert.Equal(ActionOutcome.Queued, result.Outcome);
        Assert.Contains("Queued", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(intents.HasPending("DataGrid", "supplier-grid-missing"));
    }

    [Fact]
    public async Task DefaultTypedExecutors_DataGridQueuesMissingArguments_ForWrapperLevelValidation()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IDataGridActionExecutor>();
        var intents = provider.GetRequiredService<IAgentNavigationIntentService>();

        var result = await executor.ExecuteAsync(new DataGridActionRequest(
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentId"] = "supplier-grid-missing"
            }));

        Assert.False(result.Succeeded);
        Assert.Equal(ActionOutcome.Queued, result.Outcome);
        Assert.Contains("Queued", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(intents.HasPending("DataGrid", "supplier-grid-missing"));
    }

    [Fact]
    public async Task DefaultTypedExecutors_DispatchByAgentId_ForAllComponentTypes()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();

        registry.Register(new TypedStubAgentControllable(
            agentId: "grid-a",
            componentType: "DataGrid",
            supportedActionId: AgentComponentCapabilityProfile.DataGridFilterActionId));
        registry.Register(new TypedStubAgentControllable(
            agentId: "dialog-a",
            componentType: "Dialog",
            supportedActionId: AgentComponentCapabilityProfile.DialogOpenActionId));
        registry.Register(new TypedStubAgentControllable(
            agentId: "form-a",
            componentType: "Form",
            supportedActionId: AgentComponentCapabilityProfile.FormValidateActionId));
        registry.Register(new TypedStubAgentControllable(
            agentId: "nav-a",
            componentType: "NavMenu",
            supportedActionId: AgentComponentCapabilityProfile.NavigationNavigateToActionId));
        registry.Register(new TypedStubAgentControllable(
            agentId: "tabs-a",
            componentType: "Tabs",
            supportedActionId: AgentComponentCapabilityProfile.TabsSwitchTabActionId));

        var sessionId = registry.SessionId;
        var dataGrid = await provider.GetRequiredService<IDataGridActionExecutor>().ExecuteAsync(
            new DataGridActionRequest(
                AgentComponentCapabilityProfile.DataGridFilterActionId,
                new Dictionary<string, object?> { ["agentId"] = "grid-a", [AgentRuntimeContextKeys.SessionId] = sessionId }));
        var dialog = await provider.GetRequiredService<IDialogActionExecutor>().ExecuteAsync(
            new DialogActionRequest(
                AgentComponentCapabilityProfile.DialogOpenActionId,
                new Dictionary<string, object?> { ["agentId"] = "dialog-a", [AgentRuntimeContextKeys.SessionId] = sessionId }));
        var form = await provider.GetRequiredService<IFormActionExecutor>().ExecuteAsync(
            new FormActionRequest(
                AgentComponentCapabilityProfile.FormValidateActionId,
                new Dictionary<string, object?> { ["agentId"] = "form-a", [AgentRuntimeContextKeys.SessionId] = sessionId }));
        var nav = await provider.GetRequiredService<INavigationActionExecutor>().ExecuteAsync(
            new NavigationActionRequest(
                AgentComponentCapabilityProfile.NavigationNavigateToActionId,
                new Dictionary<string, object?> { ["agentId"] = "nav-a", [AgentRuntimeContextKeys.SessionId] = sessionId }));
        var tabs = await provider.GetRequiredService<ITabsActionExecutor>().ExecuteAsync(
            new TabsActionRequest(
                AgentComponentCapabilityProfile.TabsSwitchTabActionId,
                new Dictionary<string, object?> { ["agentId"] = "tabs-a", [AgentRuntimeContextKeys.SessionId] = sessionId }));

        Assert.Equal("grid-a", dataGrid.Message);
        Assert.Equal("dialog-a", dialog.Message);
        Assert.Equal("form-a", form.Message);
        Assert.Equal("nav-a", nav.Message);
        Assert.Equal("tabs-a", tabs.Message);
    }

    [Fact]
    public async Task ComponentActionExecutor_DispatchesKnownMudActions_ToTypedExecutors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataGridActionExecutor, StubDataGridActionExecutor>();
        services.AddSingleton<IDialogActionExecutor, StubDialogActionExecutor>();
        services.AddSingleton<IFormActionExecutor, StubFormActionExecutor>();
        services.AddSingleton<INavigationActionExecutor, StubNavigationActionExecutor>();
        services.AddSingleton<ITabsActionExecutor, StubTabsActionExecutor>();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IComponentActionExecutor>();

        var grid = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            "test"));
        var dialog = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId,
            "test"));
        var form = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormValidateActionId,
            "test"));
        var nav = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            AgentComponentCapabilityProfile.NavigationNavigateToActionId,
            "test"));
        var tabs = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentCapabilityProfile.AgentTabsComponentId,
            AgentComponentCapabilityProfile.TabsSwitchTabActionId,
            "test"));

        Assert.Equal("custom-grid", grid.Message);
        Assert.Equal("custom-dialog", dialog.Message);
        Assert.Equal("custom-form", form.Message);
        Assert.Equal("custom-nav", nav.Message);
        Assert.Equal("custom-tabs", tabs.Message);
    }

    [Fact]
    public async Task ComponentActionExecutor_ExecutesAgentChatWidgetActions_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IComponentActionExecutor>();

        var result = await executor.ExecuteAsync(new PlannedComponentAction(
            "AgentChatWidget",
            "open_widget",
            "test"));
        var chatState = provider.GetRequiredService<IAgentChatWidgetState>();

        Assert.True(result.Succeeded);
        Assert.Equal("AgentChatWidget", result.ComponentId);
        Assert.Equal("open_widget", result.ActionId);
        Assert.Contains("Opened chat widget", result.Message, StringComparison.Ordinal);
        Assert.True(chatState.IsOpen);
    }

    [Fact]
    public async Task ComponentActionExecutor_ReturnsSafeFailure_ForUnknownActionMapping()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IComponentActionExecutor>();

        var result = await executor.ExecuteAsync(new PlannedComponentAction(
            "UnknownComponent",
            "unknown_action",
            "test"));

        Assert.False(result.Succeeded);
        // With the fallback executor, unknown components try registry lookup first
        // and fail with "No component registry found" when no session context exists
        Assert.Contains("No component registry found for session", result.Message, StringComparison.Ordinal);
        Assert.Contains("UnknownComponent.unknown_action", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComponentActionExecutor_Fallback_ResolvesAgentPrefixedCatalogId_ToComponentType()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        var sessionId = registry.SessionId;
        registry.Register(new TypedStubAgentControllable(
            agentId: "country-select",
            componentType: "Select",
            supportedActionId: AgentComponentCapabilityProfile.SelectSetValueActionId));

        var executor = provider.GetRequiredService<IComponentActionExecutor>();
        var result = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentCapabilityProfile.AgentSelectComponentId,
            AgentComponentCapabilityProfile.SelectSetValueActionId,
            "test",
            new Dictionary<string, object?>
            {
                [AgentRuntimeContextKeys.SessionId] = sessionId,
                ["agentId"] = "country-select",
                ["value"] = "Canada"
            }));

        Assert.True(result.Succeeded);
        Assert.Equal("country-select", result.Message);
    }

    private sealed class StubDataGridActionExecutor : IDataGridActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            DataGridActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentCapabilityProfile.AgentDataGridComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-grid"));
        }
    }

    private sealed class StubDialogActionExecutor : IDialogActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            DialogActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentCapabilityProfile.AgentDialogComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-dialog"));
        }
    }

    private sealed class StubFormActionExecutor : IFormActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            FormActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentCapabilityProfile.AgentFormComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-form"));
        }
    }

    private sealed class StubNavigationActionExecutor : INavigationActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            NavigationActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentCapabilityProfile.AgentNavMenuComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-nav"));
        }
    }

    private sealed class StubTabsActionExecutor : ITabsActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            TabsActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentCapabilityProfile.AgentTabsComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-tabs"));
        }
    }

    private sealed class TestRuntimeEventSubscriber : IAgentRuntimeEventSubscriber
    {
    }

    private sealed class StubRuntimeAdapter : IAgentRuntimeAdapter
    {
        public bool SupportsStreaming => false;

        public bool SupportsReconnect => false;

        public bool SupportsCancellation => false;

        public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new AgentTurnResponse("adapter", "adapter-response", [], []));
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            _ = runId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }

    [global::AgentBlazor.App.AgentCapability("workflow_scaffold", Name = "Workflow Scaffold")]
    private sealed class WorkflowScaffoldCapabilities
    {
        [global::AgentBlazor.Attributes.AgentAction("Assess the current case")]
        public global::AgentBlazor.App.CapabilityResult AssessCase() => global::AgentBlazor.App.CapabilityResult.Success("assessed");

        [global::AgentBlazor.Attributes.AgentAction("Prepare the current case", RequiresApproval = true)]
        public global::AgentBlazor.App.CapabilityResult PrepareCase() => global::AgentBlazor.App.CapabilityResult.Success("prepared");
    }

    [global::AgentBlazor.App.AgentCapability("long_capability_name", Name = "Long Capability Name")]
    private sealed class LongCapabilityNameCapabilities
    {
        [global::AgentBlazor.Attributes.AgentAction(
            "Prepare a very long workflow step",
            ActionId = "release_dossier_super_long_semantic_prepare_release_packet_for_final_review")]
        public global::AgentBlazor.App.CapabilityResult PrepareReleasePacket()
            => global::AgentBlazor.App.CapabilityResult.Success("prepared");
    }

    [global::AgentBlazor.App.AgentCapability("approval_ui_contract", Name = "Approval UI Contract")]
    private sealed class ApprovalUiContractCapabilities
    {
        [global::AgentBlazor.Attributes.AgentAction(
            "Draft a support reply",
            ActionId = "draft_reply",
            RequiresApproval = true)]
        public global::AgentBlazor.App.CapabilityResult DraftReply(string ticketId)
            => global::AgentBlazor.App.CapabilityResult.Success($"Prepared draft for {ticketId}.");
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private int _callCount;

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = options;
            _ = cancellationToken;
            var materialized = messages.ToList();
            Requests.Add(materialized);
            var call = Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"response-{call}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "response");
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

    private sealed class StreamingRecordingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hello world")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "hello ");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "world");
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

    private sealed class ParallelProbeChatClient : IChatClient
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;
        private int _callCount;

        public int ActiveCalls => Volatile.Read(ref _activeCalls);

        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);

        public TaskCompletionSource<bool> FirstCallObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> TwoCallsObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseAll { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            var currentActive = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrent(currentActive);
            if (currentActive >= 1)
            {
                FirstCallObserved.TrySetResult(true);
            }

            if (currentActive >= 2)
            {
                TwoCallsObserved.TrySetResult(true);
            }

            var call = Interlocked.Increment(ref _callCount);

            try
            {
                await ReleaseAll.Task.WaitAsync(cancellationToken);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"parallel-{call}"));
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

        private void UpdateMaxConcurrent(int currentActive)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxConcurrentCalls);
                if (currentActive <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, currentActive, snapshot) == snapshot)
                {
                    return;
                }
            }
        }
    }

    private sealed class MultiReconnectStreamingChatClient : IChatClient
    {
        public TaskCompletionSource<bool> FirstChunkDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> AllowCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "alpha omega")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;

            yield return new ChatResponseUpdate(ChatRole.Assistant, "alpha ");
            FirstChunkDelivered.TrySetResult(true);

            await AllowCompletion.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "omega");
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

    private sealed class ToolInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(options?.Tools?.OfType<AIFunction>() ?? []);
            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["column"] = "Risk",
                ["value"] = "High"
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "tool-invoked"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

    private sealed class ApprovalToolInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(options?.Tools?.OfType<AIFunction>() ?? []);
            await tool.InvokeAsync(new AIFunctionArguments(), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "approval-requested"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

    private sealed class ToolInvokingStreamingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(options?.Tools?.OfType<AIFunction>() ?? []);
            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["column"] = "Risk",
                ["value"] = "High"
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "stream-tool-invoked"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(options?.Tools?.OfType<AIFunction>() ?? []);
            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["column"] = "Risk",
                ["value"] = "High"
            }), cancellationToken);

            yield return new ChatResponseUpdate(ChatRole.Assistant, "stream ");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
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

    private sealed class ToolCatalogRecordingChatClient : IChatClient
    {
        public List<IReadOnlyList<string>> ToolSnapshots { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = cancellationToken;
            ToolSnapshots.Add(
                [.. (options?.Tools?.Select(static tool => tool is AIFunction function ? function.Name : tool.GetType().Name) ?? [])]);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "recorded")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

    private sealed class GeneratedUiToolInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function => function.Name.Contains("generated_ui_summary_card", StringComparison.OrdinalIgnoreCase)) ??
                []);
            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["blockId"] = "generated-summary",
                ["title"] = "High Risk Suppliers",
                ["description"] = "These suppliers need review."
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "generated-ui-ready"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

    private sealed class GeneratedUiAndApprovalToolInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var generatedUiTool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    function.Name.Contains("generated_ui_summary_card", StringComparison.OrdinalIgnoreCase)) ??
                []);
            await generatedUiTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["blockId"] = "approval-confirmation",
                ["title"] = "Approve Draft Reply",
                ["description"] = "Do you want to approve the draft reply for ticket TCK-1042?"
            }), cancellationToken);

            var approvalTool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    function.Name.Contains("capability_approval_ui_contract_draft_reply", StringComparison.OrdinalIgnoreCase)) ??
                []);
            await approvalTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["ticketId"] = "TCK-1042"
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                "I can draft the reply, but here is another generated approval request."));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

    private sealed class FormSubmitToolInvokingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function => function.Name.Contains("AgentForm_submit", StringComparison.OrdinalIgnoreCase)) ??
                []);
            await tool.InvokeAsync(new AIFunctionArguments(), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "submit-attempted"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
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

    private sealed class MutableServiceToolRegistry : IAgentServiceToolRegistry
    {
        private IReadOnlyList<AgentServiceTool> _tools = [];

        public IReadOnlyList<AgentServiceTool> GetTools() => _tools;

        public void SetTools(IReadOnlyList<AgentServiceTool> tools)
        {
            _tools = tools;
        }

        public bool TryGetTool(string name, out AgentServiceTool tool)
        {
            foreach (var candidate in _tools)
            {
                if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    tool = candidate;
                    return true;
                }
            }

            tool = default!;
            return false;
        }
    }

    private static AgentServiceTool CreateTestServiceTool(string name)
    {
        return new AgentServiceTool(
            name,
            $"Test tool {name}.",
            [],
            static (_, _, _) => Task.FromResult("ok"));
    }

    private sealed class RecordingComponentActionExecutor : IComponentActionExecutor
    {
        public List<PlannedComponentAction> ExecutedActions { get; } = [];

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ExecutedActions.Add(action);
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                ActionOutcome.Applied,
                "filter-applied"));
        }
    }

    private sealed class ClarificationComponentActionExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                ActionOutcome.NeedsClarification,
                "Which risk level should I use?"));
        }
    }

    private sealed class StubAgentControllable(string agentId) : IAgentControllable
    {
        public string AgentId { get; } = agentId;

        public string ComponentType => "Stub";

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Stub component");
            capability.UpsertAction(new ComponentActionCapability("noop", "No-op action."));
            return capability;
        }

        public ComponentState GetCurrentState() => new();

        public Task<ActionResult> ExecuteActionAsync(
            AgentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(ActionResult.Success($"Handled {action.Name}."));
        }
    }

    private sealed class TypedStubAgentControllable(
        string agentId,
        string componentType,
        string supportedActionId) : IAgentControllable
    {
        public string AgentId { get; } = agentId;

        public string ComponentType { get; } = componentType;

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Typed stub component");
            capability.UpsertAction(new ComponentActionCapability(
                supportedActionId,
                "Stub supported action"));
            return capability;
        }

        public ComponentState GetCurrentState() => new();

        public Task<ActionResult> ExecuteActionAsync(
            AgentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(ActionResult.Success(AgentId));
        }
    }
}
