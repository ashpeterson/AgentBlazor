using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Demo.Services;

[AgentCapability(
    "incident_escalation",
    Name = "Incident Escalation Workflow",
    Description = "Coordinate incident triage, evidence review, and escalation brief preparation across the live workflow surface.",
    Category = "Workflow")]
internal sealed class IncidentEscalationCapabilities(IncidentEscalationWorkflowService workflow)
{
    [AgentAction("Summarize the current incident triage workflow", ActionId = "summarize_incident_triage")]
    public Task<CapabilityResult> SummarizeIncidentTriageAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.SummarizeIncidentTriageAsync(sessionId, cancellationToken);

    [AgentAction("Focus the workflow on evidence review for the current incident", ActionId = "focus_evidence_review")]
    public Task<CapabilityResult> FocusEvidenceReviewAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.FocusEvidenceReviewAsync(sessionId, cancellationToken);

    [AgentAction("Assign the default triage owner for the current incident", ActionId = "assign_triage_owner")]
    public Task<CapabilityResult> AssignTriageOwnerAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.AssignTriageOwnerAsync(sessionId, cancellationToken);

    [AgentAction("Mark the current evidence review as complete", ActionId = "complete_evidence_review")]
    public Task<CapabilityResult> CompleteEvidenceReviewAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.CompleteEvidenceReviewAsync(sessionId, cancellationToken);

    [AgentAction("Prepare an escalation brief for the current incident", ActionId = "prepare_escalation_brief", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareEscalationBriefAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.PrepareEscalationBriefAsync(sessionId, cancellationToken);

    [AgentAction("Submit the current escalation brief to the review board", ActionId = "submit_escalation_handoff", RequiresApproval = true)]
    public Task<CapabilityResult> SubmitEscalationHandoffAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.SubmitEscalationHandoffAsync(sessionId, cancellationToken);

    [AgentAction("Apply the escalation recovery playbook for the current incident", ActionId = "apply_recovery_playbook")]
    public Task<CapabilityResult> ApplyRecoveryPlaybookAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.ApplyRecoveryPlaybookAsync(sessionId, cancellationToken);

    [AgentAction("Reset the incident escalation workflow", ActionId = "reset_incident_workflow")]
    public Task<CapabilityResult> ResetIncidentWorkflowAsync()
    {
        workflow.Reset();
        return Task.FromResult(CapabilityResult.Success("Reset the incident escalation workflow."));
    }
}

internal sealed class IncidentEscalationWorkflowService
{
    private static readonly IReadOnlyList<string> DefaultCommands =
    [
        "assign_triage_owner",
        "complete_evidence_review",
        "apply_recovery_playbook",
        "open_escalation_brief",
        "submit_escalation_handoff",
        "reset_incident_workflow"
    ];

    public event Action? Changed;

    public string? SessionId { get; private set; }

    public IncidentEscalationSnapshot Snapshot { get; private set; } = CreateDefaultSnapshot();

    public CapabilityResult? LatestResult { get; private set; }

    public string? LatestNarrative { get; private set; }

    public IncidentEscalationBrief? CurrentBrief { get; private set; }

    public bool IsEscalationDialogOpen { get; private set; }

    public Task LoadAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);
        return Task.CompletedTask;
    }

    public Task<CapabilityResult> SummarizeIncidentTriageAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        LatestNarrative = BuildSummary();
        var result = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions(includeEscalationAction: true, includeSubmissionAction: CurrentBrief is not null)
        };

        return CompleteResultAsync(result);
    }

    public Task<CapabilityResult> FocusEvidenceReviewAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        Snapshot = Snapshot with
        {
            SelectedNodeId = "evidence",
            ExpandedNodeIds = ["overview", "evidence", "timeline"],
            ActiveTabIndex = 1,
            CurrentStepIndex = 1,
            LastCommand = "request_evidence_refresh",
            TimelineEntries =
            [
                .. Snapshot.TimelineEntries,
                new IncidentTimelineEntry(DateTimeOffset.UtcNow, "Evidence review focused for triage")
            ]
        };

        LatestNarrative = Snapshot.Incident.MissingEvidenceCount > 0
            ? $"Focused the workflow on evidence review. {Snapshot.Incident.MissingEvidenceCount} evidence item(s) still block escalation."
            : "Focused the workflow on evidence review. The case is ready to move into escalation.";

        var result = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions(includeEscalationAction: true, includeSubmissionAction: CurrentBrief is not null)
        };

        return CompleteResultAsync(result);
    }

    public Task<CapabilityResult> AssignTriageOwnerAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        Snapshot.Incident.AssignedOwner = "Morgan Lee";
        Snapshot = Snapshot with
        {
            LastCommand = "assign_triage_owner",
            TimelineEntries =
            [
                .. Snapshot.TimelineEntries,
                new IncidentTimelineEntry(DateTimeOffset.UtcNow, "Assigned Morgan Lee as triage owner")
            ]
        };

        LatestNarrative = "Assigned Morgan Lee as triage owner so the workflow can continue toward evidence review and escalation.";
        var result = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions(includeEscalationAction: true, includeSubmissionAction: CurrentBrief is not null)
        };

        return CompleteResultAsync(result);
    }

    public Task<CapabilityResult> CompleteEvidenceReviewAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        Snapshot.Incident.MissingEvidenceCount = 0;
        Snapshot.Incident.EvidenceStatus = "Complete";
        Snapshot = Snapshot with
        {
            SelectedNodeId = "timeline",
            ExpandedNodeIds = ["overview", "evidence", "timeline", "escalation"],
            ActiveTabIndex = 2,
            CurrentStepIndex = 1,
            LastCommand = "complete_evidence_review",
            TimelineEntries =
            [
                .. Snapshot.TimelineEntries,
                new IncidentTimelineEntry(DateTimeOffset.UtcNow, "Evidence review completed and timeline updated")
            ]
        };

        LatestNarrative = "Completed evidence review. The incident can now move into escalation if the owner confirms the brief.";
        var result = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions(includeEscalationAction: true, includeSubmissionAction: CurrentBrief is not null)
        };

        return CompleteResultAsync(result);
    }

    public Task<CapabilityResult> PrepareEscalationBriefAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        var blockers = BuildBlockers();
        if (blockers.Count > 0)
        {
            LatestNarrative = $"Escalation is blocked for {Snapshot.Incident.IncidentId}: {blockers[0]}";
            var blockedResult = CapabilityResult.Blocked(LatestNarrative) with
            {
                Outputs = BuildOutputs(blockers),
                Warnings = BuildWarnings(),
                NextActions = BuildNextActions(includeEscalationAction: false, includeSubmissionAction: false)
            };

            return CompleteResultAsync(blockedResult);
        }

        CurrentBrief = new IncidentEscalationBrief(
            $"{Snapshot.Incident.IncidentId} escalation brief",
            $"Escalate {Snapshot.Incident.Title} to the critical-incident review board.",
            [
                $"Confirm owner {Snapshot.Incident.AssignedOwner} remains primary contact.",
                "Attach the completed evidence packet and timeline summary.",
                "Capture communications lead acknowledgement before board handoff.",
                "Notify the policy escalation board and incident communications lead.",
                "Schedule the executive review checkpoint within 30 minutes."
            ],
            [
                "critical-incident-board",
                "policy-escalation-lead",
                "incident-comms"
            ]);

        Snapshot.Incident.EscalationStatus = "Brief prepared";
        Snapshot.Incident.CommunicationsLeadConfirmed = false;
        Snapshot.Incident.HandoffBlockedReason = null;
        IsEscalationDialogOpen = true;
        Snapshot = Snapshot with
        {
            SelectedNodeId = "escalation",
            ExpandedNodeIds = ["overview", "evidence", "timeline", "escalation"],
            ActiveTabIndex = 3,
            CurrentStepIndex = 2,
            LastCommand = "open_escalation_brief",
            TimelineEntries =
            [
                .. Snapshot.TimelineEntries,
                new IncidentTimelineEntry(DateTimeOffset.UtcNow, $"Prepared escalation brief for {Snapshot.Incident.IncidentId}")
            ]
        };

        LatestNarrative = $"Prepared an escalation brief for {Snapshot.Incident.IncidentId} and moved the workflow into the escalation phase.";
        var successResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = new Dictionary<string, object?>(BuildOutputs(), StringComparer.OrdinalIgnoreCase)
            {
                ["briefTitle"] = CurrentBrief.Title,
                ["recipientCount"] = CurrentBrief.TargetRecipients.Count,
                ["checklistCount"] = CurrentBrief.ChecklistItems.Count,
                ["communicationsLeadConfirmed"] = Snapshot.Incident.CommunicationsLeadConfirmed
            },
            Warnings = BuildWarnings(),
            NextActions =
            [
                "Review the escalation brief in the dialog.",
                "Run the recovery playbook to capture communications acknowledgement.",
                "Approve and submit the escalation handoff to the review board."
            ]
        };

        return CompleteResultAsync(successResult);
    }

    public Task<CapabilityResult> SubmitEscalationHandoffAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        if (CurrentBrief is null)
        {
            const string message = "Escalation handoff is blocked because no escalation brief has been prepared yet.";
            LatestNarrative = message;
            return CompleteResultAsync(CapabilityResult.Blocked(message) with
            {
                Outputs = BuildOutputs(["No escalation brief has been prepared yet."]),
                Warnings = BuildWarnings(),
                NextActions =
                [
                    "Prepare the escalation brief before submitting the handoff."
                ]
            });
        }

        if (!Snapshot.Incident.CommunicationsLeadConfirmed)
        {
            const string blocker = "The incident communications lead has not acknowledged the handoff.";
            Snapshot.Incident.EscalationStatus = "Recovery required";
            Snapshot.Incident.HandoffBlockedReason = blocker;
            Snapshot = Snapshot with
            {
                SelectedNodeId = "escalation",
                ExpandedNodeIds = ["overview", "evidence", "timeline", "escalation"],
                ActiveTabIndex = 3,
                CurrentStepIndex = 2,
                LastCommand = "submit_escalation_handoff",
                TimelineEntries =
                [
                    .. Snapshot.TimelineEntries,
                    new IncidentTimelineEntry(DateTimeOffset.UtcNow, "Escalation handoff blocked pending communications acknowledgement")
                ]
            };

            LatestNarrative = $"Escalation handoff is blocked for {Snapshot.Incident.IncidentId}: {blocker}";
            return CompleteResultAsync(CapabilityResult.Blocked(LatestNarrative) with
            {
                Outputs = BuildOutputs([blocker]),
                Warnings = BuildWarnings(),
                NextActions =
                [
                    "Apply the recovery playbook to capture communications acknowledgement.",
                    "Review the updated brief, then retry the escalation handoff."
                ]
            });
        }

        Snapshot.Incident.EscalationStatus = "Submitted";
        Snapshot.Incident.HandoffBlockedReason = null;
        IsEscalationDialogOpen = false;
        Snapshot = Snapshot with
        {
            SelectedNodeId = "escalation",
            ExpandedNodeIds = ["overview", "evidence", "timeline", "escalation"],
            ActiveTabIndex = 3,
            CurrentStepIndex = 2,
            LastCommand = "submit_escalation_handoff",
            TimelineEntries =
            [
                .. Snapshot.TimelineEntries,
                new IncidentTimelineEntry(DateTimeOffset.UtcNow, $"Submitted escalation handoff for {Snapshot.Incident.IncidentId} to the review board")
            ]
        };

        LatestNarrative = $"Submitted the escalation handoff for {Snapshot.Incident.IncidentId}. The review board can now pick up the prepared brief.";
        return CompleteResultAsync(CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions =
            [
                "Monitor the board review and executive checkpoint timing.",
                "Keep the incident communications lead aligned on updates."
            ]
        });
    }

    public Task<CapabilityResult> ApplyRecoveryPlaybookAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        EnsureSession(sessionId);

        Snapshot.Incident.CommunicationsLeadConfirmed = true;
        Snapshot.Incident.EscalationStatus = "Ready to submit";
        Snapshot.Incident.HandoffBlockedReason = null;
        Snapshot = Snapshot with
        {
            SelectedNodeId = "timeline",
            ExpandedNodeIds = ["overview", "evidence", "timeline", "escalation"],
            ActiveTabIndex = 2,
            CurrentStepIndex = 2,
            LastCommand = "apply_recovery_playbook",
            TimelineEntries =
            [
                .. Snapshot.TimelineEntries,
                new IncidentTimelineEntry(DateTimeOffset.UtcNow, "Recovery playbook applied and communications acknowledgement captured")
            ]
        };

        LatestNarrative = "Applied the recovery playbook, captured communications acknowledgement, and returned the incident to a handoff-ready state.";
        return CompleteResultAsync(CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions =
            [
                "Submit the escalation handoff to the review board.",
                "Keep the escalation brief open for the final review if needed."
            ]
        });
    }

    public Task ExecuteCommandAsync(string? sessionId, string command)
    {
        EnsureSession(sessionId);

        return command switch
        {
            "assign_triage_owner" => AssignTriageOwnerAsync(sessionId),
            "complete_evidence_review" => CompleteEvidenceReviewAsync(sessionId),
            "apply_recovery_playbook" => ApplyRecoveryPlaybookAsync(sessionId),
            "open_escalation_brief" => OpenCurrentEscalationBriefAsync(sessionId),
            "submit_escalation_handoff" => SubmitEscalationHandoffAsync(sessionId),
            "reset_incident_workflow" => ResetAsync(),
            _ => Task.CompletedTask
        };
    }

    public Task SetSelectedNodeAsync(string? sessionId, string? selectedNodeId)
    {
        EnsureSession(sessionId);
        Snapshot = Snapshot with
        {
            SelectedNodeId = string.IsNullOrWhiteSpace(selectedNodeId) ? Snapshot.SelectedNodeId : selectedNodeId
        };
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SetExpandedNodesAsync(string? sessionId, IReadOnlyCollection<string> expandedNodeIds)
    {
        EnsureSession(sessionId);
        Snapshot = Snapshot with
        {
            ExpandedNodeIds = expandedNodeIds.Count == 0 ? Snapshot.ExpandedNodeIds : expandedNodeIds.ToArray()
        };
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SetActiveTabIndexAsync(string? sessionId, int index)
    {
        EnsureSession(sessionId);
        Snapshot = Snapshot with
        {
            ActiveTabIndex = Math.Clamp(index, 0, 3)
        };
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SetCurrentStepIndexAsync(string? sessionId, int index)
    {
        EnsureSession(sessionId);
        Snapshot = Snapshot with
        {
            CurrentStepIndex = Math.Clamp(index, 0, 2)
        };
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task SetEscalationDialogVisibleAsync(string? sessionId, bool visible)
    {
        EnsureSession(sessionId);
        IsEscalationDialogOpen = visible;
        NotifyChanged();
        return Task.CompletedTask;
    }

    public void Reset()
    {
        Snapshot = CreateDefaultSnapshot();
        LatestResult = null;
        LatestNarrative = null;
        CurrentBrief = null;
        IsEscalationDialogOpen = false;
        NotifyChanged();
    }

    private Task OpenCurrentEscalationBriefAsync(string? sessionId)
    {
        EnsureSession(sessionId);

        if (CurrentBrief is not null)
        {
            IsEscalationDialogOpen = true;
            Snapshot = Snapshot with
            {
                SelectedNodeId = "escalation",
                ActiveTabIndex = 3,
                CurrentStepIndex = 2,
                LastCommand = "open_escalation_brief"
            };
            NotifyChanged();
        }

        return Task.CompletedTask;
    }

    private Task ResetAsync()
    {
        Reset();
        return Task.CompletedTask;
    }

    private Task<CapabilityResult> CompleteResultAsync(CapabilityResult result)
    {
        LatestResult = result;
        NotifyChanged();
        return Task.FromResult(result);
    }

    private void EnsureSession(string? sessionId)
    {
        if (string.Equals(SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SessionId = sessionId;
        Snapshot = CreateDefaultSnapshot();
        LatestResult = null;
        LatestNarrative = null;
        CurrentBrief = null;
        IsEscalationDialogOpen = false;
    }

    private string BuildSummary()
    {
        var blockers = BuildBlockers();
        if (blockers.Count > 0)
        {
            return $"{Snapshot.Incident.IncidentId} is still in triage because {blockers[0].ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(Snapshot.Incident.HandoffBlockedReason))
        {
            return $"{Snapshot.Incident.IncidentId} is in recovery because {Snapshot.Incident.HandoffBlockedReason!.ToLowerInvariant()}";
        }

        if (Snapshot.Incident.EscalationStatus == "Submitted")
        {
            return $"{Snapshot.Incident.IncidentId} has been handed off to the review board and is awaiting board follow-up.";
        }

        return $"{Snapshot.Incident.IncidentId} is ready for escalation with {Snapshot.Incident.WatchlistReasons.Count} watchlist signal(s) and a complete evidence packet.";
    }

    private IReadOnlyList<string> BuildBlockers()
    {
        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(Snapshot.Incident.AssignedOwner))
        {
            blockers.Add("No triage owner has been assigned.");
        }

        if (Snapshot.Incident.MissingEvidenceCount > 0)
        {
            blockers.Add($"{Snapshot.Incident.MissingEvidenceCount} evidence item(s) are still missing.");
        }

        return blockers;
    }

    private IReadOnlyList<string> BuildWarnings()
    {
        var warnings = new List<string>();

        if (Snapshot.Incident.PolicyEscalationRequired)
        {
            warnings.Add("Policy escalation is required because the incident touches a regulated checkout flow.");
        }

        foreach (var reason in Snapshot.Incident.WatchlistReasons)
        {
            warnings.Add(reason);
        }

        return warnings;
    }

    private IReadOnlyList<string> BuildNextActions(bool includeEscalationAction, bool includeSubmissionAction)
    {
        var actions = new List<string>();

        if (string.IsNullOrWhiteSpace(Snapshot.Incident.AssignedOwner))
        {
            actions.Add("Assign the triage owner for the incident.");
        }

        if (Snapshot.Incident.MissingEvidenceCount > 0)
        {
            actions.Add("Complete the evidence review and close the missing evidence gap.");
        }

        if (includeEscalationAction)
        {
            actions.Add("Prepare the escalation brief once the blockers are resolved.");
        }

        if (!string.IsNullOrWhiteSpace(Snapshot.Incident.HandoffBlockedReason))
        {
            actions.Add("Apply the recovery playbook to resolve the blocked handoff.");
        }

        if (includeSubmissionAction && CurrentBrief is not null)
        {
            actions.Add(Snapshot.Incident.CommunicationsLeadConfirmed
                ? "Submit the escalation handoff to the review board."
                : "Capture communications acknowledgement before submitting the escalation handoff.");
        }

        if (actions.Count == 0)
        {
            actions.Add("Review the escalation brief and confirm the handoff.");
        }

        return actions;
    }

    private IReadOnlyDictionary<string, object?> BuildOutputs(IReadOnlyList<string>? blockers = null)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["incidentId"] = Snapshot.Incident.IncidentId,
            ["severity"] = Snapshot.Incident.Severity,
            ["region"] = Snapshot.Incident.Region,
            ["assignedOwner"] = Snapshot.Incident.AssignedOwner,
            ["missingEvidenceCount"] = Snapshot.Incident.MissingEvidenceCount,
            ["evidenceStatus"] = Snapshot.Incident.EvidenceStatus,
            ["escalationStatus"] = Snapshot.Incident.EscalationStatus,
            ["communicationsLeadConfirmed"] = Snapshot.Incident.CommunicationsLeadConfirmed,
            ["handoffBlockedReason"] = Snapshot.Incident.HandoffBlockedReason,
            ["selectedNodeId"] = Snapshot.SelectedNodeId,
            ["activeTabIndex"] = Snapshot.ActiveTabIndex,
            ["currentStepIndex"] = Snapshot.CurrentStepIndex,
            ["lastCommand"] = Snapshot.LastCommand,
            ["blockers"] = (blockers ?? BuildBlockers()).ToArray()
        };
    }

    private void NotifyChanged() => Changed?.Invoke();

    private static IncidentEscalationSnapshot CreateDefaultSnapshot()
    {
        return new IncidentEscalationSnapshot(
            new IncidentEscalationCase(
                "INC-4421",
                "Checkpoint API drift in EU checkout flow",
                "Critical",
                "EU-West",
                "Ava Patel",
                null,
                2,
                "Pending",
                true,
                "Initial triage",
                false,
                null,
                [
                    "Checkout API schema changed without sign-off.",
                    "Fraud-monitoring rules may be using stale payloads."
                ]),
            "overview",
            ["overview", "evidence"],
            0,
            0,
            null,
            DefaultCommands,
            [
                new IncidentTimelineEntry(DateTimeOffset.UtcNow.AddMinutes(-42), "Incident opened by Ava Patel"),
                new IncidentTimelineEntry(DateTimeOffset.UtcNow.AddMinutes(-27), "Policy review flagged a regulated checkout dependency"),
                new IncidentTimelineEntry(DateTimeOffset.UtcNow.AddMinutes(-9), "Evidence packet marked incomplete after API schema drift review")
            ]);
    }
}

internal sealed class IncidentEscalationCase(
    string incidentId,
    string title,
    string severity,
    string region,
    string reporter,
    string? assignedOwner,
    int missingEvidenceCount,
    string evidenceStatus,
    bool policyEscalationRequired,
    string escalationStatus,
    bool communicationsLeadConfirmed,
    string? handoffBlockedReason,
    IReadOnlyList<string> watchlistReasons)
{
    public string IncidentId { get; } = incidentId;
    public string Title { get; } = title;
    public string Severity { get; } = severity;
    public string Region { get; } = region;
    public string Reporter { get; } = reporter;
    public string? AssignedOwner { get; set; } = assignedOwner;
    public int MissingEvidenceCount { get; set; } = missingEvidenceCount;
    public string EvidenceStatus { get; set; } = evidenceStatus;
    public bool PolicyEscalationRequired { get; } = policyEscalationRequired;
    public string EscalationStatus { get; set; } = escalationStatus;
    public bool CommunicationsLeadConfirmed { get; set; } = communicationsLeadConfirmed;
    public string? HandoffBlockedReason { get; set; } = handoffBlockedReason;
    public IReadOnlyList<string> WatchlistReasons { get; } = watchlistReasons;
}

internal sealed record IncidentEscalationSnapshot(
    IncidentEscalationCase Incident,
    string SelectedNodeId,
    IReadOnlyList<string> ExpandedNodeIds,
    int ActiveTabIndex,
    int CurrentStepIndex,
    string? LastCommand,
    IReadOnlyList<string> Commands,
    IReadOnlyList<IncidentTimelineEntry> TimelineEntries);

internal sealed record IncidentTimelineEntry(
    DateTimeOffset TimestampUtc,
    string Message);

internal sealed record IncidentEscalationBrief(
    string Title,
    string Summary,
    IReadOnlyList<string> ChecklistItems,
    IReadOnlyList<string> TargetRecipients);
