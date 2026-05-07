using AgentBlazor.Core.Runtime.Components;

namespace AgentBlazor.Runtime;

public sealed record ActionResult(
    ActionOutcome Outcome,
    string Message)
{
    public bool Succeeded => Outcome is ActionOutcome.Applied;

    public static ActionResult Applied(string message) => new(ActionOutcome.Applied, message);

    public static ActionResult Success(string message) => Applied(message);

    public static ActionResult Queued(string message) => new(ActionOutcome.Queued, message);

    public static ActionResult Blocked(string message) => new(ActionOutcome.Blocked, message);

    public static ActionResult NeedsClarification(string message) => new(ActionOutcome.NeedsClarification, message);

    public static ActionResult Failure(string message) => new(ActionOutcome.Failed, message);

    public static ActionResult Unknown(string actionName) =>
        new(ActionOutcome.Failed, $"Unknown action '{actionName}'.");
}
