namespace AgentBlazor.Runtime;

/// <summary>
/// Stores pending component actions across Blazor navigation boundaries.
/// When the agent issues a filter/sort/etc. action before the destination page
/// has loaded, executors enqueue the action here. Components pick it up on init.
/// </summary>
public interface IAgentNavigationIntentService
{
    void Enqueue(string componentType, AgentAction action);

    IReadOnlyList<AgentAction> Dequeue(string componentType);

    bool HasPending(string componentType);
}

internal sealed class InMemoryAgentNavigationIntentService : IAgentNavigationIntentService
{
    private readonly Dictionary<string, Queue<AgentAction>> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Enqueue(string componentType, AgentAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            if (!_pending.TryGetValue(componentType, out var queue))
            {
                queue = new Queue<AgentAction>();
                _pending[componentType] = queue;
            }

            queue.Enqueue(action);
        }
    }

    public IReadOnlyList<AgentAction> Dequeue(string componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);

        lock (_gate)
        {
            if (!_pending.TryGetValue(componentType, out var queue) || queue.Count == 0)
            {
                return [];
            }

            var items = queue.ToArray();
            queue.Clear();
            return items;
        }
    }

    public bool HasPending(string componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);

        lock (_gate)
        {
            return _pending.TryGetValue(componentType, out var queue) && queue.Count > 0;
        }
    }
}
