using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Planning;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Options;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class DeterministicPlanningTests
{
    #region Service Registration Tests

    [Fact]
    public void Services_DefaultMode_UsesDeterministicRuntime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonPlanChatClient("""{"steps":[]}"""));
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        Assert.IsType<DeterministicAgentRuntime>(runtime);
    }

    [Fact]
    public void Services_DeterministicMode_UsesDeterministicRuntime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonPlanChatClient("""{"steps":[]}"""));
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        Assert.IsType<DeterministicAgentRuntime>(runtime);
    }

    [Fact]
    public void Services_RegistersAllPlanningServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonPlanChatClient("""{"steps":[]}"""));
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IStructuredActionPlanner>());
        Assert.NotNull(provider.GetService<IPlanValidator>());
        Assert.NotNull(provider.GetService<IPlanExecutor>());
    }

    #endregion

    #region PlanValidator Tests

    [Fact]
    public void Validator_EmptyPlan_IsValid()
    {
        var validator = new PlanValidator();
        var plan = ActionPlan.Empty();
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.True(result.IsValid);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void Validator_ClarificationPlan_IsNotValid()
    {
        var validator = new PlanValidator();
        var plan = ActionPlan.NeedsClarification("What column should I filter?");
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_ValidStep_PassesValidation()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentDialog",
                    ActionId = "open",
                    Arguments = new Dictionary<string, object?>()
                }
            ]
        };
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.True(result.IsValid);
        Assert.Single(result.StepResults);
        Assert.True(result.StepResults[0].IsValid);
    }

    [Fact]
    public void Validator_UnknownComponent_FailsValidation()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "UnknownComponent",
                    ActionId = "do_something",
                    Arguments = new Dictionary<string, object?>()
                }
            ]
        };
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.False(result.IsValid);
        Assert.Contains("not available", result.StepResults[0].Errors[0]);
    }

    [Fact]
    public void Validator_UnknownAction_FailsValidation()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentDialog",
                    ActionId = "unknown_action",
                    Arguments = new Dictionary<string, object?>()
                }
            ]
        };
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.False(result.IsValid);
        Assert.Contains("not available", result.StepResults[0].Errors[0]);
    }

    [Fact]
    public void Validator_MissingRequiredParameter_FailsValidation()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentNavMenu",
                    ActionId = "navigate_to",
                    Arguments = new Dictionary<string, object?>()  // Missing 'uri' parameter
                }
            ]
        };
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.False(result.IsValid);
        Assert.Contains("uri", result.StepResults[0].MissingParameters);
    }

    [Fact]
    public void Validator_AllRequiredParameters_PassesValidation()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentNavMenu",
                    ActionId = "navigate_to",
                    Arguments = new Dictionary<string, object?> { ["uri"] = "/suppliers" }
                }
            ]
        };
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_InvalidAllowedValue_FailsValidation()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentDataGrid",
                    ActionId = "filter",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["column"] = "Name",
                        ["operator"] = "invalid_op",  // Not in allowed values
                        ["value"] = "test"
                    }
                }
            ]
        };
        var context = CreateValidationContext();

        var result = validator.Validate(plan, context);

        Assert.False(result.IsValid);
        Assert.Contains("not in allowed values", result.StepResults[0].Errors[0]);
    }

    #endregion

    #region PlanExecutor Tests

    [Fact]
    public async Task Executor_EmptyPlan_Succeeds()
    {
        var executor = new PlanExecutor(new SuccessfulActionExecutor());
        var plan = ActionPlan.Empty();
        var options = new PlanExecutionOptions();

        var result = await executor.ExecuteAsync(plan, options);

        Assert.True(result.Succeeded);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public async Task Executor_SingleStep_ExecutesSuccessfully()
    {
        var executor = new PlanExecutor(new SuccessfulActionExecutor());
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentDialog",
                    ActionId = "open",
                    Arguments = new Dictionary<string, object?>()
                }
            ]
        };
        var options = new PlanExecutionOptions();

        var result = await executor.ExecuteAsync(plan, options);

        Assert.True(result.Succeeded);
        Assert.Single(result.StepResults);
        Assert.True(result.StepResults[0].Succeeded);
    }

    [Fact]
    public async Task Executor_FailingStep_StopsByDefault()
    {
        var executor = new PlanExecutor(new FailingActionExecutor());
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "open", Arguments = new Dictionary<string, object?>() },
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "close", Arguments = new Dictionary<string, object?>() }
            ]
        };
        var options = new PlanExecutionOptions { ContinueOnFailure = false };

        var result = await executor.ExecuteAsync(plan, options);

        Assert.False(result.Succeeded);
        Assert.Single(result.StepResults);  // Only first step executed
    }

    [Fact]
    public async Task Executor_FailingStep_ContinuesWhenConfigured()
    {
        var executor = new PlanExecutor(new FailingActionExecutor());
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "open", Arguments = new Dictionary<string, object?>() },
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "close", Arguments = new Dictionary<string, object?>() }
            ]
        };
        var options = new PlanExecutionOptions { ContinueOnFailure = true };

        var result = await executor.ExecuteAsync(plan, options);

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.StepResults.Count);  // Both steps executed
    }

    [Fact]
    public async Task Executor_MultipleSteps_ExecutesInOrder()
    {
        var trackingExecutor = new TrackingActionExecutor();
        var executor = new PlanExecutor(trackingExecutor);
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep { ComponentId = "AgentNavMenu", ActionId = "navigate_to", Arguments = new Dictionary<string, object?> { ["uri"] = "/page1" } },
                new PlannedStep { ComponentId = "AgentDataGrid", ActionId = "filter", Arguments = new Dictionary<string, object?> { ["column"] = "name" } },
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "open", Arguments = new Dictionary<string, object?>() }
            ]
        };
        var options = new PlanExecutionOptions();

        var result = await executor.ExecuteAsync(plan, options);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.StepResults.Count);
        Assert.Equal(["AgentNavMenu.navigate_to", "AgentDataGrid.filter", "AgentDialog.open"], trackingExecutor.ExecutedActions);
    }

    [Fact]
    public async Task Executor_AddsSessionAndRunMetadata_ToExecutionArguments()
    {
        var capturingExecutor = new CapturingActionExecutor();
        var executor = new PlanExecutor(capturingExecutor);
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "AgentDialog",
                    ActionId = "open",
                    Arguments = new Dictionary<string, object?> { ["mode"] = "inline" }
                }
            ]
        };

        var result = await executor.ExecuteAsync(plan, new PlanExecutionOptions
        {
            SessionId = "session-42",
            RunId = "run-42"
        });

        Assert.True(result.Succeeded);
        Assert.Single(capturingExecutor.ExecutedActions);
        var executed = capturingExecutor.ExecutedActions[0];
        var arguments = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(executed.Arguments);
        Assert.Equal("inline", arguments["mode"]?.ToString());
        Assert.Equal("session-42", arguments[AgentRuntimeContextKeys.SessionId]?.ToString());
        Assert.Equal("run-42", arguments[AgentRuntimeContextKeys.RunId]?.ToString());
    }

    [Fact]
    public async Task Executor_CancellationRequested_StopsExecution()
    {
        var slowExecutor = new SlowActionExecutor(TimeSpan.FromSeconds(5));
        var executor = new PlanExecutor(slowExecutor);
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "open", Arguments = new Dictionary<string, object?>() },
                new PlannedStep { ComponentId = "AgentDialog", ActionId = "close", Arguments = new Dictionary<string, object?>() }
            ]
        };
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await executor.ExecuteAsync(plan, new PlanExecutionOptions(), cts.Token));
    }

    #endregion

    #region DeterministicAgentRuntime Integration Tests

    [Fact]
    public async Task Runtime_EmptyPlan_ReturnsNoActionsMessage()
    {
        var services = CreateDeterministicServices("""{"steps":[]}""");
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("hello"));

        Assert.Equal("I understood your request but no actions are needed.", response.ResponseText);
        Assert.Empty(response.PlannedActions);
        Assert.Empty(response.ExecutionResults);
    }

    [Fact]
    public async Task Runtime_ClarificationPlan_ReturnsQuestion()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [],
                "needsClarification": true,
                "clarificationQuestion": "Which column should I filter?"
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("filter the data"));

        Assert.Equal("Which column should I filter?", response.ResponseText);
        Assert.Empty(response.PlannedActions);
    }

    [Fact]
    public async Task Runtime_ClarificationFollowUp_UsesConversationHistoryAndMountedColumns()
    {
        var services = new ServiceCollection();
        var chatClient = new ClarificationAwareChatClient();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddSingleton<IComponentActionExecutor>(new SuccessfulActionExecutor());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new StubRegisteredComponent(
            "supplier-grid",
            "DataGrid",
            new ComponentState
            {
                ["columns"] = new[] { "SupplierName", "RiskScore", "Region" }
            }));

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentblazor.session_id"] = "deterministic-clarify-history"
        };

        var first = await runtime.RunTurnAsync(new AgentTurnRequest(
            "sort suppliers from highest to lowest risk",
            Context: context));
        var second = await runtime.RunTurnAsync(new AgentTurnRequest(
            "risk",
            Context: context));

        Assert.Contains("Which column should I sort by?", first.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.True(chatClient.FirstPromptIncludedMountedColumns);
        Assert.True(chatClient.SecondPromptIncludedConversationHistory);
        Assert.Single(second.ExecutionResults);
        Assert.True(second.ExecutionResults[0].Succeeded);
        Assert.Equal("Done.", second.ResponseText);
    }

    [Fact]
    public async Task Runtime_ValidPlan_ExecutesAndReturnsResults()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ]
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open the dialog"));

        Assert.Equal("Done.", response.ResponseText);
        Assert.Single(response.PlannedActions);
        Assert.Single(response.ExecutionResults);
        Assert.True(response.ExecutionResults[0].Succeeded);
    }

    [Fact]
    public async Task Runtime_GenerativeUiDisabled_DoesNotAttachGeneratedUiDocument()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ],
                "generatedUi": {
                    "specVersion": "agentblazor.ui.v0",
                    "blocks": [
                        {
                            "id": "summary",
                            "kind": "Card",
                            "title": "Summary",
                            "description": "Open dialog"
                        }
                    ]
                }
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open the dialog"));

        Assert.Null(response.GeneratedUi);
    }

    [Fact]
    public async Task Runtime_GenerativeUiEnabled_AttachesGeneratedUiDocument()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentForm",
                        "actionId": "set_field",
                        "arguments": { "field": "SupplierName", "value": "Ash" }
                    }
                ],
                "generatedUi": {
                    "specVersion": "agentblazor.ui.v0",
                    "blocks": [
                        {
                            "id": "supplier-summary",
                            "kind": "Card",
                            "title": "Supplier Update",
                            "description": "SupplierName will be set to Ash."
                        }
                    ]
                }
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AgentGenerativeUiSpec.GenerateUiContextKey] = bool.TrueString
        };

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "set supplier name to ash",
            Context: context));

        Assert.NotNull(response.GeneratedUi);
        var block = Assert.Single(response.GeneratedUi!.Blocks);
        Assert.Equal(AgentUiBlockKind.Card, block.Kind);
        Assert.Equal("supplier-summary", block.Id);
    }

    [Fact]
    public async Task Runtime_MultiStepPlan_ExecutesAllSteps()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentNavMenu",
                        "actionId": "navigate_to",
                        "arguments": { "uri": "/suppliers" }
                    },
                    {
                        "componentId": "AgentDataGrid",
                        "actionId": "filter",
                        "arguments": { "column": "Name", "operator": "contains", "value": "test" }
                    }
                ]
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("go to suppliers and filter"));

        Assert.Equal("Completed 2 actions.", response.ResponseText);
        Assert.Equal(2, response.PlannedActions.Count);
        Assert.Equal(2, response.ExecutionResults.Count);
    }

    [Fact]
    public async Task Runtime_FormActionWithUnmountedForm_InsertsDialogOpenBeforeSetField()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentNavMenu",
                        "actionId": "navigate_to",
                        "arguments": { "uri": "/demo/onboarding" }
                    },
                    {
                        "componentId": "AgentForm",
                        "actionId": "set_field",
                        "arguments": { "field": "name", "value": "Ash" }
                    }
                ]
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open onboarding and set name to ash"));

        Assert.Equal(3, response.PlannedActions.Count);
        Assert.Equal(AgentComponentCapabilityProfile.AgentNavMenuComponentId, response.PlannedActions[0].ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.NavigationNavigateToActionId, response.PlannedActions[0].ActionId);
        Assert.Equal(AgentComponentCapabilityProfile.AgentDialogComponentId, response.PlannedActions[1].ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.DialogOpenActionId, response.PlannedActions[1].ActionId);
        Assert.Equal(AgentComponentCapabilityProfile.AgentFormComponentId, response.PlannedActions[2].ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.FormSetFieldActionId, response.PlannedActions[2].ActionId);
        Assert.Equal("Completed 3 actions.", response.ResponseText);
    }

    [Fact]
    public async Task Runtime_InvalidPlan_ReturnsClarification()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentNavMenu",
                        "actionId": "navigate_to",
                        "arguments": {}
                    }
                ]
            }
            """);  // Missing required 'uri' parameter
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("go somewhere"));

        Assert.Contains("navigate", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(response.PlannedActions);
    }

    [Fact]
    public async Task Runtime_WithTracing_StoresTrace()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ]
            }
            """);
        services.Configure<PromptTracingOptions>(o => o.Enabled = true);

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        Assert.Equal(1, traceStore.Count);
    }

    [Fact]
    public async Task Runtime_FailedExecution_ReportsFailure()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ]
            }
            """, useFailingExecutor: true);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        Assert.False(response.ExecutionResults[0].Succeeded);
        Assert.Contains("failed", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runtime_Streaming_EmitsDeltasAndFinalResponse()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ]
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var streamingRuntime = Assert.IsAssignableFrom<IAgentRuntimeStreaming>(runtime);

        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in streamingRuntime.RunTurnStreamingAsync(new AgentTurnRequest("open dialog")))
        {
            events.Add(streamEvent);
        }

        Assert.NotEmpty(events);
        Assert.Equal(AgentTurnStreamEventKind.RunStarted, events[0].Kind);
        Assert.Equal(AgentTurnStreamEventKind.RunFinished, events[^1].Kind);
        Assert.NotNull(events[^1].Response);
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent);

        var streamedText = string.Concat(events
            .Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent)
            .Select(static e => e.TextDelta));
        Assert.Equal(events[^1].Response!.ResponseText, streamedText);
    }

    [Fact]
    public async Task Runtime_Streaming_ProviderMissing_EmitsFinalResponse()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var streamingRuntime = Assert.IsAssignableFrom<IAgentRuntimeStreaming>(runtime);

        AgentTurnResponse? finalResponse = null;
        var streamedText = new StringBuilder();
        await foreach (var streamEvent in streamingRuntime.RunTurnStreamingAsync(new AgentTurnRequest("open dialog")))
        {
            if (streamEvent.Kind == AgentTurnStreamEventKind.TextMessageContent)
            {
                streamedText.Append(streamEvent.TextDelta);
            }
            else if (streamEvent.Kind == AgentTurnStreamEventKind.RunFinished)
            {
                finalResponse = streamEvent.Response;
            }
        }

        Assert.NotNull(finalResponse);
        Assert.Contains("No provider is configured", finalResponse!.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No provider is configured", streamedText.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runtime_Streaming_ConnectRunStreamAsync_ReplaysCompletedRun()
    {
        var services = CreateDeterministicServices("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ]
            }
            """);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var streamingRuntime = Assert.IsAssignableFrom<IAgentRuntimeStreaming>(runtime);

        var runId = Guid.NewGuid().ToString("N");
        var original = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in streamingRuntime.RunTurnStreamingAsync(new AgentTurnRequest(
                           "open dialog",
                           Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                           {
                               ["agentblazor.run_id"] = runId
                           })))
        {
            original.Add(streamEvent);
        }

        Assert.NotEmpty(original);

        var replay = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in streamingRuntime.ConnectRunStreamAsync(runId))
        {
            replay.Add(streamEvent);
        }

        Assert.Equal(original.Count, replay.Count);
        Assert.All(replay, static streamEvent => Assert.True(streamEvent.IsReplay));
        Assert.Equal(original.Select(static e => e.Kind), replay.Select(static e => e.Kind));
        Assert.Equal(original.Select(static e => e.Sequence), replay.Select(static e => e.Sequence));
    }

    [Fact]
    public async Task Runtime_Streaming_StopRunAsync_CancelsRunWithStoppedError()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonPlanChatClient("""
            {
                "steps": [
                    {
                        "componentId": "AgentDialog",
                        "actionId": "open",
                        "arguments": {}
                    }
                ]
            }
            """));
        services.AddSingleton<IComponentActionExecutor>(new SlowActionExecutor(TimeSpan.FromSeconds(5)));
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var streamingRuntime = Assert.IsAssignableFrom<IAgentRuntimeStreaming>(runtime);

        var runId = Guid.NewGuid().ToString("N");
        var events = new List<AgentTurnStreamEvent>();
        var streamTask = Task.Run(async () =>
        {
            await foreach (var streamEvent in streamingRuntime.RunTurnStreamingAsync(new AgentTurnRequest(
                               "open dialog",
                               Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                               {
                                   ["agentblazor.run_id"] = runId
                               })))
            {
                events.Add(streamEvent);
            }
        });

        await Task.Delay(100);
        var stopped = await streamingRuntime.StopRunAsync(runId);
        await streamTask;

        Assert.True(stopped);
        Assert.Contains(events, static e => e.Kind == AgentTurnStreamEventKind.RunError && e.ErrorCode == "STOPPED");
    }

    #endregion

    #region Helpers

    private static PlanValidationContext CreateValidationContext()
    {
        return new PlanValidationContext
        {
            AllowedComponents =
            [
                new AvailableComponent
                {
                    ComponentId = "AgentDialog",
                    Description = "Dialog component",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "open",
                            Description = "Opens the dialog",
                            Parameters = []
                        },
                        new AvailableAction
                        {
                            ActionId = "close",
                            Description = "Closes the dialog",
                            Parameters = []
                        }
                    ]
                },
                new AvailableComponent
                {
                    ComponentId = "AgentNavMenu",
                    Description = "Navigation menu",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "navigate_to",
                            Description = "Navigate to a URI",
                            Parameters =
                            [
                                new ActionParameter
                                {
                                    Name = "uri",
                                    Type = "string",
                                    Required = true,
                                    Description = "The URI to navigate to"
                                }
                            ]
                        }
                    ]
                },
                new AvailableComponent
                {
                    ComponentId = "AgentDataGrid",
                    Description = "Data grid component",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "filter",
                            Description = "Filter the grid",
                            Parameters =
                            [
                                new ActionParameter { Name = "column", Type = "string", Required = true },
                                new ActionParameter
                                {
                                    Name = "operator",
                                    Type = "string",
                                    Required = true,
                                    AllowedValues = ["eq", "neq", "contains", "startsWith"]
                                },
                                new ActionParameter { Name = "value", Type = "any", Required = true }
                            ]
                        }
                    ]
                }
            ],
            MountedComponents = [],
            ApprovedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IServiceCollection CreateDeterministicServices(string jsonResponse, bool useFailingExecutor = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonPlanChatClient(jsonResponse));

        if (useFailingExecutor)
        {
            services.AddSingleton<IComponentActionExecutor>(new FailingActionExecutor());
        }
        else
        {
            services.AddSingleton<IComponentActionExecutor>(new SuccessfulActionExecutor());
        }

        services.AddAgentBlazorServices();
        return services;
    }

    #endregion

    #region Test Doubles

    private sealed class JsonPlanChatClient(string jsonResponse) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, jsonResponse)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, jsonResponse);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class SuccessfulActionExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Executed {action.ComponentId}.{action.ActionId}"));
        }
    }

    private sealed class FailingActionExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: false,
                Message: "Action failed: component not mounted"));
        }
    }

    private sealed class TrackingActionExecutor : IComponentActionExecutor
    {
        public List<string> ExecutedActions { get; } = [];

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add($"{action.ComponentId}.{action.ActionId}");
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: "OK"));
        }
    }

    private sealed class SlowActionExecutor(TimeSpan delay) : IComponentActionExecutor
    {
        public async Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
                return new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: true,
                    Message: "OK");
            }
            catch (OperationCanceledException)
            {
                return new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: false,
                    Message: "Cancelled");
            }
        }
    }

    private sealed class CapturingActionExecutor : IComponentActionExecutor
    {
        public List<PlannedComponentAction> ExecutedActions { get; } = [];

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add(action);
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: "OK"));
        }
    }

    private sealed class ClarificationAwareChatClient : IChatClient
    {
        private int _callCount;

        public bool FirstPromptIncludedMountedColumns { get; private set; }
        public bool SecondPromptIncludedConversationHistory { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var messageArray = messages.ToArray();
            _callCount++;

            if (_callCount == 1)
            {
                var systemPrompt = GetCombinedText(messageArray.First(static m => m.Role == ChatRole.System));
                FirstPromptIncludedMountedColumns =
                    systemPrompt.Contains("RiskScore", StringComparison.OrdinalIgnoreCase) &&
                    !systemPrompt.Contains("System.String[]", StringComparison.OrdinalIgnoreCase);

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                    {
                        "steps": [],
                        "needsClarification": true,
                        "clarificationQuestion": "Which column should I sort by?"
                    }
                    """)));
            }

            var hasPriorUserTurn = messageArray.Any(m =>
                m.Role == ChatRole.User &&
                GetCombinedText(m).Contains("sort suppliers from highest to lowest risk", StringComparison.OrdinalIgnoreCase));
            var hasPriorAssistantTurn = messageArray.Any(m =>
                m.Role == ChatRole.Assistant &&
                GetCombinedText(m).Contains("Which column should I sort by?", StringComparison.OrdinalIgnoreCase));
            SecondPromptIncludedConversationHistory = hasPriorUserTurn && hasPriorAssistantTurn;

            var secondResponse = SecondPromptIncludedConversationHistory
                ? """
                  {
                      "steps": [
                          {
                              "componentId": "AgentDataGrid",
                              "actionId": "sort",
                              "arguments": {
                                  "column": "RiskScore",
                                  "direction": "desc"
                              }
                          }
                      ]
                  }
                  """
                : """
                  {
                      "steps": [],
                      "needsClarification": true,
                      "clarificationQuestion": "Which column should I sort by?"
                  }
                  """;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, secondResponse)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text ?? string.Empty);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }

        private static string GetCombinedText(ChatMessage message)
        {
            var contentText = string.Concat(message.Contents
                .OfType<TextContent>()
                .Select(static content => content.Text ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(contentText))
            {
                return contentText;
            }

            return message.Text ?? string.Empty;
        }
    }

    private sealed class StubRegisteredComponent(
        string agentId,
        string componentType,
        ComponentState state) : IAgentControllable
    {
        public string AgentId { get; } = agentId;
        public string ComponentType { get; } = componentType;

        public ComponentCapability GetCapability() =>
            new(AgentId, "Stub component");

        public ComponentState GetCurrentState() => state;

        public Task<ActionResult> ExecuteActionAsync(
            AgentAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success("stub"));
    }

    #endregion
}
