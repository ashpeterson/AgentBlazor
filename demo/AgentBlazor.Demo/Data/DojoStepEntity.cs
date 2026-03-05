namespace AgentBlazor.Demo.Data;

internal sealed class DojoStepEntity
{
    public int Id { get; set; }

    public string SessionKey { get; set; } = string.Empty;

    public int StepNumber { get; set; }

    public string Text { get; set; } = string.Empty;
}
