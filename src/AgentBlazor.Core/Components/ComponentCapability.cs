namespace AgentBlazor.Components;

public sealed class ComponentCapability
{
    private readonly Dictionary<string, ComponentActionCapability> _actions =
        new(StringComparer.OrdinalIgnoreCase);

    public ComponentCapability(string componentId, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ComponentId = componentId;
        Description = description ?? string.Empty;
    }

    public string ComponentId { get; }

    public string Description { get; private set; }

    public IReadOnlyCollection<ComponentActionCapability> Actions => _actions.Values;

    public void SetDescription(string description) => Description = description ?? string.Empty;

    public void UpsertAction(ComponentActionCapability action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actions[action.ActionId] = action;
    }
}
