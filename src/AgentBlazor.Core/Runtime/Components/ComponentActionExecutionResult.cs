namespace AgentBlazor.Core.Runtime.Components;

public sealed record ComponentActionExecutionResult(
    string ComponentId,
    string ActionId,
    ActionOutcome Outcome,
    string Message)
{
    public ComponentActionExecutionResult(
        string ComponentId,
        string ActionId,
        bool Succeeded,
        string Message)
        : this(
            ComponentId,
            ActionId,
            Succeeded ? ActionOutcome.Applied : ActionOutcome.Failed,
            Message)
    {
    }

    public bool Succeeded => Outcome is ActionOutcome.Applied or ActionOutcome.Queued;
}
