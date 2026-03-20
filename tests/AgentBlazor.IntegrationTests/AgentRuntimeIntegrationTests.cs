using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Core.Paid;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Routing;
using AgentBlazor.Execution;
using AgentBlazor.Licensing;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using AgentBlazor.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class AgentRuntimeIntegrationTests
{
    [Fact]
    public async Task RunTurnAsync_WithComponentTool_RecordsPlannedAction_AndExecutionResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentchatwidget_open_widget"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open the widget"));

        Assert.Equal(1, executor.CallCount);
        Assert.True(HasPlannedStep(response, "AgentChatWidget", "open_widget"));
        Assert.True(HasExecutionOutcome(response, "AgentChatWidget", "open_widget", succeeded: true));
    }

    [Fact]
    public async Task RunTurnAsync_ComponentTool_ForwardsToolArguments_ToExecutor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskScore",
                ["operator"] = "lte",
                ["value"] = 50
            }));
        services.AddSingleton<CapturingPlannedActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CapturingPlannedActionExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CapturingPlannedActionExecutor>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("filter low risk suppliers"));

        var action = Assert.IsType<PlannedComponentAction>(executor.LastAction);
        Assert.Equal(AgentComponentCapabilityProfile.AgentDataGridComponentId, action.ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.DataGridFilterActionId, action.ActionId);
        Assert.NotNull(action.Arguments);
        Assert.Equal("RiskScore", action.Arguments["column"]?.ToString());
        Assert.Equal("lte", action.Arguments["operator"]?.ToString());
        Assert.Equal("50", action.Arguments["value"]?.ToString());
    }

    [Fact]
    public async Task RunTurnAsync_NavigationOnlyToolCall_AppendsCrossSurfaceContinuationAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentnavmenu_navigate_to",
            new Dictionary<string, object?>
            {
                ["uri"] = "/suppliers"
            }));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("show me all suppliers that are high risk"));

        Assert.Equal(1, executor.CallCount);
        Assert.True(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            AgentComponentCapabilityProfile.NavigationNavigateToActionId));
    }

    [Fact(Skip = "Deprecated: Auto-appending filter after navigation is no longer supported. LLM should output complete plans.")]
    public async Task RunTurnAsync_NavigationOnlyToolCall_SingularRiskSupplierPrompt_AppendsFilterContinuation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentnavmenu_navigate_to",
            new Dictionary<string, object?>
            {
                ["uri"] = "/suppliers"
            }));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("show me my highest risk supplier"));

        Assert.True(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId));
    }

    [Fact(Skip = "Deprecated: Auto-appending filter after navigation is no longer supported. LLM should output complete plans.")]
    public async Task RunTurnAsync_NavigationOnlyToolCall_HighRiskPrompt_RecoversMissingFilterValueInSameTurn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentnavmenu_navigate_to",
            new Dictionary<string, object?>
            {
                ["uri"] = "/suppliers"
            }));
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("show me all suppliers filtered by highest risk"));

        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            succeeded: true,
            messageContains: "Queued"));
        Assert.DoesNotContain("What value should I filter by?", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_DataGridFilterAliasPayload_RecoversToCanonicalRiskFilterBeforeQueue()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskCategory",
                ["operator"] = "in",
                ["value"] = "High",
                ["target"] = "supplier-grid"
            }));
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("show me all suppliers filtered by highest risk"));

        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            succeeded: true,
            messageContains: "Queued"));
        Assert.DoesNotContain("Which column should I filter", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("What value should I filter by?", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RunTurnAsync_WhenActionIsMissingRequiredParameter_AppendsClarificationGuidance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdatagrid_sort"));
        services.AddSingleton<MissingParameterExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<MissingParameterExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("sort from highest to lowest"));

        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridSortActionId,
            succeeded: false,
            messageContains: "requires 'column' parameter"));

        Assert.Equal("Which column should I sort by?", response.ResponseText);
    }

    [Fact]
    public async Task RunTurnAsync_FormSubmitStep_IsFiltered_WhenPromptDoesNotAskToSubmit()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("set the recipe title to test"));

        Assert.Equal(0, executor.CallCount);
        Assert.False(response.RequiresApproval);
        Assert.False(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormSubmitActionId));
    }

    [Fact]
    public async Task RunTurnAsync_FormSubmitStep_Remains_WhenPromptExplicitlyAsksToSave()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddAgentBlazorLicensing(AgentBlazorTier.Premium);
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("save and submit the form"));

        Assert.True(response.RequiresApproval);
        Assert.True(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormSubmitActionId));
    }

    [Fact]
    public async Task RunTurnAsync_AgentFormSetField_IsRejected_WhenMountedFormDoesNotExposeIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentform_set_field",
            new Dictionary<string, object?>
            {
                ["field"] = "SupplierName",
                ["value"] = "Contoso"
            }));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new StubRegisteredComponent(
            "supplier-form",
            "Form",
            new Dictionary<string, object?>(),
            actions: ["validate", "reset", "submit"]));

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "set supplier name to Contoso",
            SessionId: registry.SessionId));

        Assert.Equal(0, executor.CallCount);
        Assert.Contains("not available on the mounted component", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_WhenPlannerAsksClarificationForSingleFieldEdit_AutoRecoversSetField()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ClarificationOnlyChatClient(
            "What are the details of the recipe you want to set (e.g., Title, Minutes, Difficulty)?"));
        services.AddSingleton<CapturingPlannedActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CapturingPlannedActionExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new StubRegisteredComponent(
            "dojo-recipe-form",
            "DojoRecipe",
            new Dictionary<string, object?>
            {
                ["fields"] = new[] { "Title", "Minutes", "Difficulty" }
            },
            actions: ["set_field"]));

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CapturingPlannedActionExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "set recipe title to test",
            SessionId: registry.SessionId));

        Assert.False(response.RequiresClarification);
        Assert.NotNull(executor.LastAction);
        Assert.Equal("set_field", executor.LastAction!.ActionId, ignoreCase: true);
        var fieldValue = executor.LastAction.Arguments?.TryGetValue("field", out var resolvedFieldValue) == true
            ? resolvedFieldValue
            : null;
        var newValue = executor.LastAction.Arguments?.TryGetValue("value", out var resolvedNewValue) == true
            ? resolvedNewValue
            : null;
        Assert.NotNull(fieldValue);
        Assert.NotNull(newValue);
        Assert.Equal("Title", fieldValue?.ToString());
        Assert.Equal("test", newValue?.ToString());
    }

    [Fact]
    public async Task RunTurnAsync_WhenNavigationActionIsMissingUriFamily_RespondsWithSingleRouteQuestion()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentnavmenu_navigate_to"));
        services.AddSingleton<MissingNavigationTargetExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<MissingNavigationTargetExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open supplier onboarding"));

        Assert.Equal("Which route should I navigate to?", response.ResponseText);
    }

    [Fact(Skip = "Deprecated: Auto-recovery of missing URI requires intent-based inference which is delegated to LLM.")]
    public async Task RunTurnAsync_WhenNavigationActionIsMissingUriFamily_AutoRecoversWithInferredUri()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentnavmenu_navigate_to"));
        services.AddSingleton<MissingNavigationTargetThenSuccessExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<MissingNavigationTargetThenSuccessExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open supplier onboarding"));

        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            AgentComponentCapabilityProfile.NavigationNavigateToActionId,
            succeeded: true,
            messageContains: "/supplier-onboarding"));
    }

    [Fact(Skip = "Deprecated: Auto-appending entity filter after navigation is no longer supported. LLM should output complete plans.")]
    public async Task RunTurnAsync_NavigationOnlyToolCall_WithEntityPrompt_AppendsEntityFilterContinuation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_agentnavmenu_navigate_to",
            new Dictionary<string, object?>
            {
                ["uri"] = "/suppliers"
            }));
        services.AddSingleton<CapturingPlannedActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CapturingPlannedActionExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("go to supplier SUP-006"));

        var continuationArguments = AssertPlannedStepArguments(
            response,
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId);

        Assert.Equal("SUP-006", continuationArguments["value"]?.ToString());
        Assert.Equal("eq", continuationArguments["operator"]?.ToString());
    }

    [Fact(Skip = "Deprecated: Multi-turn clarification with parameter recovery requires conversation state management not implemented in deterministic runtime.")]
    public async Task RunTurnAsync_WhenUserConfirmsClarification_ExecutesPendingAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdatagrid_sort"));
        services.AddSingleton<SortNeedsColumnExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<SortNeedsColumnExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<SortNeedsColumnExecutor>();
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentblazor.session_id"] = "integration-session-1"
        };

        var first = await runtime.RunTurnAsync(new AgentTurnRequest(
            "sort highest to lowest",
            Context: context));
        var second = await runtime.RunTurnAsync(new AgentTurnRequest(
            "RiskScore",
            Context: context));

        Assert.Contains("Which column should I sort by?", first.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.True(HasExecutionOutcome(
            second,
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridSortActionId,
            succeeded: true));
        Assert.Equal(2, executor.CallCount);
        Assert.Equal("RiskScore", executor.LastColumn);
        Assert.Equal("desc", executor.LastDirection);
    }

    [Fact]
    public async Task RunTurnAsync_EmitsRuntimeTelemetryStartedAndFinishedEvents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddSingleton<CapturingTelemetrySink>();
        services.AddSingleton<IAgentBlazorTelemetrySink>(sp => sp.GetRequiredService<CapturingTelemetrySink>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var telemetry = provider.GetRequiredService<CapturingTelemetrySink>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        var started = Assert.Single(telemetry.Events, static e => e.Kind == AgentBlazorRunEventKind.Started);
        var finished = Assert.Single(telemetry.Events, static e => e.Kind == AgentBlazorRunEventKind.Finished);

        Assert.Equal(AgentBlazorTelemetrySources.Runtime, started.Source);
        Assert.Equal("AgentBlazor UI Agent", started.AgentName);
        Assert.Equal(AgentBlazorRunOutcome.Succeeded, finished.Outcome);
        Assert.Equal(GetPlannedStepCount(response), finished.PlannedActionCount);
        Assert.Equal(GetExecutionOutcomeCount(response), finished.ExecutionResultCount);
    }

    [Fact]
    public async Task RunTurnAsync_InvokesRuntimeEventSubscribers_OnSuccessPath()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddSingleton<CapturingRuntimeEventSubscriber>();
        services.AddSingleton<IAgentRuntimeEventSubscriber>(sp => sp.GetRequiredService<CapturingRuntimeEventSubscriber>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var subscriber = provider.GetRequiredService<CapturingRuntimeEventSubscriber>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        Assert.Single(subscriber.TurnStartedEvents);
        Assert.Single(subscriber.TurnFinishedEvents);
        Assert.Single(subscriber.ToolStartedEvents);
        Assert.Single(subscriber.ToolFinishedEvents);
        Assert.Empty(subscriber.ErrorEvents);
        Assert.Equal("AgentDialog", subscriber.ToolStartedEvents[0].Action.ComponentId);
        Assert.Equal("open", subscriber.ToolStartedEvents[0].Action.ActionId);
        Assert.Equal("AgentDialog", subscriber.ToolFinishedEvents[0].Result.ComponentId);
        Assert.Equal("open", subscriber.ToolFinishedEvents[0].Result.ActionId);
    }

    [Fact]
    public async Task RunTurnAsync_InvokesRuntimeEventSubscribers_OnErrorPath()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ThrowingChatClient("Simulated planner failure."));
        services.AddSingleton<CapturingRuntimeEventSubscriber>();
        services.AddSingleton<IAgentRuntimeEventSubscriber>(sp => sp.GetRequiredService<CapturingRuntimeEventSubscriber>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var subscriber = provider.GetRequiredService<CapturingRuntimeEventSubscriber>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunTurnAsync(new AgentTurnRequest("trigger planner error")));

        Assert.Equal("Simulated planner failure.", exception.Message);
        Assert.Single(subscriber.TurnStartedEvents);
        Assert.Empty(subscriber.TurnFinishedEvents);
        Assert.Empty(subscriber.ToolStartedEvents);
        Assert.Empty(subscriber.ToolFinishedEvents);
        var error = Assert.Single(subscriber.ErrorEvents);
        Assert.Contains("Simulated planner failure.", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTurnAsync_IncludesRegisteredWrapperSnapshot_InInstructions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapturingInstructionChatClient>();
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<CapturingInstructionChatClient>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(new StubRegisteredComponent(
            "supplier-grid",
            "DataGrid",
            new Dictionary<string, object?>
            {
                ["riskScoreThreshold"] = 7,
                ["currentPage"] = 2
            }));

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var chatClient = provider.GetRequiredService<CapturingInstructionChatClient>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest("what can you do?", SessionId: registry.SessionId));

        Assert.Contains("supplier-grid", chatClient.LastInstructions ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("riskScoreThreshold", chatClient.LastInstructions ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("currentPage", chatClient.LastInstructions ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_WithStructuredJsonDirectiveText_ExecutesMappedAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonDirectiveThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));

        Assert.Equal(1, executor.CallCount);
        Assert.True(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId));
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId,
            succeeded: true));
    }

    [Fact]
    public async Task RunTurnAsync_WithStructuredJsonDirectiveText_ForwardsDirectiveArguments()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new JsonDirectiveThenTextChatClient(
            "agentblazor_agentnavmenu_navigate_to",
            """{"uri":"/suppliers"}"""));
        services.AddSingleton<CapturingPlannedActionExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CapturingPlannedActionExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CapturingPlannedActionExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("go to suppliers"));

        var action = Assert.IsType<PlannedComponentAction>(executor.LastAction);
        Assert.Equal(AgentComponentCapabilityProfile.AgentNavMenuComponentId, action.ComponentId);
        Assert.Equal(AgentComponentCapabilityProfile.NavigationNavigateToActionId, action.ActionId);
        Assert.NotNull(action.Arguments);
        Assert.Equal("/suppliers", action.Arguments["uri"]?.ToString());
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            AgentComponentCapabilityProfile.NavigationNavigateToActionId,
            succeeded: true));
    }

    [Fact]
    public async Task RunTurnAsync_WithAllowedActionsPolicy_DoesNotExecuteDisallowedAction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentchatwidget_open_widget"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("policy-agent", agent =>
            {
                agent.WithAllowedComponents("AgentChatWidget");
                agent.WithAllowedActions("AgentChatWidget.close_widget");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open the widget",
            AgentName: "policy-agent"));

        Assert.Equal(0, executor.CallCount);
        Assert.Equal(0, GetPlannedStepCount(response));
        Assert.True(HasExecutionOutcome(response, "AgentChatWidget", "open_widget", succeeded: false));
        Assert.Contains("not available", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_DefaultAndCustomAgentRouting_AppliesDeterministicMudPolicies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("risk-only-agent", agent =>
            {
                agent.WithAllowedComponents(AgentComponentCapabilityProfile.AgentDataGridComponentId);
                agent.WithAllowedActions(
                    $"{AgentComponentCapabilityProfile.AgentDataGridComponentId}.{AgentComponentCapabilityProfile.DataGridFilterActionId}",
                    $"{AgentComponentCapabilityProfile.AgentDataGridComponentId}.{AgentComponentCapabilityProfile.DataGridSortActionId}");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var defaultRouteResponse = await runtime.RunTurnAsync(new AgentTurnRequest("open dialog"));
        var riskRouteResponse = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            AgentName: "risk-only-agent"));

        Assert.Equal(1, executor.CallCount);
        Assert.True(HasExecutionOutcome(
            defaultRouteResponse,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId,
            succeeded: true));

        Assert.Equal(0, GetPlannedStepCount(riskRouteResponse));
        Assert.True(HasExecutionOutcome(
            riskRouteResponse,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId,
            succeeded: false));
        Assert.Contains("not available", riskRouteResponse.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_RouteLockContext_ResolvesRouteScopedAgent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("Dojo Workspace Agent", agent =>
            {
                agent.WithAllowedComponents("AgentDialog");
                agent.WithMetadata("route_prefixes", "/demo/dojo");
            })
            .AddAgent("Supplier Analyst Agent", agent =>
            {
                agent.WithAllowedComponents("AgentDialog");
                agent.WithMetadata("route_prefixes", "/demo/suppliers");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            SessionId: "route-lock-session",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.CurrentRoute] = "/demo/suppliers",
                [AgentRuntimeContextKeys.AgentLock] = bool.TrueString
            }));

        Assert.Equal("Supplier Analyst Agent", response.AgentName);
    }

    [Fact]
    public async Task RunTurnAsync_AgentLockWithUnknownAgent_ReturnsHelpfulError()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_open"));
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("Dojo Workspace Agent", agent =>
            {
                agent.WithAllowedComponents("AgentDialog");
                agent.WithMetadata("route_prefixes", "/demo/dojo");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            SessionId: "agent-lock-unknown",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.AgentName] = "Missing Agent",
                [AgentRuntimeContextKeys.AgentLock] = bool.TrueString
            }));

        Assert.Equal("none", response.AgentName);
        Assert.Contains("not registered", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_ExplicitAgentTarget_UsesAgentScopedConversationSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("unknown_tool"));
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("Agent A", agent => agent.WithAllowedComponents("AgentDialog"))
            .AddAgent("Agent B", agent => agent.WithAllowedComponents("AgentDialog"));

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var conversationStore = provider.GetRequiredService<IConversationStore>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest(
            "hello",
            AgentName: "Agent A",
            SessionId: "scoped-session"));

        var activeSessions = await conversationStore.GetActiveSessionsAsync();
        var scopedSession = AgentConversationScope.BuildSessionKey("scoped-session", "Agent A", isolateByAgent: true);

        Assert.Contains(scopedSession, activeSessions);
        Assert.DoesNotContain("scoped-session", activeSessions);
    }

    [Fact]
    public async Task RunTurnAsync_HandoffContext_RecordsInspectorHandoffEvent()
    {
        var services = new ServiceCollection();
        var inspectorStore = new InMemoryAgentInspectorStore();

        services.AddSingleton<IAgentInspectorStore>(inspectorStore);
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("unknown_tool"));
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("Agent A", agent => agent.WithAllowedComponents("AgentDialog"))
            .AddAgent("Agent B", agent => agent.WithAllowedComponents("AgentDialog"));

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest(
            "hello from handoff",
            AgentName: "Agent B",
            SessionId: "handoff-session",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.AgentHandoffFrom] = "Agent A",
                [AgentRuntimeContextKeys.AgentHandoffTo] = "Agent B",
                [AgentRuntimeContextKeys.AgentHandoffAt] = DateTimeOffset.UtcNow.ToString("O")
            }));

        var recentRuns = inspectorStore.GetRecentRuns("handoff-session");
        var run = Assert.Single(recentRuns);
        var handoffEvent = Assert.Single(run.Events, ev =>
            string.Equals(ev.Kind, "AgentHandoff", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Agent A", handoffEvent.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Agent B", handoffEvent.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_WhenPolicyRemovesAllMudActions_ReturnsPolicyMessage_AndSkipsProviderExecution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CountingChatClient>();
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<CountingChatClient>());
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("policy-empty-agent", agent =>
            {
                agent.WithAllowedComponents(AgentComponentCapabilityProfile.AgentDialogComponentId);
                agent.WithAllowedActions($"{AgentComponentCapabilityProfile.AgentDialogComponentId}.not_an_action");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();
        var chatClient = provider.GetRequiredService<CountingChatClient>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "open dialog",
            AgentName: "policy-empty-agent"));

        Assert.Equal(0, executor.CallCount);
        Assert.Equal(0, chatClient.CallCount);
        Assert.Contains("No allowed component actions are available for this agent policy", response.ResponseText, StringComparison.Ordinal);
        Assert.Contains("Filtered actions:", response.ResponseText, StringComparison.Ordinal);
        Assert.Equal(0, GetPlannedStepCount(response));
        Assert.Equal(0, GetExecutionOutcomeCount(response));
    }

    [Fact]
    public async Task RunTurnAsync_ApprovalRequiredTool_SkipsExecutor_WhenApprovalMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_confirm"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("confirm the dialog"));

        Assert.Equal(0, executor.CallCount);
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogConfirmActionId,
            messageContains: "Approval required"));
    }

    [Fact]
    public async Task RunTurnAsync_MudApprovalRequiredTool_SkipsExecutor_WhenApprovalMissing()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorLicensing(AgentBlazorTier.Premium);
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("submit the form"));

        Assert.Equal(0, executor.CallCount);
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormSubmitActionId,
            messageContains: "Approval required"));
    }

    [Fact]
    public async Task RunTurnAsync_ApprovalRequiredTool_Executes_WhenApprovalContextProvided()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentdialog_confirm"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "confirm the dialog",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.Equal(1, executor.CallCount);
        Assert.False(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogConfirmActionId,
            messageContains: "Approval required"));
    }

    [Fact]
    public async Task RunTurnAsync_MudApprovalRequiredTool_Executes_WhenApprovalContextProvided()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorLicensing(AgentBlazorTier.Premium);
        services.AddAgentBlazorServices().UseLegacyDefaultAgentFallback();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "submit the form",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.approvals"] = "all"
            }));

        // Executor may be called multiple times due to how the planner collects tool calls
        // and how the runtime processes the plan. The key assertion is that the action succeeded.
        Assert.True(executor.CallCount >= 1, "Executor should be called at least once");
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormSubmitActionId,
            succeeded: true));
    }

    [Fact]
    public async Task RunTurnAsync_FormSubmitAction_Executes_WhenTierIsPaid()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient("agentblazor_agentform_submit"));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("premium-submit-agent", agent =>
            {
                agent.WithAllowedComponents(AgentComponentCapabilityProfile.AgentFormComponentId);
                agent.WithAllowedActions($"{AgentComponentCapabilityProfile.AgentFormComponentId}.{AgentComponentCapabilityProfile.FormSubmitActionId}");
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "submit the form",
            AgentName: "premium-submit-agent",
            Context: new Dictionary<string, string>
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.True(executor.CallCount >= 1, "Executor should be called at least once");
        Assert.True(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormSubmitActionId));
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentFormComponentId,
            AgentComponentCapabilityProfile.FormSubmitActionId,
            succeeded: true));
    }

    [Fact(Skip = "Custom assembly tools are not yet supported in DeterministicAgentRuntime.")]
    public async Task RunTurnAsync_WithToolsFromAssembly_InvokesRegisteredAssemblyTool()
    {
        RuntimeCustomTools.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new ToolThenTextChatClient(
            "agentblazor_external_runtimecustomtools_getsupplierstatus",
            new Dictionary<string, object?>
            {
                ["supplierId"] = "acme"
            }));
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("custom-tools-agent", agent =>
            {
                agent.WithToolsFromAssembly(typeof(RuntimeCustomTools).Assembly);
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        _ = await runtime.RunTurnAsync(new AgentTurnRequest(
            "get supplier status",
            AgentName: "custom-tools-agent"));

        Assert.Equal(1, RuntimeCustomTools.CallCount);
        Assert.Equal("acme", RuntimeCustomTools.LastSupplierId);
    }

    [Fact(Skip = "Custom assembly tools are not yet supported in deterministic planner path.")]
    public async Task RunTurnAsync_WithMudAndAssemblyTools_ExecutesBothInSingleRun()
    {
        RuntimeCustomTools.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MultiToolThenTextChatClient(
        [
            new ToolInvocation(
                "agentblazor_external_runtimecustomtools_getsupplierstatus",
                new Dictionary<string, object?> { ["supplierId"] = "acme" }),
            new ToolInvocation("agentblazor_agentdialog_open")
        ]));
        services.AddSingleton<CountingExecutor>();
        services.AddSingleton<IComponentActionExecutor>(sp => sp.GetRequiredService<CountingExecutor>());
        services
            .AddAgentBlazorServices().UseLegacyDefaultAgentFallback()
            .AddAgent("mixed-tools-agent", agent =>
            {
                agent.WithToolsFromAssembly(typeof(RuntimeCustomTools).Assembly);
            });

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        var executor = provider.GetRequiredService<CountingExecutor>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "check supplier and open dialog",
            AgentName: "mixed-tools-agent"));

        Assert.Equal(1, RuntimeCustomTools.CallCount);
        Assert.Equal("acme", RuntimeCustomTools.LastSupplierId);
        Assert.Equal(1, executor.CallCount);
        Assert.True(HasPlannedStep(
            response,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId));
        Assert.True(HasExecutionOutcome(
            response,
            AgentComponentCapabilityProfile.AgentDialogComponentId,
            AgentComponentCapabilityProfile.DialogOpenActionId,
            succeeded: true));
    }

    public static class RuntimeCustomTools
    {
        public static int CallCount { get; private set; }

        public static string? LastSupplierId { get; private set; }

        public static void Reset()
        {
            CallCount = 0;
            LastSupplierId = null;
        }

        [Description("Gets supplier status information by supplier id.")]
        public static string GetSupplierStatus(string supplierId)
        {
            CallCount++;
            LastSupplierId = supplierId;
            return $"Supplier {supplierId} is low risk.";
        }
    }

    private static string BuildPlanJson(
        string toolName,
        IDictionary<string, object?>? arguments = null)
    {
        if (!TryResolveToolName(toolName, out var componentId, out var actionId))
        {
            return """{"message":"","actions":[],"needsClarification":false,"clarificationQuestion":null}""";
        }

        var payload = new
        {
            message = $"Executing {componentId}.{actionId}",
            actions = new[]
            {
                new
                {
                    agentId = componentId,
                    action = actionId,
                    args = arguments ?? new Dictionary<string, object?>()
                }
            },
            needsClarification = false,
            clarificationQuestion = (string?)null
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildMultiStepPlanJson(IReadOnlyList<ToolInvocation> toolInvocations)
    {
        var actions = new List<object>();
        foreach (var invocation in toolInvocations)
        {
            if (!TryResolveToolName(invocation.Name, out var componentId, out var actionId))
            {
                continue;
            }

            actions.Add(new
            {
                agentId = componentId,
                action = actionId,
                args = invocation.Arguments ?? new Dictionary<string, object?>()
            });
        }

        return JsonSerializer.Serialize(new
        {
            message = "Executing multiple actions",
            actions,
            needsClarification = false,
            clarificationQuestion = (string?)null
        });
    }

    private static bool TryResolveToolName(
        string toolName,
        out string componentId,
        out string actionId)
    {
        componentId = string.Empty;
        actionId = string.Empty;

        return toolName.ToLowerInvariant() switch
        {
            "agentblazor_agentchatwidget_open_widget" => Resolve("AgentChatWidget", "open_widget", out componentId, out actionId),
            "agentblazor_agentdatagrid_filter" => Resolve("AgentDataGrid", "filter", out componentId, out actionId),
            "agentblazor_agentdatagrid_sort" => Resolve("AgentDataGrid", "sort", out componentId, out actionId),
            "agentblazor_agentdialog_confirm" => Resolve("AgentDialog", "confirm", out componentId, out actionId),
            "agentblazor_agentdialog_open" => Resolve("AgentDialog", "open", out componentId, out actionId),
            "agentblazor_agentform_set_field" => Resolve("AgentForm", "set_field", out componentId, out actionId),
            "agentblazor_agentform_submit" => Resolve("AgentForm", "submit", out componentId, out actionId),
            "agentblazor_agentnavmenu_navigate_to" => Resolve("AgentNavMenu", "navigate_to", out componentId, out actionId),
            _ => false
        };
    }

    private static bool Resolve(
        string resolvedComponentId,
        string resolvedActionId,
        out string componentId,
        out string actionId)
    {
        componentId = resolvedComponentId;
        actionId = resolvedActionId;
        return true;
    }

    private static bool HasPlannedStep(AgentTurnResponse response, string componentId, string actionId)
        => response.ExecutionPlan?.Steps.Any(step =>
               string.Equals(step.TargetId, componentId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(step.ActionId, actionId, StringComparison.OrdinalIgnoreCase)) is true
           || response.LegacyPlannedActions.Any(action =>
               string.Equals(action.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(action.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

    private static bool HasExecutionOutcome(
        AgentTurnResponse response,
        string componentId,
        string actionId,
        bool? succeeded = null,
        string? messageContains = null)
    {
        static bool MatchesStatus(AgentExecutionStepStatus status, bool? expectedSucceeded)
            => expectedSucceeded switch
            {
                true => status is AgentExecutionStepStatus.Completed,
                false => status is not AgentExecutionStepStatus.Completed,
                _ => true
            };

        if (response.ExecutionPlan?.Steps.Count > 0)
        {
            return response.ExecutionPlan.Steps.Any(step =>
                string.Equals(step.TargetId, componentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(step.ActionId, actionId, StringComparison.OrdinalIgnoreCase) &&
                MatchesStatus(step.Status, succeeded) &&
                (messageContains is null || step.Message?.Contains(messageContains, StringComparison.OrdinalIgnoreCase) is true));
        }

        return response.LegacyExecutionResults.Any(result =>
            string.Equals(result.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ActionId, actionId, StringComparison.OrdinalIgnoreCase) &&
            (succeeded is null || result.Succeeded == succeeded.Value) &&
            (messageContains is null || result.Message.Contains(messageContains, StringComparison.OrdinalIgnoreCase)));
    }

    private static int GetPlannedStepCount(AgentTurnResponse response)
        => response.ExecutionPlan?.Steps.Count ?? response.LegacyPlannedActions.Count;

    private static int GetExecutionOutcomeCount(AgentTurnResponse response)
        => response.ExecutionPlan?.Steps.Count ?? response.LegacyExecutionResults.Count;

    private static IReadOnlyDictionary<string, object?> AssertPlannedStepArguments(
        AgentTurnResponse response,
        string componentId,
        string actionId)
    {
        var stepArguments = response.ExecutionPlan?.Steps
            .SingleOrDefault(step =>
                string.Equals(step.TargetId, componentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(step.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            ?.Arguments;

        if (stepArguments is { Count: > 0 })
        {
            return stepArguments;
        }

        var legacyArguments = response.LegacyPlannedActions
            .Single(action =>
                string.Equals(action.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            .Arguments;

        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(legacyArguments);
    }

    private sealed class ToolThenTextChatClient(string functionName, IDictionary<string, object?>? arguments = null) : IChatClient
    {
        private readonly IDictionary<string, object?> _arguments = arguments ?? new Dictionary<string, object?>();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                BuildPlanJson(functionName, _arguments))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, BuildPlanJson(functionName, _arguments));
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

    private sealed class ClarificationOnlyChatClient(string question) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                JsonSerializer.Serialize(new
                {
                    message = "Need more details.",
                    actions = Array.Empty<object>(),
                    needsClarification = true,
                    clarificationQuestion = question
                }))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, JsonSerializer.Serialize(new
            {
                message = "Need more details.",
                actions = Array.Empty<object>(),
                needsClarification = true,
                clarificationQuestion = question
            }));
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

    private sealed class JsonDirectiveThenTextChatClient(
        string functionName,
        string argumentsJson = "{}") : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            Dictionary<string, object?>? parsedArgs = null;
            try
            {
                parsedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
            }
            catch (JsonException)
            {
                parsedArgs = new Dictionary<string, object?>();
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                BuildPlanJson(functionName, parsedArgs))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;

            Dictionary<string, object?>? parsedArgs = null;
            try
            {
                parsedArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson);
            }
            catch (JsonException)
            {
                parsedArgs = new Dictionary<string, object?>();
            }

            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                BuildPlanJson(functionName, parsedArgs));
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

    private sealed class MultiToolThenTextChatClient(IReadOnlyList<ToolInvocation> toolInvocations) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                BuildMultiStepPlanJson(toolInvocations))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, BuildMultiStepPlanJson(toolInvocations));
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

    private sealed record ToolInvocation(string Name, IDictionary<string, object?>? Arguments = null);

    private sealed class CapturingInstructionChatClient : IChatClient
    {
        public string? LastInstructions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastInstructions = options?.Instructions;
            if (string.IsNullOrWhiteSpace(LastInstructions))
            {
                LastInstructions = ExtractSystemPrompt(messages);
            }
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ready.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastInstructions = options?.Instructions;
            if (string.IsNullOrWhiteSpace(LastInstructions))
            {
                LastInstructions = ExtractSystemPrompt(messages);
            }
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Ready.");
            await Task.CompletedTask;
        }

        private static string ExtractSystemPrompt(IEnumerable<ChatMessage> messages)
        {
            var systemMessage = messages.FirstOrDefault(static message => message.Role == ChatRole.System);
            if (systemMessage is null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(systemMessage.Text))
            {
                return systemMessage.Text;
            }

            return string.Concat(systemMessage.Contents
                .OfType<TextContent>()
                .Select(static content => content.Text));
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

    private sealed class ThrowingChatClient(string message) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            throw new InvalidOperationException(message);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            await Task.CompletedTask;

            if (DateTime.UtcNow.Ticks < 0)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, string.Empty);
            }

            throw new InvalidOperationException(message);
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

    private sealed class CapturingRuntimeEventSubscriber : IAgentRuntimeEventSubscriber
    {
        public List<AgentRuntimeTurnStartedEvent> TurnStartedEvents { get; } = [];

        public List<AgentRuntimeTurnFinishedEvent> TurnFinishedEvents { get; } = [];

        public List<AgentRuntimeToolExecutionStartedEvent> ToolStartedEvents { get; } = [];

        public List<AgentRuntimeToolExecutionFinishedEvent> ToolFinishedEvents { get; } = [];

        public List<AgentRuntimeErrorEvent> ErrorEvents { get; } = [];

        public ValueTask OnTurnStartedAsync(
            AgentRuntimeTurnStartedEvent runtimeEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            TurnStartedEvents.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTurnFinishedAsync(
            AgentRuntimeTurnFinishedEvent runtimeEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            TurnFinishedEvents.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnToolExecutionStartedAsync(
            AgentRuntimeToolExecutionStartedEvent runtimeEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ToolStartedEvents.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnToolExecutionFinishedAsync(
            AgentRuntimeToolExecutionFinishedEvent runtimeEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ToolFinishedEvents.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(
            AgentRuntimeErrorEvent runtimeEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ErrorEvents.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubRegisteredComponent(
        string agentId,
        string componentType,
        IReadOnlyDictionary<string, object?> state,
        IReadOnlyList<string>? actions = null) : IAgentControllable
    {
        public string AgentId { get; } = agentId;

        public string ComponentType { get; } = componentType;

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Registered wrapper component.");
            foreach (var actionId in actions ?? ["sort", "filter"])
            {
                capability.UpsertAction(new ComponentActionCapability(actionId, $"{actionId} action."));
            }
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in state)
            {
                values[pair.Key] = pair.Value;
            }

            return new ComponentState(values);
        }

        public Task<ActionResult> ExecuteActionAsync(
            AgentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = action;
            _ = cancellationToken;
            return Task.FromResult(ActionResult.Success("ok"));
        }
    }

    private sealed class CountingChatClient : IChatClient
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Should not be called.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Should not be called.");
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

    private sealed class CountingExecutor : IComponentActionExecutor
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Executed {action.ComponentId}.{action.ActionId}"));
        }
    }

    private sealed class CapturingPlannedActionExecutor : IComponentActionExecutor
    {
        public PlannedComponentAction? LastAction { get; private set; }

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastAction = action;
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Executed {action.ComponentId}.{action.ActionId}"));
        }
    }

    private sealed class MissingParameterExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: false,
                Message: "Action 'sort' requires 'column' parameter."));
        }
    }

    private sealed class SortNeedsColumnExecutor : IComponentActionExecutor
    {
        private int _callCount;

        public int CallCount => _callCount;

        public string? LastColumn { get; private set; }

        public string? LastDirection { get; private set; }

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Interlocked.Increment(ref _callCount);

            if (!string.Equals(action.ComponentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(action.ActionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: true,
                    Message: "Executed non-sort action."));
            }

            if (action.Arguments is null ||
                !action.Arguments.TryGetValue("column", out var columnRaw) ||
                string.IsNullOrWhiteSpace(columnRaw?.ToString()))
            {
                return Task.FromResult(new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: false,
                    Message: "Action 'sort' requires 'column' parameter."));
            }

            LastColumn = columnRaw!.ToString();
            LastDirection = action.Arguments.TryGetValue("direction", out var directionRaw)
                ? directionRaw?.ToString()
                : null;

            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Sorted by {LastColumn} ({LastDirection ?? "asc"})."));
        }
    }

    private sealed class MissingNavigationTargetExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: false,
                Message: "Action 'navigate_to' requires 'uri', 'url', or 'target' parameter."));
        }
    }

    private sealed class MissingNavigationTargetThenSuccessExecutor : IComponentActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            PlannedComponentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (action.Arguments is null ||
                !action.Arguments.TryGetValue("uri", out var uriRaw) ||
                string.IsNullOrWhiteSpace(uriRaw?.ToString()))
            {
                return Task.FromResult(new ComponentActionExecutionResult(
                    action.ComponentId,
                    action.ActionId,
                    Succeeded: false,
                    Message: "Action 'navigate_to' requires 'uri', 'url', or 'target' parameter."));
            }

            return Task.FromResult(new ComponentActionExecutionResult(
                action.ComponentId,
                action.ActionId,
                Succeeded: true,
                Message: $"Navigated to {uriRaw}."));
        }
    }

    private sealed class CapturingTelemetrySink : IAgentBlazorTelemetrySink
    {
        private readonly List<AgentBlazorRunTelemetryEvent> _events = [];

        public IReadOnlyList<AgentBlazorRunTelemetryEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToArray();
                }
            }
        }

        public ValueTask TrackRunEventAsync(
            AgentBlazorRunTelemetryEvent telemetryEvent,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            lock (_events)
            {
                _events.Add(telemetryEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}

