using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgentBlazor.Demo.Components.Pages.Demo;

/// <summary>
/// Model for the supplier onboarding form.
/// Properties are auto-discovered by AgentFormPageBase to generate agent actions.
/// </summary>
public sealed class SupplierOnboardingModel
{
    [Required]
    [Description("Name of the supplier company")]
    public string SupplierName { get; set; } = string.Empty;

    [Required, EmailAddress]
    [Description("Contact email address")]
    public string ContactEmail { get; set; } = string.Empty;

    [Phone]
    [Description("Contact phone number")]
    public string ContactPhone { get; set; } = string.Empty;

    [Required]
    [Description("Risk tier: low, medium, or high")]
    public string RiskTier { get; set; } = string.Empty;

    [Required]
    [Description("Country where the supplier is located")]
    public string Country { get; set; } = string.Empty;

    [Required]
    [Description("Supplier category")]
    public string Category { get; set; } = string.Empty;

    [Required]
    [Description("Payment terms")]
    public string PaymentTerms { get; set; } = string.Empty;

    [Required]
    [Description("Preferred currency (e.g., USD, EUR)")]
    public string PreferredCurrency { get; set; } = "USD";

    [Range(typeof(decimal), "0.01", "1000000000")]
    [Description("Requested budget amount")]
    public decimal RequestedBudget { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    [Description("Expected monthly spend amount")]
    public decimal ExpectedMonthlySpend { get; set; }

    [Range(1, 5)]
    [Description("Priority level from 1 (highest) to 5 (lowest)")]
    public int Priority { get; set; } = 3;

    [Description("Whether sanctions screening has been completed")]
    public bool SanctionsScreened { get; set; }

    [Description("Whether an NDA is required")]
    public bool NdaRequired { get; set; }

    [StringLength(500)]
    [Description("Additional notes")]
    public string Notes { get; set; } = string.Empty;
}
