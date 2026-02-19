namespace AgentBlazor.Runtime;

public sealed record PlannedComponentAction(
    string ComponentId,
    string ActionId,
    string Reason,
    IReadOnlyDictionary<string, object?>? Arguments = null);
