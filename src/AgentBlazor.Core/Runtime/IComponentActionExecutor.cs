namespace AgentBlazor.Runtime;

public interface IComponentActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        PlannedComponentAction action,
        CancellationToken cancellationToken = default);
}
