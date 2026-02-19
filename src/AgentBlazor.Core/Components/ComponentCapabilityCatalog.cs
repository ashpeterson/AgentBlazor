namespace AgentBlazor.Components;

public sealed class ComponentCapabilityCatalog : IComponentCapabilityCatalog
{
    private readonly Dictionary<string, ComponentCapability> _components =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ComponentCapability> GetComponents() => _components.Values.ToArray();

    public bool TryGet(string componentId, out ComponentCapability capability) =>
        _components.TryGetValue(componentId, out capability!);

    public void AddOrUpdate(ComponentCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        if (_components.TryGetValue(capability.ComponentId, out var existing))
        {
            existing.SetDescription(capability.Description);
            foreach (var action in capability.Actions)
            {
                existing.UpsertAction(action);
            }

            return;
        }

        _components[capability.ComponentId] = capability;
    }

    public void EnableAction(string componentId, string actionId, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        if (!_components.TryGetValue(componentId, out var capability))
        {
            capability = new ComponentCapability(componentId, $"{componentId} capability");
            _components[componentId] = capability;
        }

        capability.UpsertAction(new ComponentActionCapability(
            actionId,
            description ?? $"{actionId} action"));
    }
}
