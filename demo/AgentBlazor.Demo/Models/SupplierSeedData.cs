namespace AgentBlazor.Demo.Models;

public static class SupplierSeedData
{
    public static List<SupplierRow> Suppliers() =>
    [
        new("SUP-001", "Alpine Components", "EMEA", 82, new DateTime(2025, 12, 04)),
        new("SUP-002", "Beacon Industrial", "NA", 71, new DateTime(2026, 01, 12)),
        new("SUP-003", "Crestline Textiles", "APAC", 49, new DateTime(2025, 11, 21)),
        new("SUP-004", "Deltawave Metals", "EMEA", 67, new DateTime(2026, 02, 06)),
        new("SUP-005", "Everfield Logistics", "NA", 38, new DateTime(2025, 10, 19)),
        new("SUP-006", "Farside Plastics", "APAC", 91, new DateTime(2026, 02, 10)),
        new("SUP-007", "Grayson Foods", "EMEA", 55, new DateTime(2025, 09, 15)),
        new("SUP-008", "Harbor Electric", "LATAM", 62, new DateTime(2025, 12, 28)),
        new("SUP-009", "Ionis Chemical", "NA", 76, new DateTime(2026, 01, 31)),
        new("SUP-010", "Juniper BioWorks", "APAC", 44, new DateTime(2025, 08, 07)),
        new("SUP-011", "Keystone Minerals", "EMEA", 88, new DateTime(2026, 02, 02)),
        new("SUP-012", "Lighthouse MedTech", "NA", 59, new DateTime(2025, 11, 03))
    ];
}

public sealed record SupplierRow(
    string SupplierId,
    string SupplierName,
    string Region,
    int RiskScore,
    DateTime LastAuditDate);
