using System.Collections.Concurrent;

namespace AgentBlazor.Core.Paid;

public sealed class InMemoryAgentInspectorStore : IAgentInspectorStore
{
    private const int PerSessionCap = 50;

    private readonly ConcurrentDictionary<string, Queue<InspectorRunRecord>> _runsBySession =
        new(StringComparer.OrdinalIgnoreCase);

    public void RecordRun(InspectorRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var queue = _runsBySession.GetOrAdd(run.SessionId, _ => new Queue<InspectorRunRecord>());

        lock (queue)
        {
            queue.Enqueue(run);
            while (queue.Count > PerSessionCap)
                queue.Dequeue();
        }
    }

    public IReadOnlyList<InspectorRunRecord> GetRecentRuns(string sessionId, int limit = 20)
    {
        if (!_runsBySession.TryGetValue(sessionId, out var queue))
            return [];

        lock (queue)
        {
            return queue
                .Reverse()
                .Take(limit)
                .ToList();
        }
    }
}
