using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace AgentBlazor.Demo.Services;

[AgentCapability("supplier_compliance", Name = "Supplier Compliance Workflow", Description = "Identify at-risk suppliers, explain the risk signals, and prepare remediation drafts.", Category = "Workflow")]
internal sealed class SupplierComplianceCapabilities(SupplierComplianceWorkflowService workflow)
{
    [AgentAction("Show suppliers likely to fail compliance review", ActionId = "show_at_risk_suppliers")]
    public Task<CapabilityResult> ShowAtRiskSuppliersAsync(
        [AgentParam("Days until review horizon", Required = false)] int days = 30)
    {
        var summary = workflow.FocusAtRiskSuppliers(days);
        return Task.FromResult(CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["days"] = days,
                ["highlightedSupplierIds"] = workflow.HighlightedSupplierIds.ToArray(),
                ["visibleSupplierCount"] = workflow.VisibleSuppliers.Count
            },
            NextActions =
            [
                "Explain the current risk signals",
                "Prepare a remediation draft for the highlighted suppliers"
            ]
        });
    }

    [AgentAction("Explain why the highlighted suppliers are at risk", ActionId = "explain_at_risk_suppliers")]
    public Task<CapabilityResult> ExplainAtRiskSuppliersAsync()
    {
        var summary = workflow.ExplainFocusedSuppliers();
        return Task.FromResult(CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["explanation"] = workflow.LatestInsight,
                ["highlightedSupplierIds"] = workflow.HighlightedSupplierIds.ToArray()
            },
            NextActions =
            [
                "Prepare a remediation draft for the highlighted suppliers"
            ]
        });
    }

    [AgentAction("Prepare a remediation draft for the highlighted suppliers", ActionId = "prepare_remediation_draft", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareRemediationDraftAsync()
    {
        if (!workflow.HighlightedSupplierIds.Any())
        {
            return Task.FromResult(CapabilityResult.NeedsClarification(
                "No suppliers are highlighted yet. Ask me to show at-risk suppliers first or specify which suppliers should be remediated."));
        }

        var summary = workflow.PrepareRemediationDraft();
        return Task.FromResult(CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>
            {
                ["draftTitle"] = workflow.CurrentDraft?.Title,
                ["draftSupplierIds"] = workflow.CurrentDraft?.SupplierIds.ToArray(),
                ["draftActionCount"] = workflow.CurrentDraft?.Actions.Count ?? 0
            },
            NextActions =
            [
                "Review the remediation draft",
                "Approve the remediation draft"
            ]
        });
    }

    [AgentAction("Reset the supplier compliance workflow view", ActionId = "reset_supplier_workflow")]
    public Task<CapabilityResult> ResetSupplierWorkflowAsync()
    {
        workflow.Reset();
        return Task.FromResult(CapabilityResult.Success("Reset the supplier compliance workflow view."));
    }
}

internal sealed class SupplierComplianceWorkflowService
{
    private readonly List<SupplierComplianceRow> _suppliers =
    [
        new("SUP-100", "Apex Components", "China", "High", 12, 97, 3, true, false),
        new("SUP-101", "Beacon Industrial", "Poland", "Medium", 44, 61, 1, false, false),
        new("SUP-102", "Cinder Logistics", "Vietnam", "High", 18, 132, 2, true, true),
        new("SUP-103", "Delta Fabrication", "Mexico", "Low", 120, 41, 0, false, false),
        new("SUP-104", "Evergreen Materials", "Turkey", "High", 8, 84, 4, false, true)
    ];

    private readonly HashSet<string> _highlightedSupplierIds = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public int CurrentReviewWindowDays { get; private set; } = 30;

    public string? LatestInsight { get; private set; }

    public SupplierRemediationDraft? CurrentDraft { get; private set; }

    public bool IsRemediationDialogOpen { get; private set; }

    public IReadOnlyList<SupplierComplianceRow> VisibleSuppliers =>
        _suppliers
            .Where(static supplier => true)
            .Where(supplier => !_highlightedSupplierIds.Any() || _highlightedSupplierIds.Contains(supplier.Id) || !ShowOnlyHighlighted)
            .ToArray();

    public bool ShowOnlyHighlighted { get; private set; }

    public IReadOnlyCollection<string> HighlightedSupplierIds => _highlightedSupplierIds.ToArray();

    public string FocusAtRiskSuppliers(int days)
    {
        CurrentReviewWindowDays = days <= 0 ? 30 : days;
        ShowOnlyHighlighted = true;
        _highlightedSupplierIds.Clear();

        foreach (var supplier in _suppliers.Where(s => IsAtRisk(s, CurrentReviewWindowDays)))
        {
            _highlightedSupplierIds.Add(supplier.Id);
        }

        CurrentDraft = null;
        IsRemediationDialogOpen = false;
        LatestInsight = BuildInsightSummary(_highlightedSupplierIds);
        NotifyChanged();

        return _highlightedSupplierIds.Count == 0
            ? $"No suppliers match the at-risk review window of {CurrentReviewWindowDays} days."
            : $"Highlighted {_highlightedSupplierIds.Count} suppliers that are likely to fail review within {CurrentReviewWindowDays} days.";
    }

    public string ExplainFocusedSuppliers()
    {
        if (_highlightedSupplierIds.Count == 0)
        {
            LatestInsight = "No suppliers are highlighted yet. Start by identifying at-risk suppliers.";
            NotifyChanged();
            return LatestInsight;
        }

        LatestInsight = BuildInsightSummary(_highlightedSupplierIds);
        NotifyChanged();
        return LatestInsight;
    }

    public string PrepareRemediationDraft()
    {
        var targetedSuppliers = _suppliers
            .Where(supplier => _highlightedSupplierIds.Contains(supplier.Id))
            .ToArray();

        CurrentDraft = new SupplierRemediationDraft(
            "Prepare remediation batch",
            targetedSuppliers.Select(static supplier => supplier.Id).ToArray(),
            targetedSuppliers.SelectMany(BuildDraftActions).ToArray());
        IsRemediationDialogOpen = true;
        LatestInsight = $"Prepared a remediation draft for {targetedSuppliers.Length} highlighted suppliers.";
        NotifyChanged();
        return LatestInsight;
    }

    public void SetRemediationDialogVisible(bool visible)
    {
        IsRemediationDialogOpen = visible;
        NotifyChanged();
    }

    public void Reset()
    {
        CurrentReviewWindowDays = 30;
        LatestInsight = null;
        CurrentDraft = null;
        IsRemediationDialogOpen = false;
        ShowOnlyHighlighted = false;
        _highlightedSupplierIds.Clear();
        NotifyChanged();
    }

    public bool IsHighlighted(string supplierId) => _highlightedSupplierIds.Contains(supplierId);

    public string DescribeSignals(SupplierComplianceRow supplier)
    {
        var reasons = GetRiskSignals(supplier, CurrentReviewWindowDays);
        return reasons.Count == 0
            ? "No active risk signals"
            : string.Join(", ", reasons);
    }

    private static bool IsAtRisk(SupplierComplianceRow supplier, int days) =>
        supplier.CertificateExpiryDays <= days
        || supplier.AuditAgeDays >= 90
        || supplier.OpenRemediationActions > 1
        || supplier.MissingIsoEvidence
        || supplier.MissedLastAudit;

    private static IReadOnlyList<string> GetRiskSignals(SupplierComplianceRow supplier, int days)
    {
        var reasons = new List<string>();

        if (supplier.CertificateExpiryDays <= days)
        {
            reasons.Add($"certificate expires in {supplier.CertificateExpiryDays} days");
        }

        if (supplier.AuditAgeDays >= 90)
        {
            reasons.Add($"last audit was {supplier.AuditAgeDays} days ago");
        }

        if (supplier.OpenRemediationActions > 1)
        {
            reasons.Add($"{supplier.OpenRemediationActions} remediation actions are still open");
        }

        if (supplier.MissingIsoEvidence)
        {
            reasons.Add("ISO evidence bundle is missing");
        }

        if (supplier.MissedLastAudit)
        {
            reasons.Add("last scheduled audit was missed");
        }

        return reasons;
    }

    private string BuildInsightSummary(IEnumerable<string> supplierIds)
    {
        var suppliers = _suppliers
            .Where(supplier => supplierIds.Contains(supplier.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (suppliers.Length == 0)
        {
            return "No suppliers are currently in focus.";
        }

        var lines = suppliers.Select(supplier =>
            $"{supplier.Name}: {string.Join("; ", GetRiskSignals(supplier, CurrentReviewWindowDays))}");

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> BuildDraftActions(SupplierComplianceRow supplier)
    {
        yield return $"Open supplier record for {supplier.Name}.";
        yield return $"Queue certificate renewal follow-up for {supplier.Id}.";

        if (supplier.MissingIsoEvidence)
        {
            yield return $"Request missing ISO evidence from {supplier.Name}.";
        }

        if (supplier.MissedLastAudit || supplier.AuditAgeDays >= 90)
        {
            yield return $"Schedule a compliance audit for {supplier.Name}.";
        }

        if (supplier.OpenRemediationActions > 0)
        {
            yield return $"Escalate {supplier.OpenRemediationActions} open remediation action(s) for {supplier.Name}.";
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}

internal sealed record SupplierComplianceRow(
    string Id,
    string Name,
    string Country,
    string RiskTier,
    int CertificateExpiryDays,
    int AuditAgeDays,
    int OpenRemediationActions,
    bool MissingIsoEvidence,
    bool MissedLastAudit);

internal sealed record SupplierRemediationDraft(
    string Title,
    IReadOnlyList<string> SupplierIds,
    IReadOnlyList<string> Actions);
