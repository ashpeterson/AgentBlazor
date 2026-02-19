namespace AgentBlazor.Telemetry;

internal sealed class NoOpAgentBlazorTelemetrySink : IAgentBlazorTelemetrySink
{
    public ValueTask TrackRunEventAsync(
        AgentBlazorRunTelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default)
    {
        _ = telemetryEvent;
        _ = cancellationToken;
        return ValueTask.CompletedTask;
    }
}
