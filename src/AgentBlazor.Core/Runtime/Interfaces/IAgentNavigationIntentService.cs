using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Stores pending component actions across Blazor navigation boundaries.
/// When the agent issues a filter/sort/etc. action before the destination page
/// has loaded, executors enqueue the action here. Components pick it up on init.
/// </summary>
public interface IAgentNavigationIntentService
{
    void Enqueue(string componentType, AgentAction action);

    void Enqueue(string componentType, string? agentId, AgentAction action);

    IReadOnlyList<AgentAction> Dequeue(string componentType);

    IReadOnlyList<AgentAction> Dequeue(string componentType, string? agentId);

    bool HasPending(string componentType);

    bool HasPending(string componentType, string? agentId);
}

internal sealed class InMemoryAgentNavigationIntentService : IAgentNavigationIntentService
{
    private readonly Dictionary<string, Queue<AgentAction>> _pendingUnscoped =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Queue<AgentAction>>> _pendingScoped =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Enqueue(string componentType, AgentAction action)
        => Enqueue(componentType, agentId: null, action);

    public void Enqueue(string componentType, string? agentId, AgentAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                if (!_pendingUnscoped.TryGetValue(componentType, out var queue))
                {
                    queue = new Queue<AgentAction>();
                    _pendingUnscoped[componentType] = queue;
                }

                queue.Enqueue(action);
                return;
            }

            if (!_pendingScoped.TryGetValue(componentType, out var byAgent))
            {
                byAgent = new Dictionary<string, Queue<AgentAction>>(StringComparer.OrdinalIgnoreCase);
                _pendingScoped[componentType] = byAgent;
            }

            if (!byAgent.TryGetValue(agentId, out var scopedQueue))
            {
                scopedQueue = new Queue<AgentAction>();
                byAgent[agentId] = scopedQueue;
            }

            scopedQueue.Enqueue(action);
        }
    }

    public IReadOnlyList<AgentAction> Dequeue(string componentType)
        => Dequeue(componentType, agentId: null);

    public IReadOnlyList<AgentAction> Dequeue(string componentType, string? agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);

        lock (_gate)
        {
            var items = new List<AgentAction>();

            if (!string.IsNullOrWhiteSpace(agentId))
            {
                DrainScopedQueueUnsafe(componentType, agentId, items);
                DrainUnscopedQueueUnsafe(componentType, items);
                return items;
            }

            DrainUnscopedQueueUnsafe(componentType, items);
            if (_pendingScoped.TryGetValue(componentType, out var byAgent))
            {
                foreach (var pair in byAgent.ToArray())
                {
                    var queue = pair.Value;
                    while (queue.Count > 0)
                    {
                        items.Add(queue.Dequeue());
                    }

                    if (queue.Count == 0)
                    {
                        byAgent.Remove(pair.Key);
                    }
                }
            }

            if (_pendingScoped.TryGetValue(componentType, out var remainingScoped) &&
                remainingScoped.Count == 0)
            {
                _pendingScoped.Remove(componentType);
            }

            return items;
        }
    }

    public bool HasPending(string componentType)
        => HasPending(componentType, agentId: null);

    public bool HasPending(string componentType, string? agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(agentId))
            {
                return HasScopedUnsafe(componentType, agentId) || HasUnscopedUnsafe(componentType);
            }

            return HasUnscopedUnsafe(componentType) || HasAnyScopedUnsafe(componentType);
        }
    }

    private void DrainUnscopedQueueUnsafe(string componentType, ICollection<AgentAction> target)
    {
        if (!_pendingUnscoped.TryGetValue(componentType, out var queue) || queue.Count == 0)
        {
            return;
        }

        while (queue.Count > 0)
        {
            target.Add(queue.Dequeue());
        }

        _pendingUnscoped.Remove(componentType);
    }

    private void DrainScopedQueueUnsafe(string componentType, string agentId, ICollection<AgentAction> target)
    {
        if (!_pendingScoped.TryGetValue(componentType, out var byAgent) ||
            !byAgent.TryGetValue(agentId, out var queue) ||
            queue.Count == 0)
        {
            return;
        }

        while (queue.Count > 0)
        {
            target.Add(queue.Dequeue());
        }

        byAgent.Remove(agentId);
        if (byAgent.Count == 0)
        {
            _pendingScoped.Remove(componentType);
        }
    }

    private bool HasUnscopedUnsafe(string componentType)
        => _pendingUnscoped.TryGetValue(componentType, out var queue) && queue.Count > 0;

    private bool HasScopedUnsafe(string componentType, string agentId)
        => _pendingScoped.TryGetValue(componentType, out var byAgent) &&
           byAgent.TryGetValue(agentId, out var queue) &&
           queue.Count > 0;

    private bool HasAnyScopedUnsafe(string componentType)
    {
        if (!_pendingScoped.TryGetValue(componentType, out var byAgent))
        {
            return false;
        }

        return byAgent.Values.Any(static queue => queue.Count > 0);
    }
}
