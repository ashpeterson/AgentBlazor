namespace AgentBlazor.Demo.Services;

internal sealed class DemoRemoteStorageOptions
{
    public const string SectionName = "DemoRemoteStorage";

    public string Adapter { get; set; } = "InMemory";

    public string? HttpBaseUrl { get; set; }

    public string? HttpApiKey { get; set; }

    public string? HttpBearerToken { get; set; }

    public string HttpHandoffPath { get; set; } = "handoff";

    public string HttpValidatePath { get; set; } = "validate";

    public int MaxAttempts { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 150;
}
