namespace AgentBlazor.Core.Runtime.Components;

/// <summary>
/// Canonical outcome for component action execution.
/// </summary>
public enum ActionOutcome
{
    Applied = 0,
    Queued = 1,
    Blocked = 2,
    NeedsClarification = 3,
    Failed = 4
}
