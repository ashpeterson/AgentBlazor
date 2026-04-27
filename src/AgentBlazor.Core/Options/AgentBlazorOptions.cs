using System.Reflection;
using AgentBlazor.Licensing;

namespace AgentBlazor.Options;

public sealed class AgentBlazorOptions
{
    public AgentProviderOptions Provider { get; } = new();

    [Obsolete("DefaultAgent is a legacy compatibility surface. Prefer explicit agent registration via ConfigureBuilder(builder => builder.AddAgent(...)). It only remains for shipped component catalog compatibility during migration.", false)]
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

    /// <summary>
    /// When true, conversation history is scoped per agent for explicitly targeted runs.
    /// This avoids cross-agent history leakage while preserving default single-agent behavior.
    /// </summary>
    public bool IsolateConversationsByAgent { get; set; } = true;

    /// <summary>
    /// Enables runtime agent auto-resolution from current route metadata and agent route metadata.
    /// </summary>
    public bool EnableRouteAgentResolution { get; set; } = true;

    /// <summary>
    /// Normalized Pro data directory when <c>UseProLicense(..., dataDirectory)</c> is enabled.
    /// This is intended for diagnostics and product surfaces that need to explain the active storage path.
    /// </summary>
    public string? ProDataDirectory { get; set; }
}
