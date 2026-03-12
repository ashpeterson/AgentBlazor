namespace AgentBlazor.Demo.Components.Pages.Demo;

public sealed class DojoIncidentModel
{
    public string Incident { get; set; } = "Payment API latency spike";
    public string Severity { get; set; } = "P2";
    public string Owner { get; set; } = "Ash";
    public string NextStep { get; set; } = "Confirm impact and prepare ops handoff.";
}
