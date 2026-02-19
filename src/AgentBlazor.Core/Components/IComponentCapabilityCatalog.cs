namespace AgentBlazor.Components;

public interface IComponentCapabilityCatalog
{
    IReadOnlyCollection<ComponentCapability> GetComponents();

    bool TryGet(string componentId, out ComponentCapability capability);
}
