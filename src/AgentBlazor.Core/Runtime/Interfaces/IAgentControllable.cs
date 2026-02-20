using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Runtime;

namespace AgentBlazor.Core.Runtime.Interfaces;

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
