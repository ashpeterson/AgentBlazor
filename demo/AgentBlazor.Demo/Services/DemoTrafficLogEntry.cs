using System.Text.Json.Serialization;

namespace AgentBlazor.Demo.Services;

internal sealed record DemoTrafficLogEntry
{
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("visitorHash")]
    public required string VisitorHash { get; init; }

    [JsonPropertyName("userAgentHash")]
    public string? UserAgentHash { get; init; }

    [JsonPropertyName("referrerHost")]
    public string? ReferrerHost { get; init; }
}
