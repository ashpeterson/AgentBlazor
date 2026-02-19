namespace AgentBlazor.Runtime;

public sealed class ComponentState : Dictionary<string, object?>
{
    public ComponentState()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    public ComponentState(IDictionary<string, object?> values)
        : base(values, StringComparer.OrdinalIgnoreCase)
    {
    }
}
