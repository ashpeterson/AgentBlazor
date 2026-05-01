namespace AgentBlazor.Demo.Configuration;

internal sealed class DemoSecurityOptions
{
    public const string SectionName = "DemoSecurity";
    public const string AgentEndpointRateLimitPolicyName = "demo-agent-endpoints";

    public bool RequireProviderInProduction { get; set; } = true;

    public bool AllowOllamaInProduction { get; set; }

    public bool TrustForwardedHeaders { get; set; }

    public DemoRateLimitingOptions RateLimiting { get; set; } = new();
}

internal sealed class DemoRateLimitingOptions
{
    public bool Enabled { get; set; } = true;

    public int PermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;

    public int QueueLimit { get; set; }
}
