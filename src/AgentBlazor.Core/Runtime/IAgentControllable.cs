using AgentBlazor.Components;

namespace AgentBlazor.Runtime;

public interface IAgentControllable
{
    string AgentId { get; }

    string ComponentType { get; }

    ComponentCapability GetCapability();

    ComponentState GetCurrentState();

    Task<ActionResult> ExecuteActionAsync(
        AgentAction action,
        CancellationToken cancellationToken = default);
}
