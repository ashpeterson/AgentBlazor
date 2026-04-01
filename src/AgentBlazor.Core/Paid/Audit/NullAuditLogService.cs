namespace AgentBlazor.Core.Paid.Audit;

/// <summary>
/// No-op audit log service for free tier.
/// Silently ignores all audit events.
/// </summary>
public sealed class NullAuditLogService : IAuditLogService
{
    public static NullAuditLogService Instance { get; } = new();

    public Task LogAsync(AuditEvent evt, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task LogActionAsync(
        string userId,
        string? userEmail,
        string actionId,
        string agentId,
        bool succeeded,
        string? errorMessage = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    public Task<IReadOnlyList<AuditEvent>> GetByUserAsync(string userId, int limit = 100, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    public Task<Stream> ExportAsync(AuditQuery query, AuditExportFormat format, CancellationToken ct = default)
    {
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task PruneAsync(int retentionDays = 365, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
