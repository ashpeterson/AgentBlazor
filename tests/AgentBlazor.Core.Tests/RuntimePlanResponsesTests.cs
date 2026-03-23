using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.ExecutionPlans;
using AgentBlazor.Licensing;

namespace AgentBlazor.Core.Tests;

public sealed class RuntimePlanResponsesTests
{
    [Fact]
    public void BuildValidationFailureResults_UsesTierMessageWhenCurrentTierIsBelowRequiredTier()
    {
        var validationResult = new PlanValidationResult
        {
            Plan = new ActionPlan { Steps = [CreateStep("AgentDataGrid", "filter")] },
            IsValid = false,
            StepResults =
            [
                new StepValidationResult
                {
                    Step = CreateStep("AgentDataGrid", "filter"),
                    IsValid = false,
                    Errors = ["Should not be used when tier blocks first."]
                }
            ]
        };

        var results = RuntimePlanResponses.BuildValidationFailureResults(validationResult, (AgentBlazorTier)(-1));

        var failure = Assert.Single(results);
        Assert.Equal(ActionOutcome.NeedsClarification, failure.Outcome);
        Assert.Contains("requires 'Free' tier", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Current tier: -1", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildValidationClarificationText_PrefersTierFailureMessage()
    {
        var validationResult = new PlanValidationResult
        {
            Plan = new ActionPlan { Steps = [CreateStep("AgentDataGrid", "filter")] },
            IsValid = false,
            StepResults =
            [
                new StepValidationResult
                {
                    Step = CreateStep("AgentDataGrid", "filter"),
                    IsValid = false,
                    MissingParameters = ["column"]
                }
            ]
        };

        var failures = new[]
        {
            new ComponentActionExecutionResult(
                "AgentDataGrid",
                "filter",
                ActionOutcome.NeedsClarification,
                "Action 'AgentDataGrid.filter' requires 'Premium' tier. Current tier: Free.")
        };

        var message = RuntimePlanResponses.BuildValidationClarificationText(validationResult, failures);

        Assert.Equal("Action 'AgentDataGrid.filter' requires 'Premium' tier. Current tier: Free.", message);
    }

    [Fact]
    public void BuildExecutionResponseText_ReturnsFailureBeforeOtherOutcomes()
    {
        var result = new PlanExecutionResult
        {
            Plan = new ActionPlan { Steps = [CreateStep("AgentGrid", "filter")] },
            Succeeded = false,
            StepResults =
            [
                new StepExecutionResult
                {
                    Step = CreateStep("AgentGrid", "filter"),
                    Outcome = ActionOutcome.Failed,
                    Message = "The filter failed."
                },
                new StepExecutionResult
                {
                    Step = CreateStep("AgentGrid", "sort"),
                    Outcome = ActionOutcome.Blocked,
                    Message = "Blocked."
                }
            ]
        };

        var message = RuntimePlanResponses.BuildExecutionResponseText(result);

        Assert.Equal("The filter failed.", message);
    }

    [Fact]
    public void BuildExecutionResponseText_ReturnsBlockedSummaryWhenMultipleBlocked()
    {
        var result = new PlanExecutionResult
        {
            Plan = new ActionPlan { Steps = [CreateStep("AgentGrid", "filter"), CreateStep("AgentGrid", "sort")] },
            Succeeded = false,
            StepResults =
            [
                new StepExecutionResult
                {
                    Step = CreateStep("AgentGrid", "filter"),
                    Outcome = ActionOutcome.Blocked,
                    Message = "Blocked filter."
                },
                new StepExecutionResult
                {
                    Step = CreateStep("AgentGrid", "sort"),
                    Outcome = ActionOutcome.Blocked,
                    Message = "Blocked sort."
                }
            ]
        };

        var message = RuntimePlanResponses.BuildExecutionResponseText(result);

        Assert.Equal("Blocked 2 actions pending approval.", message);
    }

    [Fact]
    public void BuildExecutionResponseText_ReturnsDoneForSingleAppliedAction()
    {
        var result = new PlanExecutionResult
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

        var message = RuntimePlanResponses.BuildExecutionResponseText(result);

        Assert.Equal("Done.", message);
    }

    [Fact]
    public void BuildExecutionResponseText_ForExecutionResults_PrefersFailureOverModelText()
    {
        var executionResults = new[]
        {
            new ComponentActionExecutionResult(
                "AgentGrid",
                "filter",
                ActionOutcome.Failed,
                "The filter failed.")
        };

        var message = RuntimePlanResponses.BuildExecutionResponseText("Model said done.", executionResults);

        Assert.Equal("The filter failed.", message);
    }

    [Fact]
    public void BuildExecutionResponseText_ForExecutionResults_UsesModelTextWhenNoActionsRan()
    {
        var message = RuntimePlanResponses.BuildExecutionResponseText("Here is the answer.", []);

        Assert.Equal("Here is the answer.", message);
    }

    private static PlannedStep CreateStep(string componentId, string actionId)
        => new()
        {
            ComponentId = componentId,
            ActionId = actionId,
            Arguments = new Dictionary<string, object?>()
        };
}
