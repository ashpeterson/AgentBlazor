using System.Reflection;

namespace AgentBlazor.Options;

public sealed class AgentBlazorOptions
{
    public AgentProviderOptions Provider { get; } = new();

    public DefaultAgentOptions DefaultAgent { get; } = new();

    /// <summary>
    /// Assemblies to scan at startup for [Route] pages. Used by IRouteRegistry for intent→route resolution.
    /// Defaults to the entry assembly when none are added.
    /// </summary>
    public IList<Assembly> AssembliesToScan { get; } = new List<Assembly>();
}
