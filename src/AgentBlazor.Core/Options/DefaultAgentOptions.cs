using AgentBlazor.Components;

namespace AgentBlazor.Options;

public sealed class DefaultAgentOptions
{
    public bool Enabled { get; set; }

    [Obsolete("Prefer explicit agent targeting. This flag only preserves legacy default-agent implicit fallback behavior.", false)]
    public bool PreferAsImplicitFallback { get; set; }

    public string Name { get; set; } = "AgentBlazor UI Agent";

    public string Description { get; set; } =
        "Built-in AgentBlazor agent with first-party awareness of shipped UI components.";

    public string Instructions { get; set; } =
        "You are AgentBlazor's built-in component-aware agent. Prefer shipped components and their declared capabilities.";

    public ComponentCatalogMode ComponentCatalogMode { get; set; } = ComponentCatalogMode.AllShippedComponents;

    public ISet<string> AllowedComponents { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ISet<string> AllowedActions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
