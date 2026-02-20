using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Core.Runtime.Interfaces;

public interface IComponentActionExecutor
{
    Task<ComponentActionExecutionResult> ExecuteAsync(
        PlannedComponentAction action,
        CancellationToken cancellationToken = default);
}
