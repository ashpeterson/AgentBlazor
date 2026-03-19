using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Tools;

namespace AgentBlazor.Core.Runtime.Planning;

internal static class RuntimePlanExecution
{
    public static ActionPlanRequest BuildPlanRequest(
        AgentTurnRequest request,
        string sessionId,
        IReadOnlyList<AvailableComponent> allowedComponents,
        IReadOnlyList<MountedComponentState> mountedComponents,
        IReadOnlyList<ConversationTurn> conversationHistory,
        IReadOnlyDictionary<string, string> sharedState,
        IReadOnlyList<AvailableRoute> availableRoutes,
        string? agentInstructions,
        string? currentRoute,
        IReadOnlyList<AgentServiceTool> serviceTools)
        => new()
        {
            UserMessage = request.UserMessage,
            SessionId = sessionId,
            UserId = request.GetEffectiveUserId(),
            GenerateUi = request.Context is not null &&
                         request.Context.TryGetValue(AgentGenerativeUiSpec.GenerateUiContextKey, out var raw) &&
                         bool.TryParse(raw, out var enabled) &&
                         enabled,
            GeneratedUiAction = request.GeneratedUiAction,
            AvailableComponents = allowedComponents,
            MountedComponents = mountedComponents,
            ConversationHistory = conversationHistory,
            SharedState = sharedState,
            AvailableRoutes = availableRoutes,
            AgentInstructions = agentInstructions,
            CurrentRoute = currentRoute,
            ServiceTools = serviceTools
        };

    public static PlanExecutionOptions BuildExecutionOptions(
        string sessionId,
        IDictionary<string, string>? context,
        string runIdContextKey)
        => new()
        {
            ContinueOnFailure = false,
            SessionId = sessionId,
            RunId = context is not null &&
                    context.TryGetValue(runIdContextKey, out var contextRunId) &&
                    !string.IsNullOrWhiteSpace(contextRunId)
                ? contextRunId
                : null
        };

    public static RuntimePlanPartition Partition(ActionPlan plan)
    {
        var toolSteps = plan.Steps
            .Where(static step => string.Equals(step.ComponentId, "tool", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var componentPlan = toolSteps.Length > 0
            ? plan with
            {
                Steps = plan.Steps
                    .Where(static step => !string.Equals(step.ComponentId, "tool", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            }
            : plan;

        return new RuntimePlanPartition(componentPlan, toolSteps);
    }

    public static ComponentActionExecutionResult[] CombineExecutionResults(
        PlanExecutionResult executionResult,
        IReadOnlyList<ComponentActionExecutionResult> toolResults)
        => executionResult.StepResults
            .Select(static result => new ComponentActionExecutionResult(
                result.Step.ComponentId,
                result.Step.ActionId,
                result.Outcome,
                result.Message))
            .Concat(toolResults)
            .ToArray();
}

internal sealed record RuntimePlanPartition(
    ActionPlan ComponentPlan,
    IReadOnlyList<PlannedStep> ToolSteps);
