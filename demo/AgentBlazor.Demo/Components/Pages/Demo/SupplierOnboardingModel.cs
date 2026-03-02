using System.ComponentModel.DataAnnotations;

namespace AgentBlazor.Demo.Components.Pages.Demo;

/// <summary>
/// Model for the supplier onboarding form.
/// </summary>
public sealed class SupplierOnboardingModel
{
    [Required]
    public string SupplierName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string ContactEmail { get; set; } = string.Empty;

    [Phone]
    public string ContactPhone { get; set; } = string.Empty;

    [Required]
    public string RiskTier { get; set; } = string.Empty;

    [Required]
    public string Country { get; set; } = string.Empty;

    [Required]
    public string Category { get; set; } = string.Empty;

    [Required]
    public string PaymentTerms { get; set; } = string.Empty;

    [Required]
    public string PreferredCurrency { get; set; } = "USD";

    [Range(typeof(decimal), "0.01", "1000000000")]
    public decimal RequestedBudget { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal ExpectedMonthlySpend { get; set; }

    [Range(1, 5)]
    public int Priority { get; set; } = 3;

    public bool SanctionsScreened { get; set; }

    public bool NdaRequired { get; set; }

    [StringLength(500)]
    public string Notes { get; set; } = string.Empty;
}
