using System.ComponentModel.DataAnnotations;

namespace AgentBlazor.Demo.Data;

internal sealed class OnboardingRequestEntity
{
    public int Id { get; set; }

    [MaxLength(24)]
    public string RequestId { get; set; } = string.Empty;

    [MaxLength(24)]
    public string SupplierId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string SupplierName { get; set; } = string.Empty;

    [MaxLength(160)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(64)]
    public string RiskTier { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Country { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(80)]
    public string PaymentTerms { get; set; } = string.Empty;

    [MaxLength(16)]
    public string PreferredCurrency { get; set; } = "USD";

    public decimal RequestedBudget { get; set; }

    public decimal ExpectedMonthlySpend { get; set; }

    [Range(1, 5)]
    public int Priority { get; set; }

    public bool SanctionsScreened { get; set; }

    public bool NdaRequired { get; set; }

    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = "Submitted";

    public DateTime SubmittedUtc { get; set; }
}
