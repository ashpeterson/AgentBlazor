namespace AgentBlazor.Components;

public sealed class ComponentCapabilityCatalogBuilder
{
    private readonly ComponentCapabilityCatalog _catalog;

    internal ComponentCapabilityCatalogBuilder(ComponentCapabilityCatalog catalog)
    {
        _catalog = catalog;
    }

    public ComponentCapabilityCatalogBuilder AddComponent(
        string componentId,
        string description,
        params ComponentActionCapability[] actions)
    {
        var component = new ComponentCapability(componentId, description);
        foreach (var action in actions)
        {
            component.UpsertAction(action);
        }

        _catalog.AddOrUpdate(component);
        return this;
    }

    public ComponentCapabilityCatalogBuilder Enable(
        string componentId,
        params string[] actionIds)
    {
        foreach (var actionId in actionIds)
        {
            _catalog.EnableAction(componentId, actionId);
        }

        return this;
    }
}
