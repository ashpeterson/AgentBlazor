using AgentBlazor.Components;
using AgentBlazor.Options;
using Microsoft.Extensions.Options;

namespace AgentBlazor.DefaultAgent;

[Obsolete("AgentBlazor.DefaultAgent is a legacy compatibility surface. Prefer explicit agent registration and runtime-adapter-backed capability projection.", false)]
internal sealed class DefaultComponentAwareAgentDescriptorProvider(
    IOptions<AgentBlazorOptions> options,
    IComponentCapabilityCatalog catalog) : IDefaultComponentAwareAgentDescriptorProvider
{
    public DefaultComponentAwareAgentDescriptor GetDescriptor()
    {
        var configured = options.Value.DefaultAgent;
        var knownComponents = catalog.GetComponents()
            .Select(static c => c.ComponentId)
            .ToArray();

        return new DefaultComponentAwareAgentDescriptor(
            configured.Name,
            configured.Description,
            knownComponents);
    }
}
