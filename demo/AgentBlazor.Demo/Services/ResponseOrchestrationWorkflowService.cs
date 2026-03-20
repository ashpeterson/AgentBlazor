using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Demo.Services;

[AgentCapability(
    "response_orchestration",
    Name = "Response Orchestration Workflow",
    Description = "Coordinate supplier risk, audit evidence, and incident escalation into one approval-gated response packet.",
    Category = "Workflow")]
internal sealed class ResponseOrchestrationCapabilities(ResponseOrchestrationWorkflowService workflow)
{
    [AgentAction("Assess cross-system response readiness for the current operational scenario", ActionId = "assess_response_readiness")]
    public Task<CapabilityResult> AssessResponseReadinessAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.AssessResponseReadinessAsync(sessionId, cancellationToken);

    [AgentAction("Prepare the cross-system response packet for the current operational scenario", ActionId = "prepare_response_packet", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareResponsePacketAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.PrepareResponsePacketAsync(sessionId, cancellationToken);

    [AgentAction("Advance the next guided subsystem stage for the current operational scenario", ActionId = "advance_response_stage")]
    public Task<CapabilityResult> AdvanceNextStageAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.AdvanceNextStageAsync(sessionId, cancellationToken);

    [AgentAction("Apply the cross-system recovery playbook for the current operational scenario", ActionId = "apply_response_recovery_playbook")]
    public Task<CapabilityResult> ApplyRecoveryPlaybookAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
        => workflow.ApplyRecoveryPlaybookAsync(sessionId, cancellationToken);

    [AgentAction("Reset the cross-system response workflow", ActionId = "reset_response_workflow")]
    public Task<CapabilityResult> ResetResponseWorkflowAsync()
    {
        workflow.Reset();
        return Task.FromResult(CapabilityResult.Success("Reset the cross-system response workflow."));
    }
}

internal sealed class ResponseOrchestrationWorkflowService(
    SupplierComplianceWorkflowService supplierWorkflow,
    DemoFileWorkflowService fileWorkflow,
    IncidentEscalationWorkflowService incidentWorkflow)
{
    private const string OrchestrationRoute = "/demo/workflows/response-orchestration";
    private const string ResponseSource = "response-orchestration";

    public event Action? Changed;

    public string? SessionId { get; private set; }

    public ResponseOrchestrationSnapshot Snapshot { get; private set; } = ResponseOrchestrationSnapshot.Empty;

    public CapabilityResult? LatestResult { get; private set; }

    public string? LatestNarrative { get; private set; }

    public ResponsePacketDraft? CurrentPacket { get; private set; }

    public bool IsPacketDialogOpen { get; private set; }

    public IReadOnlyList<string> LatestBlockers { get; private set; } = [];

    public IReadOnlyList<ResponseOrchestrationJourneyEvent> JourneyEvents => _journeyEvents.ToArray();

    private readonly List<ResponseOrchestrationJourneyEvent> _journeyEvents = [];

    public string GetSupplierWorkflowRoute() => BuildWorkflowRoute("/demo/workflows/supplier-compliance", GetSupplierRouteFocus());

    public string GetFileWorkflowRoute() => BuildWorkflowRoute("/demo/workflows/file-audit-bundle", GetFileRouteFocus());

    public string GetIncidentWorkflowRoute() => BuildWorkflowRoute("/demo/workflows/incident-escalation", GetIncidentRouteFocus());

    public string? GetNextGuidedWorkflowRoute(string? returnedFrom = null)
    {
        var returned = returnedFrom?.Trim().ToLowerInvariant();

        if (NeedsSupplierSurface() && returned is not "supplier")
        {
            return GetSupplierWorkflowRoute();
        }

        if (NeedsFileSurface() && returned is not "file")
        {
            return GetFileWorkflowRoute();
        }

        if (NeedsIncidentSurface() && returned is not "incident")
        {
            return GetIncidentWorkflowRoute();
        }

        if (NeedsSupplierSurface())
        {
            return GetSupplierWorkflowRoute();
        }

        if (NeedsFileSurface())
        {
            return GetFileWorkflowRoute();
        }

        if (NeedsIncidentSurface())
        {
            return GetIncidentWorkflowRoute();
        }

        return null;
    }

    public string GetGuidedJourneySummary(string? returnedFrom = null, string? returnState = null)
    {
        if (!string.IsNullOrWhiteSpace(returnedFrom))
        {
            var surface = returnedFrom switch
            {
                "supplier" => "supplier compliance",
                "file" => "file audit",
                "incident" => "incident escalation",
                _ => returnedFrom
            };

            if (!string.IsNullOrWhiteSpace(returnState))
            {
                return $"Returned from the {surface} surface with state '{returnState}'.";
            }

            return $"Returned from the {surface} surface.";
        }

        if (LatestBlockers.Count > 0)
        {
            return $"The orchestration flow is still blocked: {LatestBlockers[0]}";
        }

        return CurrentPacket is not null
            ? "All subsystem surfaces are aligned and the response packet is ready."
            : "Use the guided journey to move through the live subsystem surfaces in order.";
    }

    public IReadOnlyList<ResponseOrchestrationJourneyStep> GetJourneySteps(string? returnedFrom = null)
    {
        var nextRoute = GetNextGuidedWorkflowRoute(returnedFrom);

        return
        [
            new ResponseOrchestrationJourneyStep(
                "supplier",
                "Supplier compliance",
                Snapshot.SupplierPhase,
                DescribeSupplierStatus(),
                GetSupplierWorkflowRoute(),
                GetJourneyStepStatus(
                    isComplete: !NeedsSupplierSurface(),
                    isBlocked: supplierWorkflow.LatestDraftBlockers.Count > 0,
                    isCurrent: string.Equals(nextRoute, GetSupplierWorkflowRoute(), StringComparison.OrdinalIgnoreCase))),
            new ResponseOrchestrationJourneyStep(
                "file",
                "Audit evidence",
                Snapshot.FileBundlePhase,
                DescribeFileStatus(),
                GetFileWorkflowRoute(),
                GetJourneyStepStatus(
                    isComplete: !NeedsFileSurface(),
                    isBlocked: LatestBlockers.Any(static blocker => blocker.Contains("Audit evidence", StringComparison.OrdinalIgnoreCase)),
                    isCurrent: string.Equals(nextRoute, GetFileWorkflowRoute(), StringComparison.OrdinalIgnoreCase))),
            new ResponseOrchestrationJourneyStep(
                "incident",
                "Incident escalation",
                Snapshot.IncidentPhase,
                DescribeIncidentStatus(),
                GetIncidentWorkflowRoute(),
                GetJourneyStepStatus(
                    isComplete: !NeedsIncidentSurface(),
                    isBlocked: LatestBlockers.Any(static blocker => blocker.Contains("Incident", StringComparison.OrdinalIgnoreCase)),
                    isCurrent: string.Equals(nextRoute, GetIncidentWorkflowRoute(), StringComparison.OrdinalIgnoreCase))),
            new ResponseOrchestrationJourneyStep(
                "packet",
                "Response packet",
                CurrentPacket is not null ? "Ready for review" : LatestBlockers.Count == 0 && nextRoute is null ? "Ready to prepare" : "Waiting on subsystem alignment",
                DescribePacketStatus(),
                null,
                GetJourneyStepStatus(
                    isComplete: CurrentPacket is not null,
                    isBlocked: LatestBlockers.Count > 0,
                    isCurrent: CurrentPacket is null && nextRoute is null))
        ];
    }

    public async Task LoadAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await EnsureSessionAsync(cancellationToken);
        await RefreshSnapshotAsync(cancellationToken);
    }

    public async Task<CapabilityResult> AssessResponseReadinessAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await EnsureSessionAsync(cancellationToken);

        supplierWorkflow.FocusAtRiskSuppliers(30);
        supplierWorkflow.ExplainFocusedSuppliers();
        _ = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Local", cancellationToken);
        _ = await incidentWorkflow.SummarizeIncidentTriageAsync(SessionId, cancellationToken);

        await RefreshSnapshotAsync(cancellationToken);

        LatestNarrative = BuildReadinessSummary();
        LatestBlockers = BuildBlockers();
        AddJourneyEvent("assessment", "Readiness assessed", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> PrepareResponsePacketAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await EnsureSessionAsync(cancellationToken);

        if (supplierWorkflow.HighlightedSupplierIds.Count == 0)
        {
            supplierWorkflow.FocusAtRiskSuppliers(30);
        }

        supplierWorkflow.ExplainFocusedSuppliers();
        _ = supplierWorkflow.PrepareRemediationDraft();

        var currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
        _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
        _ = await fileWorkflow.RunRemoteHandoffAsync(SessionId ?? string.Empty, cancellationToken);
        _ = await fileWorkflow.ValidateRemoteTokensAsync(SessionId ?? string.Empty, cancellationToken);

        _ = await incidentWorkflow.PrepareEscalationBriefAsync(SessionId, cancellationToken);

        await RefreshSnapshotAsync(cancellationToken);

        LatestBlockers = BuildBlockers();
        if (LatestBlockers.Count > 0)
        {
            CurrentPacket = null;
            IsPacketDialogOpen = false;
            LatestNarrative = $"Cross-system response packet is blocked: {LatestBlockers[0]}";
            AddJourneyEvent("packet", "Packet blocked", LatestNarrative);
            LatestResult = CapabilityResult.Blocked(LatestNarrative) with
            {
                Outputs = BuildOutputs(),
                Warnings = BuildWarnings(),
                NextActions = BuildNextActions()
            };

            NotifyChanged();
            return LatestResult;
        }

        CurrentPacket = new ResponsePacketDraft(
            "Cross-system response packet",
            supplierWorkflow.HighlightedSupplierIds.ToArray(),
            Snapshot.FileSnapshot.Files,
            Snapshot.IncidentSnapshot.Incident.IncidentId,
            [
                $"Supplier remediation draft prepared for {supplierWorkflow.CurrentDraft?.SupplierIds.Count ?? 0} supplier(s).",
                $"Audit evidence bundle is in {Snapshot.FileBundlePhase} phase with {Snapshot.VerifiedFileCount} verified file token(s).",
                $"Incident escalation brief '{incidentWorkflow.CurrentBrief?.Title}' is ready for review."
            ],
            [
                "Review the supplier remediation draft.",
                "Attach the verified audit evidence bundle.",
                "Review the escalation brief before board handoff."
            ]);

        IsPacketDialogOpen = true;
        LatestNarrative = $"Prepared the cross-system response packet for {CurrentPacket.SupplierIds.Count} suppliers and incident {CurrentPacket.IncidentId}.";
        AddJourneyEvent("packet", "Packet prepared", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions =
            [
                "Review the response packet in the dialog.",
                "Approve the cross-system response packet for handoff."
            ]
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> ApplyRecoveryPlaybookAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await EnsureSessionAsync(cancellationToken);

        supplierWorkflow.ApplyRecoveryPlaybook();
        var currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
        _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
        _ = await fileWorkflow.ApplyRecoveryPlaybookAsync(SessionId ?? string.Empty, cancellationToken);
        _ = await incidentWorkflow.AssignTriageOwnerAsync(SessionId, cancellationToken);
        _ = await incidentWorkflow.CompleteEvidenceReviewAsync(SessionId, cancellationToken);

        await RefreshSnapshotAsync(cancellationToken);

        LatestBlockers = BuildBlockers();
        CurrentPacket = null;
        IsPacketDialogOpen = false;
        LatestNarrative = LatestBlockers.Count == 0
            ? "Applied the cross-system recovery playbook. The response packet can now be prepared."
            : $"Applied the cross-system recovery playbook, but blockers remain: {LatestBlockers[0]}";
        AddJourneyEvent("recovery", "Recovery playbook applied", LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> ProcessGuidedReturnAsync(
        string? sessionId,
        string? returnedFrom,
        string? returnState,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await EnsureSessionAsync(cancellationToken);
        await RefreshSnapshotAsync(cancellationToken);

        LatestBlockers = BuildBlockers();
        LatestNarrative = BuildReturnNarrative(returnedFrom, returnState);
        AddJourneyEvent(
            returnedFrom?.Trim().ToLowerInvariant() switch
            {
                "supplier" => "supplier",
                "file" => "file",
                "incident" => "incident",
                _ => "return"
            },
            $"Returned from {(string.IsNullOrWhiteSpace(returnedFrom) ? "workflow" : returnedFrom)}",
            LatestNarrative);
        LatestResult = CapabilityResult.Success(LatestNarrative) with
        {
            Outputs = BuildOutputs(),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public async Task<CapabilityResult> AdvanceNextStageAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        await EnsureSessionAsync(cancellationToken);
        await RefreshSnapshotAsync(cancellationToken);

        var nextStage = GetNextGuidedStageKey();
        if (nextStage is null)
        {
            LatestBlockers = BuildBlockers();
            LatestNarrative = LatestBlockers.Count == 0
                ? "All subsystem stages are aligned. The next step is to prepare the approval-gated response packet."
                : $"Subsystem blockers still remain before packet preparation: {LatestBlockers[0]}";
            AddJourneyEvent("packet", "Subsystem alignment complete", LatestNarrative);
            LatestResult = CapabilityResult.Success(LatestNarrative) with
            {
                Outputs = BuildOutputsWithStage("packet"),
                Warnings = BuildWarnings(),
                NextActions = BuildNextActions()
            };

            NotifyChanged();
            return LatestResult;
        }

        switch (nextStage)
        {
            case "supplier":
                supplierWorkflow.FocusAtRiskSuppliers(30);
                supplierWorkflow.ExplainFocusedSuppliers();
                _ = supplierWorkflow.PrepareRemediationDraft();
                if (supplierWorkflow.LatestDraftBlockers.Count > 0)
                {
                    supplierWorkflow.ApplyRecoveryPlaybook();
                    _ = supplierWorkflow.PrepareRemediationDraft();
                }
                break;
            case "file":
            {
                var currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
                _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
                _ = await fileWorkflow.RunRemoteHandoffAsync(SessionId ?? string.Empty, cancellationToken);
                _ = await fileWorkflow.ValidateRemoteTokensAsync(SessionId ?? string.Empty, cancellationToken);
                await RefreshSnapshotAsync(cancellationToken);
                if (NeedsFileSurface())
                {
                    _ = await fileWorkflow.ApplyRecoveryPlaybookAsync(SessionId ?? string.Empty, cancellationToken);
                    currentFileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Remote", cancellationToken);
                    _ = await fileWorkflow.SyncFilesAsync(SessionId ?? string.Empty, currentFileSnapshot.Files, "Remote", cancellationToken);
                    _ = await fileWorkflow.RunRemoteHandoffAsync(SessionId ?? string.Empty, cancellationToken);
                    _ = await fileWorkflow.ValidateRemoteTokensAsync(SessionId ?? string.Empty, cancellationToken);
                }
                break;
            }
            case "incident":
                await incidentWorkflow.LoadAsync(SessionId, cancellationToken);
                if (incidentWorkflow.Snapshot.Incident.AssignedOwner is null)
                {
                    _ = await incidentWorkflow.AssignTriageOwnerAsync(SessionId, cancellationToken);
                }

                if (incidentWorkflow.Snapshot.Incident.MissingEvidenceCount > 0)
                {
                    _ = await incidentWorkflow.CompleteEvidenceReviewAsync(SessionId, cancellationToken);
                }

                _ = await incidentWorkflow.PrepareEscalationBriefAsync(SessionId, cancellationToken);
                break;
        }

        await RefreshSnapshotAsync(cancellationToken);
        LatestBlockers = BuildBlockers();

        var stageTitle = nextStage switch
        {
            "supplier" => "supplier compliance",
            "file" => "file audit",
            "incident" => "incident escalation",
            _ => "workflow"
        };

        var stillNeedsStage = nextStage switch
        {
            "supplier" => NeedsSupplierSurface(),
            "file" => NeedsFileSurface(),
            "incident" => NeedsIncidentSurface(),
            _ => false
        };

        LatestNarrative = stillNeedsStage
            ? $"Advanced the {stageTitle} stage, but it still needs attention: {BuildStageBlocker(nextStage)}"
            : $"Advanced the {stageTitle} stage and updated the shared orchestration state.";
        AddJourneyEvent(nextStage, $"Advanced {stageTitle}", LatestNarrative);
        LatestResult = (stillNeedsStage ? CapabilityResult.Blocked(LatestNarrative) : CapabilityResult.Success(LatestNarrative)) with
        {
            Outputs = BuildOutputsWithStage(nextStage),
            Warnings = BuildWarnings(),
            NextActions = BuildNextActions()
        };

        NotifyChanged();
        return LatestResult;
    }

    public void SetPacketDialogVisible(bool visible)
    {
        IsPacketDialogOpen = visible;
        NotifyChanged();
    }

    public void Reset()
    {
        supplierWorkflow.Reset();
        incidentWorkflow.Reset();
        CurrentPacket = null;
        LatestNarrative = null;
        LatestResult = null;
        LatestBlockers = [];
        IsPacketDialogOpen = false;
        Snapshot = ResponseOrchestrationSnapshot.Empty;
        _journeyEvents.Clear();
        NotifyChanged();
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        await incidentWorkflow.LoadAsync(SessionId, cancellationToken);
    }

    private async Task RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        var fileSnapshot = await fileWorkflow.GetOrCreateAsync(SessionId ?? string.Empty, "Local", cancellationToken);
        var latestFileJob = fileSnapshot.Jobs.OrderByDescending(job => job.UpdatedUtc).FirstOrDefault();

        Snapshot = new ResponseOrchestrationSnapshot(
            supplierWorkflow.HighlightedSupplierIds.Count,
            supplierWorkflow.RecoveredSupplierIds.Count,
            supplierWorkflow.CurrentDraft is not null ? "Draft prepared" : supplierWorkflow.LatestDraftBlockers.Count > 0 ? "Recovery required" : supplierWorkflow.HighlightedSupplierIds.Count > 0 ? "At-risk focus" : "Idle",
            fileSnapshot,
            latestFileJob?.Status ?? "Idle",
            fileSnapshot.Jobs.Count(job => string.Equals(job.Status, "Verified", StringComparison.OrdinalIgnoreCase)),
            incidentWorkflow.Snapshot,
            incidentWorkflow.CurrentBrief is not null ? "Escalation brief ready" : incidentWorkflow.Snapshot.Incident.AssignedOwner is null || incidentWorkflow.Snapshot.Incident.MissingEvidenceCount > 0 ? "Recovery required" : "Ready");
    }

    private string BuildReadinessSummary()
    {
        return $"Response readiness spans {Snapshot.HighlightedSupplierCount} supplier(s), {Snapshot.FileSnapshot.Files.Count} audit file(s), and incident {Snapshot.IncidentSnapshot.Incident.IncidentId}.";
    }

    private string? GetNextGuidedStageKey()
    {
        if (NeedsSupplierSurface())
        {
            return "supplier";
        }

        if (NeedsFileSurface())
        {
            return "file";
        }

        if (NeedsIncidentSurface())
        {
            return "incident";
        }

        return null;
    }

    private string BuildReturnNarrative(string? returnedFrom, string? returnState)
    {
        var summary = GetGuidedJourneySummary(returnedFrom, returnState);
        var nextRoute = GetNextGuidedWorkflowRoute(returnedFrom);
        if (CurrentPacket is not null)
        {
            return $"{summary} The cross-system response packet is ready for review.";
        }

        if (nextRoute is null)
        {
            return $"{summary} All live subsystem surfaces are aligned. Prepare the response packet when you are ready to cross the approval boundary.";
        }

        var nextSurface = nextRoute switch
        {
            var route when route.Contains("/demo/workflows/supplier-compliance", StringComparison.OrdinalIgnoreCase) => "supplier compliance",
            var route when route.Contains("/demo/workflows/file-audit-bundle", StringComparison.OrdinalIgnoreCase) => "file audit",
            var route when route.Contains("/demo/workflows/incident-escalation", StringComparison.OrdinalIgnoreCase) => "incident escalation",
            _ => "next workflow"
        };

        return $"{summary} Next, continue with the {nextSurface} surface.";
    }

    private string DescribeSupplierStatus()
    {
        if (supplierWorkflow.LatestDraftBlockers.Count > 0)
        {
            return supplierWorkflow.LatestDraftBlockers[0];
        }

        if (supplierWorkflow.CurrentDraft is not null)
        {
            return $"Remediation draft is ready for {supplierWorkflow.CurrentDraft.SupplierIds.Count} supplier(s).";
        }

        if (Snapshot.HighlightedSupplierCount > 0)
        {
            return $"Focus is set on {Snapshot.HighlightedSupplierCount} supplier(s), but a remediation draft still needs to be prepared.";
        }

        return "No supplier risk focus has been staged yet.";
    }

    private string DescribeFileStatus()
    {
        if (!string.Equals(Snapshot.FileSnapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase))
        {
            return "Files are still staged locally and need remote handoff.";
        }

        if (LatestBlockers.Any(static blocker => blocker.Contains("Audit evidence bundle still has rejected or failed remote processing.", StringComparison.OrdinalIgnoreCase)))
        {
            return "Remote processing failed for at least one current evidence file.";
        }

        if (Snapshot.VerifiedFileCount == 0)
        {
            return "No remote evidence tokens have been verified yet.";
        }

        return $"{Snapshot.VerifiedFileCount} evidence file(s) are verified and ready for the response packet.";
    }

    private string DescribeIncidentStatus()
    {
        if (incidentWorkflow.CurrentBrief is not null)
        {
            return $"Escalation brief '{incidentWorkflow.CurrentBrief.Title}' is ready for handoff.";
        }

        if (Snapshot.IncidentSnapshot.Incident.AssignedOwner is null)
        {
            return "Incident triage owner still needs to be assigned.";
        }

        if (Snapshot.IncidentSnapshot.Incident.MissingEvidenceCount > 0)
        {
            return $"{Snapshot.IncidentSnapshot.Incident.MissingEvidenceCount} incident evidence item(s) still need review.";
        }

        return "Incident state is aligned, but the escalation brief still needs to be prepared.";
    }

    private string DescribePacketStatus()
    {
        if (CurrentPacket is not null)
        {
            return $"Packet '{CurrentPacket.Title}' is ready for the approval-gated handoff.";
        }

        if (LatestBlockers.Count > 0)
        {
            return $"Packet preparation is waiting on the subsystem blockers: {LatestBlockers[0]}";
        }

        return "All subsystems are aligned. The next step is to prepare the approval-gated response packet.";
    }

    private static ResponseOrchestrationJourneyStepStatus GetJourneyStepStatus(bool isComplete, bool isBlocked, bool isCurrent)
    {
        if (isComplete)
        {
            return ResponseOrchestrationJourneyStepStatus.Complete;
        }

        if (isBlocked)
        {
            return ResponseOrchestrationJourneyStepStatus.Blocked;
        }

        return isCurrent
            ? ResponseOrchestrationJourneyStepStatus.Current
            : ResponseOrchestrationJourneyStepStatus.Pending;
    }

    private IReadOnlyList<string> BuildBlockers()
    {
        var blockers = new List<string>();

        if (supplierWorkflow.LatestDraftBlockers.Count > 0)
        {
            blockers.AddRange(supplierWorkflow.LatestDraftBlockers);
        }

        if (Snapshot.FileSnapshot.UploadMode != "Remote")
        {
            blockers.Add("Audit evidence is still in Local mode.");
        }

        var currentFiles = Snapshot.FileSnapshot.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latestJobsByFile = Snapshot.FileSnapshot.Jobs
            .Where(job => currentFiles.Contains(job.FileName))
            .GroupBy(job => job.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(job => job.UpdatedUtc).First())
            .ToArray();

        if (latestJobsByFile.Any(job => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("Audit evidence bundle still has rejected or failed remote processing.");
        }
        else if (latestJobsByFile.Length > 0 && latestJobsByFile.All(job => !string.Equals(job.Status, "Verified", StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("Audit evidence bundle has not been verified yet.");
        }

        if (incidentWorkflow.Snapshot.Incident.AssignedOwner is null)
        {
            blockers.Add("Incident triage owner has not been assigned.");
        }

        if (incidentWorkflow.Snapshot.Incident.MissingEvidenceCount > 0)
        {
            blockers.Add("Incident evidence review is still incomplete.");
        }

        if (incidentWorkflow.CurrentBrief is null)
        {
            blockers.Add("Incident escalation brief has not been prepared.");
        }

        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<string> BuildWarnings()
    {
        var warnings = new List<string>();
        warnings.AddRange(Snapshot.FileSnapshot.Jobs
            .Where(job => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(job => $"{job.FileName}: {job.Message}"));

        if (supplierWorkflow.RecoveredSupplierIds.Count > 0)
        {
            warnings.Add("Supplier recovery has staged remediation prerequisites, but the response packet still needs a fresh preparation pass.");
        }

        return warnings;
    }

    private IReadOnlyList<string> BuildNextActions()
    {
        if (LatestBlockers.Count > 0)
        {
            return
            [
                "Apply the cross-system recovery playbook.",
                "Prepare the cross-system response packet again once the blockers are cleared."
            ];
        }

        if (CurrentPacket is not null)
        {
            return
            [
                "Review the cross-system response packet in the dialog.",
                "Approve the packet for downstream handoff."
            ];
        }

        return
        [
            "Assess cross-system response readiness.",
            "Prepare the response packet once supplier, file, and incident states are aligned."
        ];
    }

    private IReadOnlyDictionary<string, object?> BuildOutputs()
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["highlightedSupplierCount"] = Snapshot.HighlightedSupplierCount,
            ["recoveredSupplierCount"] = Snapshot.RecoveredSupplierCount,
            ["fileCount"] = Snapshot.FileSnapshot.Files.Count,
            ["verifiedFileCount"] = Snapshot.VerifiedFileCount,
            ["fileBundlePhase"] = Snapshot.FileBundlePhase,
            ["incidentId"] = Snapshot.IncidentSnapshot.Incident.IncidentId,
            ["incidentPhase"] = Snapshot.IncidentPhase,
            ["blockers"] = LatestBlockers.ToArray()
        };
    }

    private IReadOnlyDictionary<string, object?> BuildOutputsWithStage(string stageKey)
    {
        var outputs = new Dictionary<string, object?>(BuildOutputs(), StringComparer.OrdinalIgnoreCase)
        {
            ["advancedStage"] = stageKey,
            ["nextGuidedStage"] = GetNextGuidedStageKey()
        };

        return outputs;
    }

    private string BuildStageBlocker(string stageKey)
    {
        return stageKey switch
        {
            "supplier" when supplierWorkflow.LatestDraftBlockers.Count > 0 => supplierWorkflow.LatestDraftBlockers[0],
            "file" => LatestBlockers.FirstOrDefault(static blocker => blocker.Contains("Audit evidence", StringComparison.OrdinalIgnoreCase))
                      ?? "Audit evidence still needs attention.",
            "incident" => LatestBlockers.FirstOrDefault(static blocker => blocker.Contains("Incident", StringComparison.OrdinalIgnoreCase))
                          ?? "Incident escalation still needs attention.",
            _ => LatestBlockers.FirstOrDefault() ?? "The workflow still needs attention."
        };
    }

    private void AddJourneyEvent(string stageKey, string title, string summary)
    {
        _journeyEvents.Insert(0, new ResponseOrchestrationJourneyEvent(DateTimeOffset.UtcNow, stageKey, title, summary));
        if (_journeyEvents.Count > 12)
        {
            _journeyEvents.RemoveRange(12, _journeyEvents.Count - 12);
        }
    }

    private void NotifyChanged() => Changed?.Invoke();

    private static string BuildWorkflowRoute(string route, string focus)
    {
        return $"{route}?source={Uri.EscapeDataString(ResponseSource)}&focus={Uri.EscapeDataString(focus)}&returnTo={Uri.EscapeDataString(OrchestrationRoute)}";
    }

    private string GetSupplierRouteFocus()
    {
        if (supplierWorkflow.LatestDraftBlockers.Count > 0)
        {
            return "recovery";
        }

        return supplierWorkflow.HighlightedSupplierIds.Count > 0
            ? "remediation"
            : "suppliers";
    }

    private string GetFileRouteFocus()
    {
        if (Snapshot.FileSnapshot.UploadMode != "Remote" || LatestBlockers.Any(static blocker => blocker.Contains("Audit evidence", StringComparison.OrdinalIgnoreCase)))
        {
            return "remote-bundle";
        }

        return Snapshot.VerifiedFileCount > 0
            ? "verification"
            : "handoff";
    }

    private string GetIncidentRouteFocus()
    {
        if (incidentWorkflow.CurrentBrief is not null)
        {
            return "escalation";
        }

        if (LatestBlockers.Any(static blocker => blocker.Contains("Incident", StringComparison.OrdinalIgnoreCase)))
        {
            return "evidence";
        }

        return "triage";
    }

    private bool NeedsSupplierSurface()
        => supplierWorkflow.LatestDraftBlockers.Count > 0
           || supplierWorkflow.CurrentDraft is null;

    private bool NeedsFileSurface()
        => !string.Equals(Snapshot.FileSnapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase)
           || Snapshot.VerifiedFileCount == 0;

    private bool NeedsIncidentSurface()
        => incidentWorkflow.CurrentBrief is null
           || incidentWorkflow.Snapshot.Incident.AssignedOwner is null
           || incidentWorkflow.Snapshot.Incident.MissingEvidenceCount > 0;
}

internal sealed record ResponseOrchestrationSnapshot(
    int HighlightedSupplierCount,
    int RecoveredSupplierCount,
    string SupplierPhase,
    DemoFileWorkflowSnapshot FileSnapshot,
    string FileBundlePhase,
    int VerifiedFileCount,
    IncidentEscalationSnapshot IncidentSnapshot,
    string IncidentPhase)
{
    public static ResponseOrchestrationSnapshot Empty { get; } = new(
        0,
        0,
        "Idle",
        new DemoFileWorkflowSnapshot([], "Local", [], []),
        "Idle",
        0,
        new IncidentEscalationSnapshot(
            new IncidentEscalationCase(
                "INC-0000",
                "No incident loaded",
                "Unknown",
                "Unknown",
                "system",
                null,
                0,
                "Pending",
                false,
                "Initial triage",
                false,
                null,
                []),
            "overview",
            ["overview"],
            0,
            0,
            null,
            [],
            []),
        "Idle");
}

internal sealed record ResponsePacketDraft(
    string Title,
    IReadOnlyList<string> SupplierIds,
    IReadOnlyList<string> FileNames,
    string IncidentId,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> HandoffChecklist);

internal enum ResponseOrchestrationJourneyStepStatus
{
    Pending,
    Current,
    Blocked,
    Complete
}

internal sealed record ResponseOrchestrationJourneyStep(
    string Key,
    string Title,
    string Phase,
    string Summary,
    string? Route,
    ResponseOrchestrationJourneyStepStatus Status);

internal sealed record ResponseOrchestrationJourneyEvent(
    DateTimeOffset TimestampUtc,
    string StageKey,
    string Title,
    string Summary);
