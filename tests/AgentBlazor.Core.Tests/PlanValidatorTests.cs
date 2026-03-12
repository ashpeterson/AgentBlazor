using AgentBlazor.Core.Runtime.Planning;

namespace AgentBlazor.Core.Tests;

public class PlanValidatorTests
{
    [Fact]
    public void Validate_RemapsUnknownComponentId_WhenActionUniquelyMatchesAllowedComponent()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "checkout-incident-manager",
                    ActionId = "UpdateControlledIncidentDraft",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["incident"] = "Checkout API outage",
                        ["severity"] = "P1"
                    }
                }
            ]
        };

        var context = new PlanValidationContext
        {
            AllowedComponents =
            [
                new AvailableComponent
                {
                    ComponentId = "DojoRecipe",
                    Description = "Dojo workspace page",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "UpdateControlledIncidentDraft",
                            Description = "Update the incident draft.",
                            Parameters =
                            [
                                new ActionParameter { Name = "incident", Type = "string", Required = false },
                                new ActionParameter { Name = "severity", Type = "string", Required = false, AllowedValues = ["P1", "P2", "P3"] }
                            ]
                        }
                    ]
                }
            ],
            MountedComponents =
            [
                new MountedComponentState
                {
                    AgentId = "dojo-workspace",
                    ComponentType = "DojoRecipe",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "UpdateControlledIncidentDraft",
                            Description = "Update the incident draft.",
                            Parameters =
                            [
                                new ActionParameter { Name = "incident", Type = "string", Required = false },
                                new ActionParameter { Name = "severity", Type = "string", Required = false, AllowedValues = ["P1", "P2", "P3"] }
                            ]
                        }
                    ]
                }
            ]
        };

        var result = validator.Validate(plan, context);

        Assert.True(result.IsValid);
        Assert.Single(result.Plan.Steps);
        Assert.Equal("DojoRecipe", result.Plan.Steps[0].ComponentId);
        Assert.Equal("DojoRecipe", result.StepResults[0].Step.ComponentId);
    }

    [Fact]
    public void Validate_DoesNotRemapUnknownComponentId_WhenActionMatchIsAmbiguous()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "checkout-incident-manager",
                    ActionId = "update_draft",
                    Arguments = new Dictionary<string, object?>()
                }
            ]
        };

        var sharedAction = new AvailableAction
        {
            ActionId = "update_draft",
            Description = "Update the draft.",
            Parameters = []
        };

        var context = new PlanValidationContext
        {
            AllowedComponents =
            [
                new AvailableComponent
                {
                    ComponentId = "DojoRecipe",
                    Description = "Dojo workspace page",
                    Actions = [sharedAction]
                },
                new AvailableComponent
                {
                    ComponentId = "AgentForm",
                    Description = "Some other form",
                    Actions = [sharedAction]
                }
            ],
            MountedComponents =
            [
                new MountedComponentState
                {
                    AgentId = "dojo-workspace",
                    ComponentType = "DojoRecipe",
                    Actions = [sharedAction]
                },
                new MountedComponentState
                {
                    AgentId = "secondary-form",
                    ComponentType = "AgentForm",
                    Actions = [sharedAction]
                }
            ]
        };

        var result = validator.Validate(plan, context);

        Assert.False(result.IsValid);
        Assert.Equal("checkout-incident-manager", result.StepResults[0].Step.ComponentId);
        Assert.Contains("not available or not allowed", result.StepResults[0].Errors[0]);
    }

    [Fact]
    public void Validate_RemapsUnknownComponentAndAction_WhenArgumentsUniquelyMatchSingleMountedAction()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "ownerManagement",
                    ActionId = "fill_owner",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["owner"] = "Steve"
                    }
                }
            ]
        };

        var context = new PlanValidationContext
        {
            AllowedComponents =
            [
                new AvailableComponent
                {
                    ComponentId = "DojoRecipe",
                    Description = "Dojo workspace page",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "UpdateControlledIncidentDraft",
                            Description = "Update the incident draft.",
                            Parameters =
                            [
                                new ActionParameter { Name = "incident", Type = "string", Required = false },
                                new ActionParameter { Name = "severity", Type = "string", Required = false, AllowedValues = ["P1", "P2", "P3"] },
                                new ActionParameter { Name = "owner", Type = "string", Required = false },
                                new ActionParameter { Name = "nextStep", Type = "string", Required = false }
                            ]
                        }
                    ]
                }
            ],
            MountedComponents =
            [
                new MountedComponentState
                {
                    AgentId = "dojo-workspace",
                    ComponentType = "DojoRecipe",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "UpdateControlledIncidentDraft",
                            Description = "Update the incident draft.",
                            Parameters =
                            [
                                new ActionParameter { Name = "incident", Type = "string", Required = false },
                                new ActionParameter { Name = "severity", Type = "string", Required = false, AllowedValues = ["P1", "P2", "P3"] },
                                new ActionParameter { Name = "owner", Type = "string", Required = false },
                                new ActionParameter { Name = "nextStep", Type = "string", Required = false }
                            ]
                        }
                    ]
                }
            ]
        };

        var result = validator.Validate(plan, context);

        Assert.True(result.IsValid);
        Assert.Equal("DojoRecipe", result.Plan.Steps[0].ComponentId);
        Assert.Equal("UpdateControlledIncidentDraft", result.Plan.Steps[0].ActionId);
        Assert.Equal("DojoRecipe", result.StepResults[0].Step.ComponentId);
        Assert.Equal("UpdateControlledIncidentDraft", result.StepResults[0].Step.ActionId);
    }

    [Fact]
    public void Validate_RemapsUnknownComponentId_WhenAliasActionUniquelyMatchesAllowedComponent()
    {
        var validator = new PlanValidator();
        var plan = new ActionPlan
        {
            Steps =
            [
                new PlannedStep
                {
                    ComponentId = "draft-assignment-agent",
                    ActionId = "assign_draft",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["assignee"] = "Steve"
                    }
                }
            ]
        };

        var context = new PlanValidationContext
        {
            AllowedComponents =
            [
                new AvailableComponent
                {
                    ComponentId = "DojoRecipe",
                    Description = "Dojo workspace page",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "assign_draft",
                            Description = "Assign the incident draft.",
                            Parameters =
                            [
                                new ActionParameter { Name = "assignee", Type = "string", Required = false },
                                new ActionParameter { Name = "owner", Type = "string", Required = false }
                            ]
                        }
                    ]
                }
            ],
            MountedComponents =
            [
                new MountedComponentState
                {
                    AgentId = "dojo-workspace",
                    ComponentType = "DojoRecipe",
                    Actions =
                    [
                        new AvailableAction
                        {
                            ActionId = "assign_draft",
                            Description = "Assign the incident draft.",
                            Parameters =
                            [
                                new ActionParameter { Name = "assignee", Type = "string", Required = false },
                                new ActionParameter { Name = "owner", Type = "string", Required = false }
                            ]
                        }
                    ]
                }
            ]
        };

        var result = validator.Validate(plan, context);

        Assert.True(result.IsValid);
        Assert.Equal("DojoRecipe", result.Plan.Steps[0].ComponentId);
        Assert.Equal("assign_draft", result.Plan.Steps[0].ActionId);
    }
}
