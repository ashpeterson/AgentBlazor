using System.Reflection;
using AgentBlazor.Licensing;

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

    /// <summary>
    /// The licensed feature tier. Defaults to Free. Set via UseProLicense() in registration options.
    /// </summary>
    public AgentBlazorTier LicensedTier { get; set; } = AgentBlazorTier.Free;

    /// <summary>
    /// Enables built-in in-app developer inspector tools.
    /// </summary>
    public bool EnableDevTools { get; set; }

    /// <summary>
    /// Automatically opens the developer inspector when enabled.
    /// </summary>
    public bool AutoShowDevTools { get; set; }
}
