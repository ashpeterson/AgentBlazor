using System.ComponentModel.DataAnnotations;

namespace AgentBlazor.Demo.Data;

internal sealed class MitigationTaskEntity
{
    public int Id { get; set; }

    [MaxLength(24)]
    public string TaskId { get; set; } = string.Empty;

    [MaxLength(24)]
    public string SupplierId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string SupplierName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ServiceName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Owner { get; set; } = string.Empty;

    [MaxLength(500)]
    public string MitigationAction { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Priority { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "Open";

    public DateTime DueDate { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }
}
