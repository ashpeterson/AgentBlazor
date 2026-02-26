using System.ComponentModel.DataAnnotations;

namespace AgentBlazor.Demo.Data;

internal sealed class SupplierEntity
{
    public int Id { get; set; }

    [MaxLength(24)]
    public string SupplierId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string SupplierName { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Region { get; set; } = string.Empty;

    [Range(0, 100)]
    public int RiskScore { get; set; }

    public DateTime LastAuditDate { get; set; }

    public DateTime CreatedUtc { get; set; }
}
