namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Validates action plans before execution.
/// No inference. No auto-correction. Just validation.
/// </summary>
internal sealed class PlanValidator : IPlanValidator
{
    public PlanValidationResult Validate(ActionPlan plan, PlanValidationContext context)
    {
        if (plan.RequiresClarification)
        {
            return new PlanValidationResult
            {
                Plan = plan,
                IsValid = false,
                StepResults = []
            };
        }

        if (plan.Steps.Count == 0)
        {
            return new PlanValidationResult
            {
                Plan = plan,
                IsValid = true,
                StepResults = []
            };
        }

        var stepResults = new List<StepValidationResult>();
        var allValid = true;

        foreach (var step in plan.Steps)
        {
            var result = ValidateStep(step, context);
            stepResults.Add(result);
            if (!result.IsValid)
            {
                allValid = false;
            }
        }

        return new PlanValidationResult
        {
            Plan = plan,
            IsValid = allValid,
            StepResults = stepResults
        };
    }

    private static StepValidationResult ValidateStep(PlannedStep step, PlanValidationContext context)
    {
        var errors = new List<string>();
        var missingParams = new List<string>();

        // Find the component
        var component = context.AllowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, step.ComponentId, StringComparison.OrdinalIgnoreCase));

        if (component is null)
        {
            errors.Add($"Component '{step.ComponentId}' is not available or not allowed.");
            return new StepValidationResult
            {
                Step = step,
                IsValid = false,
                Errors = errors,
                MissingParameters = missingParams
            };
        }

        // Find the action
        var action = component.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, step.ActionId, StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            errors.Add($"Action '{step.ActionId}' is not available on component '{step.ComponentId}'.");
            return new StepValidationResult
            {
                Step = step,
                IsValid = false,
                Errors = errors,
                MissingParameters = missingParams
            };
        }

        // Check required parameters
        foreach (var param in action.Parameters.Where(p => p.Required))
        {
            if (!step.Arguments.TryGetValue(param.Name, out var value) ||
                value is null ||
                (value is string s && string.IsNullOrWhiteSpace(s)))
            {
                missingParams.Add(param.Name);
            }
            else if (param.AllowedValues?.Count > 0)
            {
                // Validate against allowed values
                var stringValue = value.ToString();
                if (!param.AllowedValues.Any(av =>
                    string.Equals(av, stringValue, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Parameter '{param.Name}' value '{stringValue}' is not in allowed values: {string.Join(", ", param.AllowedValues)}");
                }
            }
        }

        if (missingParams.Count > 0)
        {
            errors.Add($"Missing required parameters: {string.Join(", ", missingParams)}");
        }

        // Check approval requirements
        if (action.RequiresApproval)
        {
            var actionKey = $"{step.ComponentId}.{step.ActionId}";
            if (!context.ApprovedActions.Contains(actionKey))
            {
                errors.Add($"Action '{step.ActionId}' requires approval before execution.");
            }
        }

        return new StepValidationResult
        {
            Step = step,
            IsValid = errors.Count == 0,
            Errors = errors,
            MissingParameters = missingParams
        };
    }
}
