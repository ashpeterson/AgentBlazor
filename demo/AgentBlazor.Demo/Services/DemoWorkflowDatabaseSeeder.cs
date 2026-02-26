using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoWorkflowDatabaseSeeder(IDbContextFactory<DemoWorkflowDbContext> dbContextFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureMitigationSchemaAsync(db, cancellationToken);

        if (!await db.Suppliers.AnyAsync(cancellationToken))
        {
            var now = DateTime.UtcNow;
            foreach (var seed in SupplierSeedData.Suppliers())
            {
                db.Suppliers.Add(new SupplierEntity
                {
                    SupplierId = seed.SupplierId,
                    SupplierName = seed.SupplierName,
                    Region = seed.Region,
                    RiskScore = seed.RiskScore,
                    LastAuditDate = seed.LastAuditDate,
                    CreatedUtc = now.AddDays(-Random.Shared.Next(20, 180))
                });
            }
        }

        if (!await db.OnboardingRequests.AnyAsync(cancellationToken))
        {
            var samples = SupplierSeedData.Suppliers().Take(8).ToArray();
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                db.OnboardingRequests.Add(new OnboardingRequestEntity
                {
                    RequestId = $"REQ-{(i + 1).ToString("D4")}",
                    SupplierId = sample.SupplierId,
                    SupplierName = sample.SupplierName,
                    ContactEmail = $"ops+{sample.SupplierId.ToLowerInvariant()}@example.com",
                    RiskTier = sample.RiskScore >= 80 ? "High" : sample.RiskScore >= 60 ? "Medium" : "Low",
                    Country = sample.Region switch
                    {
                        "EMEA" => "Germany",
                        "APAC" => "Singapore",
                        "LATAM" => "Brazil",
                        _ => "United States"
                    },
                    Category = "Strategic Parts",
                    PaymentTerms = "Net 45",
                    PreferredCurrency = "USD",
                    RequestedBudget = 120000 + (i * 5000),
                    ExpectedMonthlySpend = 24000 + (i * 1200),
                    Priority = (i % 5) + 1,
                    SanctionsScreened = i % 3 != 0,
                    NdaRequired = i % 2 == 0,
                    Notes = "Seeded onboarding request for workflow demos.",
                    Status = "Submitted",
                    SubmittedUtc = DateTime.UtcNow.AddMonths(-7 + i).AddDays(Random.Shared.Next(0, 20))
                });
            }
        }

        if (!await db.MitigationTasks.AnyAsync(cancellationToken))
        {
            var seededSuppliers = await db.Suppliers
                .OrderByDescending(x => x.RiskScore)
                .Take(4)
                .ToListAsync(cancellationToken);

            for (var i = 0; i < seededSuppliers.Count; i++)
            {
                var supplier = seededSuppliers[i];
                db.MitigationTasks.Add(new MitigationTaskEntity
                {
                    TaskId = $"MIT-{(i + 1).ToString("D4")}",
                    SupplierId = supplier.SupplierId,
                    SupplierName = supplier.SupplierName,
                    ServiceName = "Supplier Risk Monitoring",
                    Owner = i % 2 == 0 ? "Ops Lead" : "Risk Analyst",
                    MitigationAction = "Increase audit cadence and validate contingency coverage.",
                    Priority = Math.Clamp(1 + i, 1, 5),
                    Status = i == 0 ? "InProgress" : "Open",
                    DueDate = DateTime.UtcNow.Date.AddDays(7 + (i * 3)),
                    CreatedUtc = DateTime.UtcNow.AddDays(-(i + 2))
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureMitigationSchemaAsync(
        DemoWorkflowDbContext db,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS mitigation_tasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId TEXT NOT NULL,
                SupplierId TEXT NOT NULL,
                SupplierName TEXT NOT NULL,
                ServiceName TEXT NOT NULL,
                Owner TEXT NOT NULL,
                MitigationAction TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                Status TEXT NOT NULL,
                DueDate TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                CompletedUtc TEXT NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_mitigation_tasks_TaskId ON mitigation_tasks (TaskId);",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_mitigation_tasks_SupplierId ON mitigation_tasks (SupplierId);",
            cancellationToken);
    }
}
