using AgentBlazor.App;
using AgentBlazor.Attributes;

[AgentCapability("support_queue", Name = "Support queue", Description = "Triage support tickets and draft replies.")]
public sealed class SupportQueueCapabilities
{
    [AgentAction("Show open support tickets")]
    public Task<CapabilityResult> ShowOpenTicketsAsync()
        => Task.FromResult(CapabilityResult.Success("Open tickets shown."));

    [AgentAction("Draft ticket reply", RequiresApproval = true)]
    public Task<CapabilityResult> DraftTicketReplyAsync()
        => Task.FromResult(CapabilityResult.Success("Draft reply prepared."));
}
