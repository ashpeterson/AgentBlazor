using AgentBlazor.App;

namespace AgentBlazor.Starter.Services;

public sealed class OpsReviewService
{
    public OpsReviewState Current { get; private set; } = CreateInitialState();

    public CapabilityResult Assess()
    {
        var result = CapabilityResult.Success(Current.Summary)
            .WithOutput("supplierCount", Current.SupplierCount)
            .WithOutput("reviewQueue", Current.ReviewQueue);

        if (Current.HasBlocker)
        {
            result = CapabilityResult.Blocked("Manual supplier review is still blocking draft preparation.")
                .WithWarning("One supplier still needs a human decision.")
                .WithNextActions("Apply the recovery playbook", "Ask what changed since the last review")
                .WithOutput("supplierCount", Current.SupplierCount)
                .WithOutput("reviewQueue", Current.ReviewQueue);
        }
        else
        {
            result = result
                .WithNextActions("Prepare the review draft", "Explain the approval step");
        }

        Current = Current with
        {
            Summary = result.Summary,
            Blockers = Current.HasBlocker
                ? ["Manual supplier review is still blocking draft preparation."]
                : [],
            Warnings = [.. result.Warnings],
            NextActions = [.. result.NextActions]
        };

        return result;
    }

    public CapabilityResult ApplyRecoveryPlaybook()
    {
        Current = Current with
        {
            Phase = "Recovered",
            HasBlocker = false,
            ReviewQueue = 0,
            Summary = "Manual review is complete. The draft can now be prepared for approval.",
            Blockers = [],
            Warnings = [],
            NextActions =
            [
                "Prepare the review draft",
                "Explain the approval step"
            ]
        };

        return CapabilityResult.Success(Current.Summary)
            .WithNextActions(Current.NextActions.ToArray())
            .WithOutput("phase", Current.Phase);
    }

    public CapabilityResult PrepareDraft()
    {
        if (Current.HasBlocker)
        {
            return CapabilityResult.Blocked("The draft is still blocked by manual review.")
                .WithWarning("Run the recovery playbook first.")
                .WithNextActions("Apply the recovery playbook");
        }

        Current = Current with
        {
            Phase = "Ready",
            DraftPrepared = true,
            RequiresApproval = true,
            Summary = "Prepared the ops review draft. Approval is required before submission.",
            Blockers = [],
            Warnings = [],
            NextActions =
            [
                "Review the draft",
                "Approve the submission"
            ]
        };

        return CapabilityResult.Success(Current.Summary)
            .WithNextActions(Current.NextActions.ToArray())
            .WithOutput("phase", Current.Phase)
            .WithOutput("approvalRequired", true);
    }

    public CapabilityResult Reset()
    {
        Current = CreateInitialState();
        return CapabilityResult.Success("Reset the starter workflow.");
    }

    private static OpsReviewState CreateInitialState() => new(
        Phase: "Assess",
        Summary: "Review three supplier updates and decide if the draft is ready.",
        SupplierCount: 3,
        ReviewQueue: 1,
        HasBlocker: true,
        DraftPrepared: false,
        RequiresApproval: false,
        Blockers:
        [
            "Manual supplier review is still blocking draft preparation."
        ],
        Warnings:
        [
            "One supplier still needs manual review."
        ],
        NextActions:
        [
            "Apply the recovery playbook",
            "Explain why the draft is blocked"
        ]);
}

public sealed record OpsReviewState(
    string Phase,
    string Summary,
    int SupplierCount,
    int ReviewQueue,
    bool HasBlocker,
    bool DraftPrepared,
    bool RequiresApproval,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NextActions);
