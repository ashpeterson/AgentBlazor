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

        await db.SaveChangesAsync(cancellationToken);
    }
}
