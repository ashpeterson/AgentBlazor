using AgentBlazor.Core.Runtime.Agents;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Dependency required before a queued action can be applied.
/// </summary>
public enum PendingActionDependency
{
    ComponentRegistration = 0,
    NavigationCompletion = 1
}

/// <summary>
/// Configuration for queued action orchestration.
/// </summary>
public sealed record PendingActionOptions
{
    /// <summary>
    /// How long the action remains valid in the queue.
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>
    /// Dependency gate before action can be dequeued.
    /// </summary>
    public PendingActionDependency Dependency { get; init; } = PendingActionDependency.ComponentRegistration;

    /// <summary>
    /// Optional route path required when <see cref="Dependency"/> is <see cref="PendingActionDependency.NavigationCompletion"/>.
    /// </summary>
    public string? RequiredRoutePath { get; init; }
}

internal sealed record PendingAgentAction(
    AgentAction Action,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset ExpiresAt,
    PendingActionDependency Dependency,
    string? RequiredRoutePath);

/// <summary>
/// Stores pending component actions across Blazor navigation boundaries.
/// Includes deterministic expiration and dependency checks before apply.
/// </summary>
public interface IAgentNavigationIntentService
{
    void Enqueue(string componentType, AgentAction action);

    void Enqueue(string componentType, string? agentId, AgentAction action);

    void Enqueue(string componentType, string? agentId, AgentAction action, PendingActionOptions options);

    IReadOnlyList<AgentAction> Dequeue(string componentType);

    IReadOnlyList<AgentAction> Dequeue(string componentType, string? agentId);

    bool HasPending(string componentType);

    bool HasPending(string componentType, string? agentId);

    void MarkNavigationCompleted(string? path);
}

internal sealed class InMemoryAgentNavigationIntentService : IAgentNavigationIntentService
{
    private readonly Dictionary<string, Queue<PendingAgentAction>> _pendingUnscoped =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Queue<PendingAgentAction>>> _pendingScoped =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly ILogger<InMemoryAgentNavigationIntentService>? _logger;
    private readonly TimeSpan _defaultTimeToLive = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastNavigationCompletedAt = DateTimeOffset.MinValue;
    private string? _currentPath;

    public InMemoryAgentNavigationIntentService(ILogger<InMemoryAgentNavigationIntentService>? logger = null)
    {
        _logger = logger;
    }

    public void Enqueue(string componentType, AgentAction action)
        => Enqueue(componentType, agentId: null, action, new PendingActionOptions());

    public void Enqueue(string componentType, string? agentId, AgentAction action)
        => Enqueue(componentType, agentId, action, new PendingActionOptions());

    public void Enqueue(string componentType, string? agentId, AgentAction action, PendingActionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var enqueuedAt = DateTimeOffset.UtcNow;
        var ttl = options.TimeToLive.GetValueOrDefault(_defaultTimeToLive);
        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromSeconds(1);
        }

        var pending = new PendingAgentAction(
            Action: action,
            EnqueuedAt: enqueuedAt,
            ExpiresAt: enqueuedAt.Add(ttl),
            Dependency: options.Dependency,
            RequiredRoutePath: NormalizePath(options.RequiredRoutePath));

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                if (!_pendingUnscoped.TryGetValue(componentType, out var queue))
                {
                    queue = new Queue<PendingAgentAction>();
                    _pendingUnscoped[componentType] = queue;
                }

                queue.Enqueue(pending);
                _logger?.LogInformation(
                    "[AgentFlow] NavIntent.Enqueue: ComponentType={ComponentType}, AgentId=(unscoped), ActionId={ActionId}, Dependency={Dependency}, RequiredRoute={RequiredRoute}, ExpiresAt={ExpiresAt:o}, ArgsKeys=[{Keys}]",
                    componentType,
                    action.Name,
                    pending.Dependency,
                    pending.RequiredRoutePath ?? "(none)",
                    pending.ExpiresAt,
                    action.Parameters is null ? "" : string.Join(", ", action.Parameters.Keys));
                return;
            }

            if (!_pendingScoped.TryGetValue(componentType, out var byAgent))
            {
                byAgent = new Dictionary<string, Queue<PendingAgentAction>>(StringComparer.OrdinalIgnoreCase);
                _pendingScoped[componentType] = byAgent;
            }

            if (!byAgent.TryGetValue(agentId, out var scopedQueue))
            {
                scopedQueue = new Queue<PendingAgentAction>();
                byAgent[agentId] = scopedQueue;
            }

            scopedQueue.Enqueue(pending);
            _logger?.LogInformation(
                "[AgentFlow] NavIntent.Enqueue: ComponentType={ComponentType}, AgentId={AgentId}, ActionId={ActionId}, Dependency={Dependency}, RequiredRoute={RequiredRoute}, ExpiresAt={ExpiresAt:o}, ArgsKeys=[{Keys}]",
                componentType,
                agentId,
                action.Name,
                pending.Dependency,
                pending.RequiredRoutePath ?? "(none)",
                pending.ExpiresAt,
                action.Parameters is null ? "" : string.Join(", ", action.Parameters.Keys));
        }
    }

    public IReadOnlyList<AgentAction> Dequeue(string componentType)
        => Dequeue(componentType, agentId: null);

    public IReadOnlyList<AgentAction> Dequeue(string componentType, string? agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var items = new List<AgentAction>();

            if (!string.IsNullOrWhiteSpace(agentId))
            {
                DrainScopedQueueUnsafe(componentType, agentId, items, now);
                DrainUnscopedQueueUnsafe(componentType, items, now);
                _logger?.LogInformation(
                    "[AgentFlow] NavIntent.Dequeue: ComponentType={ComponentType}, AgentId={AgentId}, DequeuedReadyCount={Count}, ActionIds=[{Ids}]",
                    componentType,
                    agentId,
                    items.Count,
                    string.Join(", ", items.Select(static a => a.Name)));
                return items;
            }

            DrainUnscopedQueueUnsafe(componentType, items, now);
            if (_pendingScoped.TryGetValue(componentType, out var byAgent))
            {
                foreach (var pair in byAgent.ToArray())
                {
                    DrainReadyFromQueueUnsafe(pair.Value, items, now);
                    if (pair.Value.Count == 0)
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

            _logger?.LogInformation(
                "[AgentFlow] NavIntent.Dequeue: ComponentType={ComponentType}, AgentId=(all), DequeuedReadyCount={Count}, ActionIds=[{Ids}]",
                componentType,
                items.Count,
                string.Join(", ", items.Select(static a => a.Name)));
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
            var now = DateTimeOffset.UtcNow;
            PruneExpiredUnsafe(componentType, now);

            if (!string.IsNullOrWhiteSpace(agentId))
            {
                return HasScopedUnsafe(componentType, agentId) || HasUnscopedUnsafe(componentType);
            }

            return HasUnscopedUnsafe(componentType) || HasAnyScopedUnsafe(componentType);
        }
    }

    public void MarkNavigationCompleted(string? path)
    {
        lock (_gate)
        {
            _lastNavigationCompletedAt = DateTimeOffset.UtcNow;
            _currentPath = NormalizePath(path);
            _logger?.LogDebug(
                "[AgentFlow] NavIntent.NavigationCompleted: Path={Path}, Timestamp={Timestamp:o}",
                _currentPath ?? "(unknown)",
                _lastNavigationCompletedAt);
        }
    }

    private void DrainUnscopedQueueUnsafe(string componentType, ICollection<AgentAction> target, DateTimeOffset now)
    {
        if (!_pendingUnscoped.TryGetValue(componentType, out var queue) || queue.Count == 0)
        {
            return;
        }

        DrainReadyFromQueueUnsafe(queue, target, now);
        if (queue.Count == 0)
        {
            _pendingUnscoped.Remove(componentType);
        }
    }

    private void DrainScopedQueueUnsafe(
        string componentType,
        string agentId,
        ICollection<AgentAction> target,
        DateTimeOffset now)
    {
        if (!_pendingScoped.TryGetValue(componentType, out var byAgent) ||
            !byAgent.TryGetValue(agentId, out var queue) ||
            queue.Count == 0)
        {
            return;
        }

        DrainReadyFromQueueUnsafe(queue, target, now);
        if (queue.Count == 0)
        {
            byAgent.Remove(agentId);
        }

        if (byAgent.Count == 0)
        {
            _pendingScoped.Remove(componentType);
        }
    }

    private void DrainReadyFromQueueUnsafe(Queue<PendingAgentAction> queue, ICollection<AgentAction> target, DateTimeOffset now)
    {
        var count = queue.Count;
        for (var i = 0; i < count; i++)
        {
            var pending = queue.Dequeue();
            if (pending.ExpiresAt <= now)
            {
                _logger?.LogWarning(
                    "[AgentFlow] NavIntent.Expired: ActionId={ActionId}, Dependency={Dependency}, RequiredRoute={RequiredRoute}, EnqueuedAt={EnqueuedAt:o}, ExpiresAt={ExpiresAt:o}",
                    pending.Action.Name,
                    pending.Dependency,
                    pending.RequiredRoutePath ?? "(none)",
                    pending.EnqueuedAt,
                    pending.ExpiresAt);
                continue;
            }

            if (!IsDependencySatisfiedUnsafe(pending))
            {
                queue.Enqueue(pending);
                continue;
            }

            target.Add(pending.Action);
        }
    }

    private void PruneExpiredUnsafe(string componentType, DateTimeOffset now)
    {
        if (_pendingUnscoped.TryGetValue(componentType, out var unscoped))
        {
            PruneQueueUnsafe(unscoped, now);
            if (unscoped.Count == 0)
            {
                _pendingUnscoped.Remove(componentType);
            }
        }

        if (_pendingScoped.TryGetValue(componentType, out var scoped))
        {
            foreach (var pair in scoped.ToArray())
            {
                PruneQueueUnsafe(pair.Value, now);
                if (pair.Value.Count == 0)
                {
                    scoped.Remove(pair.Key);
                }
            }

            if (scoped.Count == 0)
            {
                _pendingScoped.Remove(componentType);
            }
        }
    }

    private void PruneQueueUnsafe(Queue<PendingAgentAction> queue, DateTimeOffset now)
    {
        var count = queue.Count;
        for (var i = 0; i < count; i++)
        {
            var pending = queue.Dequeue();
            if (pending.ExpiresAt <= now)
            {
                _logger?.LogWarning(
                    "[AgentFlow] NavIntent.Expired: ActionId={ActionId}, Dependency={Dependency}, RequiredRoute={RequiredRoute}, EnqueuedAt={EnqueuedAt:o}, ExpiresAt={ExpiresAt:o}",
                    pending.Action.Name,
                    pending.Dependency,
                    pending.RequiredRoutePath ?? "(none)",
                    pending.EnqueuedAt,
                    pending.ExpiresAt);
                continue;
            }

            queue.Enqueue(pending);
        }
    }

    private bool IsDependencySatisfiedUnsafe(PendingAgentAction pending)
    {
        if (pending.Dependency is PendingActionDependency.ComponentRegistration)
        {
            return true;
        }

        if (pending.Dependency is not PendingActionDependency.NavigationCompletion)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(pending.RequiredRoutePath))
        {
            return !string.IsNullOrWhiteSpace(_currentPath) &&
                   PathMatches(_currentPath!, pending.RequiredRoutePath!);
        }

        return _lastNavigationCompletedAt >= pending.EnqueuedAt;
    }

    private static bool PathMatches(string currentPath, string requiredPath)
    {
        if (string.Equals(currentPath, requiredPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return currentPath.StartsWith(requiredPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
        {
            normalized = absolute.AbsolutePath;
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        var cut = normalized.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            normalized = normalized[..cut];
        }

        return normalized;
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
