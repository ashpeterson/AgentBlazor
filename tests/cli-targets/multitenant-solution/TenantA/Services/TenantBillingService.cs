namespace TenantA.Services;

public sealed class TenantBillingService
{
    public Task<IReadOnlyList<string>> FindOverdueInvoicesAsync(string tenantId)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
