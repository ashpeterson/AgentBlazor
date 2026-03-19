using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Components;
using AgentBlazor.Licensing;

namespace AgentBlazor.Core.Runtime.Planning;

internal static class RuntimePlanResponses
{
    public static ComponentActionExecutionResult[] BuildValidationFailureResults(
        PlanValidationResult validationResult,
        AgentBlazorTier effectiveTier)
        => validationResult.StepResults
            .Where(static stepResult => !stepResult.IsValid)
            .Select(stepResult =>
            {
                var requiredTier = AgentComponentTierBoundaries.GetRequiredTier(
                    stepResult.Step.ComponentId,
                    stepResult.Step.ActionId);

                var message = effectiveTier < requiredTier
                    ? $"Action '{stepResult.Step.ComponentId}.{stepResult.Step.ActionId}' requires '{requiredTier}' tier. Current tier: {effectiveTier}."
                    : stepResult.MissingParameters.Count > 0
                        ? $"Action '{stepResult.Step.ActionId}' requires '{stepResult.MissingParameters[0]}' parameter."
                    : stepResult.Errors.FirstOrDefault() ?? "Plan validation failed.";

                return new ComponentActionExecutionResult(
                    stepResult.Step.ComponentId,
                    stepResult.Step.ActionId,
                    Outcome: ActionOutcome.NeedsClarification,
                    Message: message);
            })
            .ToArray();

    public static string BuildValidationClarificationText(
        PlanValidationResult validationResult,
        IReadOnlyList<ComponentActionExecutionResult> validationFailures)
    {
        return validationFailures
            .Select(static failure => failure.Message)
            .FirstOrDefault(static message => message.Contains("Current tier:", StringComparison.OrdinalIgnoreCase))
            ?? validationResult.BuildClarificationQuestion()
            ?? "The plan could not be validated.";
    }

    public static string BuildExecutionResponseText(PlanExecutionResult result)
    {
        if (result.StepResults.Count == 0)
        {
            return "I understood your request but no actions were required.";
        }

        var failures = result.StepResults.Where(static step => step.Outcome is ActionOutcome.Failed).ToList();
        if (failures.Count > 0)
        {
            return failures[0].Message;
        }

        var clarification = result.StepResults.Where(static step => step.Outcome is ActionOutcome.NeedsClarification).ToList();
        if (clarification.Count > 0)
        {
            return clarification[0].Message;
        }

        var blocked = result.StepResults.Where(static step => step.Outcome is ActionOutcome.Blocked).ToList();
        if (blocked.Count > 0)
        {
            return blocked.Count == 1
                ? blocked[0].Message
                : $"Blocked {blocked.Count} actions pending approval.";
        }

        var applied = result.AppliedCount;
        return applied == 1 ? "Done." : $"Completed {applied} actions.";
    }

    public static string BuildExecutionResponseText(
        string? responseText,
        IReadOnlyList<ComponentActionExecutionResult> executionResults)
    {
        var failures = executionResults
            .Where(static result => result.Outcome is ActionOutcome.Failed)
            .ToList();
        if (failures.Count > 0)
        {
            return failures[0].Message;
        }

        var clarification = executionResults
            .Where(static result => result.Outcome is ActionOutcome.NeedsClarification)
            .ToList();
        if (clarification.Count > 0)
        {
            return clarification[0].Message;
        }

        var blocked = executionResults
            .Where(static result => result.Outcome is ActionOutcome.Blocked)
            .ToList();
        if (blocked.Count > 0)
        {
            return blocked.Count == 1
                ? blocked[0].Message
                : $"Blocked {blocked.Count} actions pending approval.";
        }

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            return responseText;
        }

        if (executionResults.Count == 0)
        {
            return "I understood your request but no actions were required.";
        }

        var appliedCount = executionResults.Count(static result => result.Succeeded);
        return appliedCount == 1
            ? "Done."
            : $"Completed {appliedCount} actions.";
    }
}
