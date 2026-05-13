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
        Assert.False(workflow.IsDraftDialogOpen);
        Assert.Empty(workflow.LatestDraftBlockers);
        Assert.Contains("TCK-1055", workflow.EscalatedTicketIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DraftReplyForTicketAsync_TargetsRequestedTicketWithoutPriorHighlight()
    {
        var workflow = new SupportInboxWorkflowService();
        var capabilities = new SupportInboxCapabilities(workflow);

        var result = await capabilities.DraftReplyForTicketAsync("TCK-1042");

        Assert.True(result.Succeeded);
        Assert.Contains("Prepared a reply draft", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 highlighted ticket", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(workflow.CurrentDraft);
        Assert.False(workflow.IsDraftDialogOpen);
        Assert.Contains("TCK-1042", workflow.HighlightedTicketIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TCK-1042", workflow.CurrentDraft.TicketIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Cannot export monthly invoice pack", workflow.CurrentDraft.IssueSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Billing", workflow.CurrentDraft.NextOwner, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("monthly invoice pack", workflow.CurrentDraft.CustomerReply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high priority", workflow.CurrentDraft.CustomerReply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("TCK-1042", Assert.Single(workflow.CurrentDraft.TicketIds));
        Assert.Equal(workflow.CurrentDraft.IssueSummary, Convert.ToString(result.Outputs["draftIssueSummary"]));
        Assert.Equal(workflow.CurrentDraft.NextOwner, Convert.ToString(result.Outputs["draftNextOwner"]));
        Assert.Equal(workflow.CurrentDraft.CustomerReply, Convert.ToString(result.Outputs["draftCustomerReply"]));
    }

    [Fact]
    public void ExplainFocusedTickets_UsesSingularGrammarForSingleTicket()
    {
        var workflow = new SupportInboxWorkflowService();

        Assert.True(workflow.FocusTicket("TCK-1042"));
        var summary = workflow.ExplainFocusedTickets();

        Assert.Contains("1 ticket needing attention", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 ticket has escalation risk", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 tickets are blocked by missing evidence", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1 are", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DraftReplyForTicketAsync_ReturnsClarificationForUnknownTicket()
    {
        var workflow = new SupportInboxWorkflowService();
        var capabilities = new SupportInboxCapabilities(workflow);

        var result = await capabilities.DraftReplyForTicketAsync("TCK-9999");

        Assert.False(result.Succeeded);
        Assert.True(result.RequiresClarification);
        Assert.Contains("could not find ticket TCK-9999", result.ClarificationQuestion, StringComparison.OrdinalIgnoreCase);
        Assert.Null(workflow.CurrentDraft);
        Assert.Empty(workflow.HighlightedTicketIds);
    }

    [Fact]
    public async Task ShowOpenTicketsAsync_ReturnsStructuredErrorForInvalidReviewWindow()
    {
        var workflow = new SupportInboxWorkflowService();
        var capabilities = new SupportInboxCapabilities(workflow);

        var result = await capabilities.ShowOpenTicketsAsync(90);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_review_window", result.Outputs["errorCode"]);
        Assert.Equal("days", result.Outputs["parameterName"]);
        Assert.Equal("an integer from 1 to 30", result.Outputs["expectedShape"]);
        Assert.Equal(90, result.Outputs["actualValue"]);
        Assert.Contains(result.NextActions, action => action.Contains("show_open_tickets", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(workflow.HighlightedTicketIds);
    }
}
