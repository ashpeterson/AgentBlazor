namespace AgentBlazor.Core.Paid.Audit;

/// <summary>
/// An auditable event in the system.
/// </summary>
public record AuditEvent(
    string Id,
    DateTimeOffset Timestamp,
    string UserId,
    string? UserEmail,
    AuditEventType EventType,
    string TargetType,
    string TargetId,
    string Description,
    string? IpAddress,
    IReadOnlyDictionary<string, object?>? Metadata = null);

/// <summary>
/// Types of auditable events.
/// </summary>
public enum AuditEventType
{
    ActionExecuted,
    ActionApprovalRequested,
    ActionApproved,
    ActionDenied,
    ActionFailed,
    ConfigChanged,
    SessionStarted,
    SessionEnded,
    UserAuthenticated,
    ApiKeyUsed
}

/// <summary>
/// Query parameters for filtering audit events.
/// </summary>
public record AuditQuery
{
    public string? UserId { get; init; }
    public string? UserEmail { get; init; }
    public AuditEventType? EventType { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; } = 0;
}

/// <summary>
/// Export format for audit logs.
/// </summary>
public enum AuditExportFormat
{
    Json,
    Csv
}
