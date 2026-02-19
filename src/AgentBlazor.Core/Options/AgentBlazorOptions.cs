namespace AgentBlazor.Options;

public sealed class AgentBlazorOptions
{
    public AgentProviderOptions Provider { get; } = new();

    public DefaultAgentOptions DefaultAgent { get; } = new();
}
