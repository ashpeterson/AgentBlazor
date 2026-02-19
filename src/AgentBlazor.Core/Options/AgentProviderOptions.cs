namespace AgentBlazor.Options;

public sealed class AgentProviderOptions
{
    public AgentProviderKind Kind { get; set; } = AgentProviderKind.None;

    public string? Model { get; set; }

    public string? Endpoint { get; set; }

    public string? DeploymentName { get; set; }

    public string? ApiKey { get; set; }

    public IDictionary<string, string> AdditionalSettings { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
