using System.ComponentModel;
using AgentBlazor.Components;

namespace AgentBlazor.Options;

[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("DefaultAgentOptions is a legacy compatibility surface. Prefer explicit agent registration via ConfigureBuilder(builder => builder.AddAgent(...)). This type only remains for shipped component catalog compatibility during migration.", false)]
public sealed class DefaultAgentOptions
{
    public ComponentCatalogMode ComponentCatalogMode { get; set; } = ComponentCatalogMode.AllShippedComponents;

    public ISet<string> AllowedComponents { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
