using System.Collections.Concurrent;
using AgentBlazor.Telemetry;

namespace AgentBlazor.Demo.Services;

public sealed class DemoTelemetrySink : IAgentBlazorTelemetrySink
{
    private const int MaxEvents = 200;
    private readonly ConcurrentQueue<AgentBlazorRunTelemetryEvent> _events = [];

    public ValueTask TrackRunEventAsync(
        AgentBlazorRunTelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _events.Enqueue(telemetryEvent);
        while (_events.Count > MaxEvents && _events.TryDequeue(out _))
        {
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<AgentBlazorRunTelemetryEvent> GetRecent(int count = 50)
    {
        var safeCount = Math.Clamp(count, 1, MaxEvents);
        return _events
            .Reverse()
            .Take(safeCount)
            .ToArray();
    }
}
