namespace AgentBlazor.Runtime;

public sealed record ActionResult(
    bool Succeeded,
    string Message)
{
    public static ActionResult Success(string message) => new(true, message);

    public static ActionResult Failure(string message) => new(false, message);

    public static ActionResult Unknown(string actionName) =>
        new(false, $"Unknown action '{actionName}'.");
}
