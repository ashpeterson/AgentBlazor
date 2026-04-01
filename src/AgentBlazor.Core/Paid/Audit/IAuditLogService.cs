namespace AgentBlazor.Core.Paid.Audit;

/// <summary>
/// Audit logging service for Pro/Enterprise tiers.
/// Provides compliance-ready activity tracking with query and export capabilities.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Logs an audit event.
    /// </summary>
    Task LogAsync(AuditEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Logs an action execution audit event.
    /// </summary>
    Task LogActionAsync(
        string userId,
        string? userEmail,
        string actionId,
        string agentId,
        bool succeeded,
        string? errorMessage = null,
        string? ipAddress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Queries audit events based on filter criteria.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default);

    /// <summary>
    /// Gets audit events for a specific user.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetByUserAsync(string userId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Gets recent audit events.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Exports audit events matching the query to the specified format.
    /// </summary>
    Task<Stream> ExportAsync(AuditQuery query, AuditExportFormat format, CancellationToken ct = default);

    /// <summary>
    /// Prunes old audit events based on retention policy.
    /// </summary>
    Task PruneAsync(int retentionDays = 365, CancellationToken ct = default);
}
