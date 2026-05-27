public sealed class AuditBundleService
{
    public Task SummarizeAuditBundleAsync(string bundleId)
        => Task.CompletedTask;

    public Task PrepareRemoteBundleAsync(string bundleId)
        => Task.CompletedTask;
}
