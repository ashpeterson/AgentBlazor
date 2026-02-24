using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Interfaces;

public sealed record DeferredComponentActionEvent(
    string ComponentType,
    string AgentId,
    string ActionId,
    bool Succeeded,
    string Message,
    DateTimeOffset OccurredAt);

public interface IAgentDeferredActionEvents
{
    event Action<DeferredComponentActionEvent>? DeferredActionCompleted;

    void Publish(DeferredComponentActionEvent actionEvent);
}

internal sealed class AgentDeferredActionEvents(
    ILogger<AgentDeferredActionEvents>? logger = null) : IAgentDeferredActionEvents
{
    public event Action<DeferredComponentActionEvent>? DeferredActionCompleted;

    public void Publish(DeferredComponentActionEvent actionEvent)
    {
        ArgumentNullException.ThrowIfNull(actionEvent);

        var handlers = DeferredActionCompleted;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DeferredComponentActionEvent>)handler).Invoke(actionEvent);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Deferred action event subscriber failed for {ComponentType}.{ActionId}.",
                    actionEvent.ComponentType,
                    actionEvent.ActionId);
            }
        }
    }
}
