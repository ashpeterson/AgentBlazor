namespace AgentBlazor.Core.Paid;

public record ActionHistoryEntry(
    string SessionId,
    string? UserId,
    DateTimeOffset Timestamp,
    string UserMessage,
    string ActionId,
    string AgentId,
    IReadOnlyDictionary<string, object?> Args,
    // Execution metrics (for analytics)
    bool Succeeded = true,
    TimeSpan? Duration = null,
    string? Route = null,
    string? ErrorMessage = null);
