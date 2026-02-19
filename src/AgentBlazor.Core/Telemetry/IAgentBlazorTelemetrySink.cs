namespace AgentBlazor.Telemetry;

public interface IAgentBlazorTelemetrySink
{
    ValueTask TrackRunEventAsync(
        AgentBlazorRunTelemetryEvent telemetryEvent,
        CancellationToken cancellationToken = default);
}
