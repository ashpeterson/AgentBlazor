namespace AgentBlazor.Core.Runtime.ExecutionPlans;

/// <summary>
/// Validates action plans before execution.
/// Performs deterministic validation and limited step normalization.
/// </summary>
internal sealed class PlanValidator
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
        var normalizedSteps = new List<PlannedStep>(plan.Steps.Count);
        var allValid = true;

        foreach (var step in plan.Steps)
        {
            var result = ValidateStep(step, context);
            stepResults.Add(result);
            normalizedSteps.Add(result.Step);
            if (!result.IsValid)
            {
                allValid = false;
            }
        }

        return new PlanValidationResult
        {
            Plan = plan with { Steps = normalizedSteps },
            IsValid = allValid,
            StepResults = stepResults
        };
    }

    private static StepValidationResult ValidateStep(PlannedStep step, PlanValidationContext context)
    {
        var errors = new List<string>();
        var missingParams = new List<string>();

        var normalizedStep = TryNormalizeStep(step, context);
        var component = context.AllowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, normalizedStep.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (component is null)
        {
            errors.Add($"Component '{step.ComponentId}' is not available or not allowed.");
            return new StepValidationResult
            {
                Step = normalizedStep,
                IsValid = false,
                Errors = errors,
                MissingParameters = missingParams
            };
        }

        var action = component.Actions.FirstOrDefault(a => ActionIdMatches(a.ActionId, normalizedStep.ActionId));
        if (action is null)
        {
            errors.Add($"Action '{normalizedStep.ActionId}' is not available on component '{normalizedStep.ComponentId}'.");
            return new StepValidationResult
            {
                Step = normalizedStep,
                IsValid = false,
                Errors = errors,
                MissingParameters = missingParams
            };
        }

        // When a matching component type is mounted, ensure the action is actually
        // available on the live component instance as well (not just in static catalog data).
        if (!IsActionAvailableOnMountedComponent(normalizedStep, component.ComponentId, context))
        {
            errors.Add($"Action '{normalizedStep.ActionId}' is not available on the mounted component '{normalizedStep.ComponentId}'.");
            return new StepValidationResult
            {
                Step = normalizedStep,
                IsValid = false,
                Errors = errors,
                MissingParameters = missingParams
            };
        }

        // Check required parameters
        foreach (var param in action.Parameters.Where(p => p.Required))
        {
            if (!normalizedStep.Arguments.TryGetValue(param.Name, out var value) ||
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
            var actionKey = $"{normalizedStep.ComponentId}.{normalizedStep.ActionId}";
            if (!context.ApprovedActions.Contains(actionKey))
            {
                errors.Add($"Action '{normalizedStep.ActionId}' requires approval before execution.");
            }
        }

        return new StepValidationResult
        {
            Step = normalizedStep,
            IsValid = errors.Count == 0,
            Errors = errors,
            MissingParameters = missingParams
        };
    }

    private static PlannedStep TryNormalizeStep(PlannedStep step, PlanValidationContext context)
    {
        var exactComponent = context.AllowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, step.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (exactComponent is not null &&
            exactComponent.Actions.Any(a => ActionIdMatches(a.ActionId, step.ActionId)))
        {
            return step;
        }

        var actionMatches = context.AllowedComponents
            .Where(component => component.Actions.Any(action => ActionIdMatches(action.ActionId, step.ActionId)))
            .Where(component => IsActionAvailableOnMountedComponent(step, component.ComponentId, context))
            .ToArray();

        if (actionMatches.Length == 1)
        {
            return step with { ComponentId = actionMatches[0].ComponentId };
        }

        var argumentMatch = TryNormalizeStepByArguments(step, context);
        return argumentMatch ?? step;
    }

    private static PlannedStep? TryNormalizeStepByArguments(PlannedStep step, PlanValidationContext context)
    {
        var argumentKeys = step.Arguments.Keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(NormalizeToken)
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (argumentKeys.Length == 0)
        {
            return null;
        }

        var actionableMounted = context.MountedComponents
            .Where(static component => component.Actions.Count > 0)
            .ToArray();

        var preferredComponentIds = actionableMounted.Length == 1
            ? context.AllowedComponents
                .Where(component => ComponentTypeMatchesAllowedId(actionableMounted[0].ComponentType, component.ComponentId))
                .Select(component => component.ComponentId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var candidates = context.AllowedComponents
            .Where(component => preferredComponentIds is null || preferredComponentIds.Contains(component.ComponentId))
            .SelectMany(component => component.Actions.Select(action => new { Component = component, Action = action }))
            .Where(candidate => IsActionAvailableOnMountedComponent(
                step with { ComponentId = candidate.Component.ComponentId, ActionId = candidate.Action.ActionId },
                candidate.Component.ComponentId,
                context))
            .Where(candidate => AcceptsProvidedArguments(candidate.Action, argumentKeys))
            .ToArray();

        if (candidates.Length != 1)
        {
            return null;
        }

        return step with
        {
            ComponentId = candidates[0].Component.ComponentId,
            ActionId = candidates[0].Action.ActionId
        };
    }

    private static bool IsActionAvailableOnMountedComponent(
        PlannedStep step,
        string allowedComponentId,
        PlanValidationContext context)
    {
        var mountedCandidates = context.MountedComponents
            .Where(m => ComponentTypeMatchesAllowedId(m.ComponentType, allowedComponentId))
            .ToArray();

        if (mountedCandidates.Length == 0)
        {
            // Component not mounted on this route; keep existing behavior for cross-route flows.
            return true;
        }

        return mountedCandidates.Any(m => m.Actions.Any(a => ActionIdMatches(a.ActionId, step.ActionId)));
    }

    private static bool ComponentTypeMatchesAllowedId(string mountedType, string allowedComponentId)
    {
        if (string.Equals(mountedType, allowedComponentId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedAllowed = allowedComponentId.StartsWith("Agent", StringComparison.OrdinalIgnoreCase)
            ? allowedComponentId[5..]
            : allowedComponentId;

        return string.Equals(mountedType, normalizedAllowed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ActionIdMatches(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedA = a.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        var normalizedB = b.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return string.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
    }

    private static bool AcceptsProvidedArguments(AvailableAction action, IReadOnlyCollection<string> argumentKeys)
    {
        if (action.Parameters.Count == 0)
        {
            return false;
        }

        var parameterKeys = action.Parameters
            .Select(static parameter => NormalizeToken(parameter.Name))
            .Where(static key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return argumentKeys.All(parameterKeys.Contains);
    }

    private static string NormalizeToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
