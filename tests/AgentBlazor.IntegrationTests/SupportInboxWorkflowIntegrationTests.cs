using AgentBlazor.Demo.Services;

namespace AgentBlazor.IntegrationTests;

public class SupportInboxWorkflowIntegrationTests
{
    [Fact]
    public void PrepareReplyDraft_IsBlocked_UntilEscalationRuns()
    {
        var workflow = new SupportInboxWorkflowService();

        var focusSummary = workflow.FocusOpenTickets(7);
        var blockedSummary = workflow.PrepareReplyDraft();

        Assert.Contains("Highlighted", focusSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blocked", blockedSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Null(workflow.CurrentDraft);
        Assert.False(workflow.IsDraftDialogOpen);
        Assert.NotEmpty(workflow.LatestDraftBlockers);
        Assert.Contains(workflow.LatestDraftBlockers, blocker => blocker.Contains("TCK-1055", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrepareReplyDraft_Succeeds_AfterEscalationClearsEvidenceBlocker()
    {
        var workflow = new SupportInboxWorkflowService();

        workflow.FocusOpenTickets(7);
        var escalationSummary = workflow.ApplyEscalationPlaybook();
        var draftSummary = workflow.PrepareReplyDraft();

        Assert.Contains("Escalated", escalationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prepared a reply draft", draftSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(workflow.CurrentDraft);
        Assert.True(workflow.IsDraftDialogOpen);
        Assert.Empty(workflow.LatestDraftBlockers);
        Assert.Contains("TCK-1055", workflow.EscalatedTicketIds, StringComparer.OrdinalIgnoreCase);
    }
}
