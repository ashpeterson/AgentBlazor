using System.Reflection;

namespace AgentBlazor.Options;

public sealed class AgentBlazorOptions
{
    public AgentProviderOptions Provider { get; } = new();

    public DefaultAgentOptions DefaultAgent { get; } = new();

    /// <summary>
    /// Assemblies to scan at startup for routes and agent pages (e.g. [Route] pages and AgentComponentIds).
    /// Defaults to the entry assembly when none are added. Used by IRouteRegistry and for UnmountedComponentRoutes.
    /// </summary>
    public IList<Assembly> AssembliesToScan { get; } = new List<Assembly>();

    /// <summary>
    /// When the planner returns a plan whose first step targets a component that is not currently mounted,
    /// the runtime can prepend a navigate_to step to this route (key = component id, e.g. "AgentDataGrid").
    /// Populated automatically from AssembliesToScan via AgentPageDiscovery; can also be set by app config.
    /// </summary>
    public IDictionary<string, string> UnmountedComponentRoutes { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
