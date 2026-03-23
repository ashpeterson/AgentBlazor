using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.ExecutionPlans;
using AgentBlazor.Core.Runtime.Tools;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimePlanExecutionTests
{
    [Fact]
    public void BuildPlanRequest_MapsTurnStateIntoPlannerRequest()
    {
        var request = new AgentTurnRequest(
            UserMessage: "show risks",
            SessionId: "session-1",
            UserId: "user-1",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentGenerativeUiSpec.GenerateUiContextKey] = "true"
            });
        var allowedComponents = new[] { CreateComponent("AgentGrid", CreateAction("filter")) };
        var mountedComponents = new[] { new MountedComponentState { AgentId = "grid-1", ComponentType = "AgentGrid" } };
        var conversationHistory = new[] { new ConversationTurn { Role = "user", Content = "previous" } };
        var sharedState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["page"] = "home" };
        var routes = new[] { new AvailableRoute { Path = "/", Description = "Home", Aliases = ["home"] } };
        var serviceTools = new[] { new AgentServiceTool("tool-1", "desc", [], (_, _, _) => Task.FromResult("ok")) };

        var planRequest = RuntimePlanExecution.BuildPlanRequest(
            request,
            sessionId: "session-1",
            allowedComponents,
            mountedComponents,
            conversationHistory,
            sharedState,
            routes,
            agentInstructions: "Be concise.",
            currentRoute: "/home",
            serviceTools);

        Assert.Equal("show risks", planRequest.UserMessage);
        Assert.Equal("session-1", planRequest.SessionId);
        Assert.Equal("user-1", planRequest.UserId);
        Assert.True(planRequest.GenerateUi);
        Assert.Same(allowedComponents, planRequest.AvailableComponents);
        Assert.Same(mountedComponents, planRequest.MountedComponents);
        Assert.Same(conversationHistory, planRequest.ConversationHistory);
        Assert.Same(sharedState, planRequest.SharedState);
        Assert.Same(routes, planRequest.AvailableRoutes);
        Assert.Equal("Be concise.", planRequest.AgentInstructions);
        Assert.Equal("/home", planRequest.CurrentRoute);
        Assert.Same(serviceTools, planRequest.ServiceTools);
    }

    [Fact]
    public void BuildExecutionOptions_UsesRunIdFromContextWhenPresent()
    {
        var options = RuntimePlanExecution.BuildExecutionOptions(
            "session-1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["run_id"] = "run-123"
            },
            "run_id");

        Assert.Equal("session-1", options.SessionId);
        Assert.Equal("run-123", options.RunId);
        Assert.False(options.ContinueOnFailure);
    }

    [Fact]
    public void Partition_SplitsToolStepsFromComponentSteps()
    {
        var plan = new ActionPlan
        {
            Steps =
            [
                CreateStep("AgentGrid", "filter"),
                CreateStep("tool", "export_csv"),
                CreateStep("AgentDialog", "open")
            ]
        };

        var partition = RuntimePlanExecution.Partition(plan);

        Assert.Single(partition.ToolSteps);
        Assert.Equal("export_csv", partition.ToolSteps[0].ActionId);
        Assert.Equal(2, partition.ComponentPlan.Steps.Count);
        Assert.DoesNotContain(partition.ComponentPlan.Steps, static step => step.ComponentId == "tool");
    }

    [Fact]
    public void CombineExecutionResults_AppendsToolResultsAfterComponentResults()
    {
        var executionResult = new PlanExecutionResult
        {
            Plan = new ActionPlan { Steps = [CreateStep("AgentGrid", "filter")] },
            Succeeded = true,
            StepResults =
            [
                new StepExecutionResult
                {
                    Step = CreateStep("AgentGrid", "filter"),
                    Outcome = ActionOutcome.Applied,
                    Message = "Applied."
                }
            ]
        };
        var toolResults = new[]
        {
            new ComponentActionExecutionResult("tool", "export_csv", ActionOutcome.Applied, "Exported.")
        };

        var combined = RuntimePlanExecution.CombineExecutionResults(executionResult, toolResults);

        Assert.Equal(2, combined.Length);
        Assert.Equal("AgentGrid", combined[0].ComponentId);
        Assert.Equal("tool", combined[1].ComponentId);
    }

    private static AvailableComponent CreateComponent(string componentId, params AvailableAction[] actions)
        => new()
        {
            ComponentId = componentId,
            Description = componentId,
            Actions = actions
        };

    private static AvailableAction CreateAction(string actionId)
        => new()
        {
            ActionId = actionId,
            Description = actionId,
            Parameters = []
        };

    private static PlannedStep CreateStep(string componentId, string actionId)
        => new()
        {
            ComponentId = componentId,
            ActionId = actionId,
            Arguments = new Dictionary<string, object?>()
        };
}
