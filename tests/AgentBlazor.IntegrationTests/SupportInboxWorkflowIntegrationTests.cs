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

    [Fact]
    public async Task DraftReplyAsync_TargetsRequestedTicketWithoutPriorHighlight()
    {
        var workflow = new SupportInboxWorkflowService();
        var capabilities = new SupportInboxCapabilities(workflow);

        var result = await capabilities.DraftReplyAsync("TCK-1042");

        Assert.True(result.Succeeded);
        Assert.Contains("Prepared a reply draft", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(workflow.CurrentDraft);
        Assert.True(workflow.IsDraftDialogOpen);
        Assert.Contains("TCK-1042", workflow.HighlightedTicketIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TCK-1042", workflow.CurrentDraft.TicketIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DraftReplyAsync_ReturnsClarificationForUnknownTicket()
    {
        var workflow = new SupportInboxWorkflowService();
        var capabilities = new SupportInboxCapabilities(workflow);

        var result = await capabilities.DraftReplyAsync("TCK-9999");

        Assert.False(result.Succeeded);
        Assert.True(result.RequiresClarification);
        Assert.Contains("could not find ticket TCK-9999", result.ClarificationQuestion, StringComparison.OrdinalIgnoreCase);
        Assert.Null(workflow.CurrentDraft);
        Assert.Empty(workflow.HighlightedTicketIds);
    }
}
