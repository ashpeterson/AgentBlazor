namespace AgentBlazor;

public sealed class AgentBlazorConfigurationException : InvalidOperationException
{
    public AgentBlazorConfigurationException(string message)
        : base(message)
    {
    }
}
