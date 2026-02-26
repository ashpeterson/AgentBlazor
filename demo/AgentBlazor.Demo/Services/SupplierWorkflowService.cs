using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentBlazor.Demo.Services;

internal sealed class SupplierWorkflowService(IDbContextFactory<DemoWorkflowDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<SupplierRow>> ListSuppliersAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Suppliers
            .OrderBy(static x => x.SupplierId)
            .Select(static x => new SupplierRow(
                x.SupplierId,
                x.SupplierName,
                x.Region,
                x.RiskScore,
                x.LastAuditDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierOnboardingResult> SubmitOnboardingAsync(
        SupplierOnboardingSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var supplierId = await GetNextIdAsync(
            db.Suppliers.Select(static x => x.SupplierId),
            prefix: "SUP-",
            pad: 3,
            cancellationToken);

        var requestId = await GetNextIdAsync(
            db.OnboardingRequests.Select(static x => x.RequestId),
            prefix: "REQ-",
            pad: 4,
            cancellationToken);

        var now = DateTime.UtcNow;
        var riskScore = ComputeRiskScore(submission);

        db.Suppliers.Add(new SupplierEntity
        {
            SupplierId = supplierId,
            SupplierName = submission.SupplierName.Trim(),
            Region = ResolveRegion(submission.Country),
            RiskScore = riskScore,
            LastAuditDate = now.Date,
            CreatedUtc = now
        });

        db.OnboardingRequests.Add(new OnboardingRequestEntity
        {
            RequestId = requestId,
            SupplierId = supplierId,
            SupplierName = submission.SupplierName.Trim(),
            ContactEmail = submission.ContactEmail.Trim(),
            RiskTier = submission.RiskTier.Trim(),
            Country = submission.Country.Trim(),
            Category = submission.Category.Trim(),
            PaymentTerms = submission.PaymentTerms.Trim(),
            PreferredCurrency = string.IsNullOrWhiteSpace(submission.PreferredCurrency)
                ? "USD"
                : submission.PreferredCurrency.Trim().ToUpperInvariant(),
            RequestedBudget = submission.RequestedBudget,
            ExpectedMonthlySpend = submission.ExpectedMonthlySpend,
            Priority = submission.Priority,
            SanctionsScreened = submission.SanctionsScreened,
            NdaRequired = submission.NdaRequired,
            Notes = submission.Notes.Trim(),
            Status = "Submitted",
            SubmittedUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return new SupplierOnboardingResult(
            requestId,
            supplierId,
            submission.SupplierName.Trim(),
            riskScore);
    }

    public async Task<IReadOnlyList<RegionRiskPoint>> GetAverageRiskByRegionAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Suppliers
            .GroupBy(static x => x.Region)
            .Select(static group => new RegionRiskPoint(
                group.Key,
                Math.Round(group.Average(static x => x.RiskScore), 1)))
            .OrderByDescending(static x => x.AverageRisk)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RiskTierPoint>> GetRiskTierDistributionAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Suppliers
            .GroupBy(static x => x.RiskScore >= 80
                ? "High"
                : x.RiskScore >= 60
                    ? "Medium"
                    : "Low")
            .Select(static group => new RiskTierPoint(group.Key, group.Count()))
            .OrderByDescending(static x => x.Count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OnboardingRequestRow>> ListOnboardingRequestsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.OnboardingRequests
            .OrderByDescending(static x => x.SubmittedUtc)
            .Select(static x => new OnboardingRequestRow(
                x.RequestId,
                x.SupplierId,
                x.SupplierName,
                x.RiskTier,
                x.Priority,
                x.Status,
                x.SubmittedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkflowSummary> GetWorkflowSummaryAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var totalSuppliers = await db.Suppliers.CountAsync(cancellationToken);
        var highRiskSuppliers = await db.Suppliers.CountAsync(x => x.RiskScore >= 80, cancellationToken);
        var openOnboarding = await db.OnboardingRequests
            .CountAsync(x => x.Status == "Submitted" || x.Status == "InReview", cancellationToken);
        var submittedThisMonth = await db.OnboardingRequests
            .CountAsync(x => x.SubmittedUtc >= monthStart, cancellationToken);

        return new WorkflowSummary(
            totalSuppliers,
            highRiskSuppliers,
            openOnboarding,
            submittedThisMonth);
    }

    public async Task<IReadOnlyList<MonthlyOnboardingPoint>> GetMonthlyOnboardingVolumeAsync(
        int months,
        CancellationToken cancellationToken)
    {
        months = Math.Clamp(months, 3, 18);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var monthStarts = Enumerable.Range(0, months)
            .Select(offset => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-(months - 1 - offset)))
            .ToArray();

        var firstMonth = monthStarts[0];

        var submitted = await db.OnboardingRequests
            .Where(x => x.SubmittedUtc >= firstMonth)
            .ToListAsync(cancellationToken);

        var counts = submitted
            .GroupBy(x => new DateTime(x.SubmittedUtc.Year, x.SubmittedUtc.Month, 1))
            .ToDictionary(static group => group.Key, static group => group.Count());

        return monthStarts
            .Select(month => new MonthlyOnboardingPoint(
                month.ToString("MMM yyyy"),
                counts.TryGetValue(month, out var count) ? count : 0))
            .ToArray();
    }

    private static int ComputeRiskScore(SupplierOnboardingSubmission submission)
    {
        var baseRisk = submission.RiskTier.Trim().ToLowerInvariant() switch
        {
            "high" => 82,
            "medium" => 64,
            "low" => 44,
            _ => 58
        };

        var priorityAdjustment = (submission.Priority - 3) * 4;
        var sanctionsAdjustment = submission.SanctionsScreened ? -6 : 10;
        var ndaAdjustment = submission.NdaRequired ? 3 : 0;
        var spendAdjustment = submission.ExpectedMonthlySpend >= 50000m ? 5 : 0;

        return Math.Clamp(baseRisk + priorityAdjustment + sanctionsAdjustment + ndaAdjustment + spendAdjustment, 0, 100);
    }

    private static async Task<string> GetNextIdAsync(
        IQueryable<string> existingIds,
        string prefix,
        int pad,
        CancellationToken cancellationToken)
    {
        var ids = await existingIds.ToListAsync(cancellationToken);
        var max = 0;

        foreach (var id in ids)
        {
            if (TryParseTrailingNumber(id, prefix, out var parsed))
            {
                max = Math.Max(max, parsed);
            }
        }

        return $"{prefix}{(max + 1).ToString($"D{pad}")}";
    }

    private static bool TryParseTrailingNumber(string id, string prefix, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(id[prefix.Length..], out number);
    }

    private static string ResolveRegion(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "NA";
        }

        var normalized = country.Trim().ToLowerInvariant();
        return normalized switch
        {
            "united states" or "usa" or "canada" or "mexico" => "NA",
            "united kingdom" or "ireland" or "germany" or "france" or "spain" or "italy" => "EMEA",
            "japan" or "china" or "india" or "singapore" or "australia" => "APAC",
            "brazil" or "argentina" or "chile" or "colombia" => "LATAM",
            _ => "Global"
        };
    }
}

internal sealed record SupplierOnboardingSubmission(
    string SupplierName,
    string ContactEmail,
    string ContactPhone,
    string RiskTier,
    string Country,
    string Category,
    string PaymentTerms,
    string PreferredCurrency,
    decimal RequestedBudget,
    decimal ExpectedMonthlySpend,
    int Priority,
    bool SanctionsScreened,
    bool NdaRequired,
    string Notes);

internal sealed record SupplierOnboardingResult(
    string RequestId,
    string SupplierId,
    string SupplierName,
    int RiskScore);

internal sealed record RegionRiskPoint(string Region, double AverageRisk);

internal sealed record RiskTierPoint(string Tier, int Count);

internal sealed record MonthlyOnboardingPoint(string Month, int Count);

internal sealed record OnboardingRequestRow(
    string RequestId,
    string SupplierId,
    string SupplierName,
    string RiskTier,
    int Priority,
    string Status,
    DateTime SubmittedUtc);

internal sealed record WorkflowSummary(
    int TotalSuppliers,
    int HighRiskSuppliers,
    int OpenOnboardingRequests,
    int SubmittedThisMonth);
