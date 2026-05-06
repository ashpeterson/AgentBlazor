using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace AgentBlazor.Demo.Services;

[AgentCapability("support_inbox", Name = "Support Inbox Workflow", Description = "Review open tickets, explain the queue, draft replies, and escalate blocked cases.", Category = "Workflow")]
internal sealed class SupportInboxCapabilities(SupportInboxWorkflowService workflow)
{
    [AgentAction("Show open tickets that still need a reply", ActionId = "show_open_tickets")]
    public Task<CapabilityResult> ShowOpenTicketsAsync(
        [AgentParam("Include tickets from the last N days", Required = false)] int days = 7)
    {
        var summary = workflow.FocusOpenTickets(days);
        return Task.FromResult(CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["days"] = days,
                ["highlightedTicketIds"] = workflow.HighlightedTicketIds.ToArray(),
                ["visibleTicketCount"] = workflow.VisibleTickets.Count
            },
            NextActions =
            [
                "Explain why the highlighted tickets need attention",
                "Draft a reply for the highlighted tickets"
            ]
        });
    }

    [AgentAction("Explain why the highlighted tickets need attention", ActionId = "explain_open_tickets")]
    public Task<CapabilityResult> ExplainOpenTicketsAsync()
    {
        var summary = workflow.ExplainFocusedTickets();
        return Task.FromResult(CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["explanation"] = workflow.LatestInsight,
                ["highlightedTicketIds"] = workflow.HighlightedTicketIds.ToArray()
            },
            NextActions =
            [
                "Draft a reply for the highlighted tickets"
            ]
        });
    }

    [AgentAction("Draft a reply for a ticket or the highlighted tickets", ActionId = "draft_ticket_reply", RequiresApproval = true)]
    public Task<CapabilityResult> DraftReplyAsync(
        [AgentParam("Optional ticket id to draft a reply for, for example TCK-1042", Required = false)] string? ticketId = null)
    {
        if (!string.IsNullOrWhiteSpace(ticketId) && !workflow.FocusTicket(ticketId))
        {
            return Task.FromResult(CapabilityResult.NeedsClarification(
                $"I could not find ticket {ticketId}. Ask me to show open tickets first or choose a visible ticket id."));
        }

        if (!workflow.HighlightedTicketIds.Any())
        {
            return Task.FromResult(CapabilityResult.NeedsClarification(
                "No tickets are highlighted yet. Ask me to show open tickets first or tell me which ticket needs a reply."));
        }

        var summary = workflow.PrepareReplyDraft();
        var resultFactory = workflow.LatestDraftBlockers.Count > 0
            ? CapabilityResult.Blocked(summary)
            : CapabilityResult.Success(summary);

        return Task.FromResult(resultFactory with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["draftTitle"] = workflow.CurrentDraft?.Title,
                ["draftTicketIds"] = workflow.CurrentDraft?.TicketIds.ToArray(),
                ["draftActionCount"] = workflow.CurrentDraft?.Checklist.Count ?? 0,
                ["draftBlockers"] = workflow.LatestDraftBlockers.ToArray()
            },
            NextActions =
                workflow.LatestDraftBlockers.Count > 0
                ? [
                    "Escalate the blocked tickets",
                    "Draft the reply again once the blockers are cleared"
                ]
                : [
                    "Review the draft reply",
                    "Approve the reply draft"
                ]
        });
    }

    [AgentAction("Escalate the blocked tickets", ActionId = "escalate_blocked_tickets")]
    public Task<CapabilityResult> EscalateBlockedTicketsAsync()
    {
        var summary = workflow.ApplyEscalationPlaybook();
        return Task.FromResult(CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["escalatedTicketIds"] = workflow.EscalatedTicketIds.ToArray(),
                ["remainingBlockerCount"] = workflow.LatestDraftBlockers.Count
            },
            NextActions =
            [
                "Draft a reply for the highlighted tickets again",
                "Review the queue before sending the reply"
            ]
        });
    }

    [AgentAction("Reset the support inbox workflow", ActionId = "reset_support_inbox")]
    public Task<CapabilityResult> ResetSupportInboxAsync()
    {
        workflow.Reset();
        return Task.FromResult(CapabilityResult.Success("Reset the support inbox workflow."));
    }
}

internal sealed class SupportInboxWorkflowService
{
    private readonly List<SupportTicketRow> _tickets =
    [
        new("TCK-1042", "Cannot export monthly invoice pack", "Billing", "High", 2, true, true, false),
        new("TCK-1044", "Need shipping address corrected before dispatch", "Operations", "Medium", 1, false, false, false),
        new("TCK-1048", "Password reset loop for reseller portal", "Identity", "High", 6, true, false, false),
        new("TCK-1051", "Draft reply requested for delayed shipment complaint", "Support", "Medium", 3, false, true, false),
        new("TCK-1055", "Refund request blocked by missing order evidence", "Billing", "High", 5, true, true, true)
    ];

    private readonly HashSet<string> _highlightedTicketIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _escalatedTicketIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _latestDraftBlockers = [];

    public event Action? Changed;

    public int CurrentReviewWindowDays { get; private set; } = 7;
    public string? LatestInsight { get; private set; }
    public SupportReplyDraft? CurrentDraft { get; private set; }
    public bool IsDraftDialogOpen { get; private set; }
    public bool ShowOnlyHighlighted { get; private set; }

    public IReadOnlyList<string> LatestDraftBlockers => _latestDraftBlockers.ToArray();
    public IReadOnlyCollection<string> HighlightedTicketIds => _highlightedTicketIds.ToArray();
    public IReadOnlyCollection<string> EscalatedTicketIds => _escalatedTicketIds.ToArray();

    public IReadOnlyList<SupportTicketRow> VisibleTickets =>
        _tickets
            .Where(ticket => !_highlightedTicketIds.Any() || _highlightedTicketIds.Contains(ticket.Id) || !ShowOnlyHighlighted)
            .ToArray();

    public string FocusOpenTickets(int days)
    {
        CurrentReviewWindowDays = days <= 0 ? 7 : days;
        ShowOnlyHighlighted = true;
        _highlightedTicketIds.Clear();
        _escalatedTicketIds.Clear();
        _latestDraftBlockers.Clear();

        foreach (var ticket in _tickets.Where(ticket => NeedsAttention(ticket, CurrentReviewWindowDays)))
        {
            _highlightedTicketIds.Add(ticket.Id);
        }

        CurrentDraft = null;
        IsDraftDialogOpen = false;
        LatestInsight = BuildInsightSummary(_highlightedTicketIds);
        NotifyChanged();

        return _highlightedTicketIds.Count == 0
            ? $"No tickets in the last {CurrentReviewWindowDays} days still need a reply."
            : $"Highlighted {_highlightedTicketIds.Count} tickets from the last {CurrentReviewWindowDays} days that still need a reply.";
    }

    public bool FocusTicket(string ticketId)
    {
        var ticket = _tickets.FirstOrDefault(ticket => string.Equals(ticket.Id, ticketId, StringComparison.OrdinalIgnoreCase));
        if (ticket is null)
        {
            LatestInsight = $"Ticket {ticketId} was not found in the support queue.";
            NotifyChanged();
            return false;
        }

        ShowOnlyHighlighted = true;
        _highlightedTicketIds.Clear();
        _highlightedTicketIds.Add(ticket.Id);
        _latestDraftBlockers.Clear();
        CurrentDraft = null;
        IsDraftDialogOpen = false;
        LatestInsight = BuildInsightSummary(_highlightedTicketIds);
        NotifyChanged();
        return true;
    }

    public string ExplainFocusedTickets()
    {
        if (_highlightedTicketIds.Count == 0)
        {
            LatestInsight = "No tickets are highlighted yet. Start by showing open tickets that still need a reply.";
            NotifyChanged();
            return LatestInsight;
        }

        LatestInsight = BuildInsightSummary(_highlightedTicketIds);
        NotifyChanged();
        return LatestInsight;
    }

    public string PrepareReplyDraft()
    {
        var targetedTickets = _tickets
            .Where(ticket => _highlightedTicketIds.Contains(ticket.Id))
            .ToArray();

        _latestDraftBlockers.Clear();
        foreach (var blocker in BuildDraftBlockers(targetedTickets, _escalatedTicketIds))
        {
            _latestDraftBlockers.Add(blocker);
        }

        if (_latestDraftBlockers.Count > 0)
        {
            CurrentDraft = null;
            IsDraftDialogOpen = false;
            LatestInsight = $"Reply draft is blocked: {_latestDraftBlockers[0]}";
            NotifyChanged();
            return LatestInsight;
        }

        CurrentDraft = new SupportReplyDraft(
            "Draft customer reply",
            targetedTickets.Select(static ticket => ticket.Id).ToArray(),
            targetedTickets.SelectMany(BuildReplyChecklist).ToArray());
        IsDraftDialogOpen = true;
        LatestInsight = $"Prepared a reply draft for {targetedTickets.Length} highlighted tickets.";
        NotifyChanged();
        return LatestInsight;
    }

    public string ApplyEscalationPlaybook()
    {
        var targetedTickets = _tickets
            .Where(ticket => _highlightedTicketIds.Contains(ticket.Id))
            .ToArray();
        var blockedTickets = targetedTickets
            .Where(ticket => IsBlockedTicket(ticket))
            .ToArray();

        foreach (var ticket in blockedTickets)
        {
            _escalatedTicketIds.Add(ticket.Id);
        }

        _latestDraftBlockers.Clear();
        CurrentDraft = null;
        IsDraftDialogOpen = false;

        LatestInsight = blockedTickets.Length == 0
            ? "No blocked tickets needed escalation."
            : $"Escalated {blockedTickets.Length} blocked tickets so the reply draft can proceed.";
        NotifyChanged();
        return LatestInsight;
    }

    public void SetDraftDialogVisible(bool visible)
    {
        IsDraftDialogOpen = visible;
        NotifyChanged();
    }

    public void Reset()
    {
        CurrentReviewWindowDays = 7;
        LatestInsight = null;
        CurrentDraft = null;
        IsDraftDialogOpen = false;
        ShowOnlyHighlighted = false;
        _highlightedTicketIds.Clear();
        _escalatedTicketIds.Clear();
        _latestDraftBlockers.Clear();
        NotifyChanged();
    }

    public bool IsHighlighted(string ticketId) => _highlightedTicketIds.Contains(ticketId);

    public string DescribeSignals(SupportTicketRow ticket)
    {
        var signals = new List<string>();

        if (ticket.AgeDays <= CurrentReviewWindowDays)
        {
            signals.Add("recent");
        }

        if (ticket.NeedsReply)
        {
            signals.Add("waiting on reply");
        }

        if (ticket.HasEscalationRisk)
        {
            signals.Add("high customer risk");
        }

        if (ticket.MissingEvidence)
        {
            signals.Add("missing evidence");
        }

        return string.Join(" • ", signals);
    }

    private string BuildInsightSummary(IEnumerable<string> ticketIds)
    {
        var focused = _tickets.Where(ticket => ticketIds.Contains(ticket.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (focused.Length == 0)
        {
            return "No open ticket cluster is active yet.";
        }

        var oldestAge = focused.Max(static ticket => ticket.AgeDays);
        var highRiskCount = focused.Count(static ticket => ticket.HasEscalationRisk);
        var evidenceBlockers = focused.Count(static ticket => ticket.MissingEvidence);

        return $"The current queue has {focused.Length} tickets needing attention. {highRiskCount} have escalation risk, {evidenceBlockers} are blocked by missing evidence, and the oldest highlighted ticket is {oldestAge} days old.";
    }

    private static IEnumerable<string> BuildDraftBlockers(
        IEnumerable<SupportTicketRow> tickets,
        IReadOnlySet<string> escalatedTicketIds)
    {
        foreach (var ticket in tickets.Where(ticket => ticket.MissingEvidence && !escalatedTicketIds.Contains(ticket.Id)))
        {
            yield return $"{ticket.Id} is missing order evidence, so the reply needs escalation first.";
        }
    }

    private static IEnumerable<string> BuildReplyChecklist(SupportTicketRow ticket)
    {
        yield return $"Summarize the current issue for {ticket.Id}.";
        yield return $"Confirm the next owner for the {ticket.Team} follow-up.";
        yield return $"Draft a customer-safe reply for the {ticket.Priority.ToLowerInvariant()} priority case.";
    }

    private static bool NeedsAttention(SupportTicketRow ticket, int days)
        => ticket.NeedsReply && ticket.AgeDays <= days;

    private bool IsBlockedTicket(SupportTicketRow ticket) => ticket.MissingEvidence;

    private void NotifyChanged() => Changed?.Invoke();
}

internal sealed record SupportTicketRow(
    string Id,
    string Subject,
    string Team,
    string Priority,
    int AgeDays,
    bool NeedsReply,
    bool HasEscalationRisk,
    bool MissingEvidence);

internal sealed record SupportReplyDraft(
    string Title,
    IReadOnlyList<string> TicketIds,
    IReadOnlyList<string> Checklist);
