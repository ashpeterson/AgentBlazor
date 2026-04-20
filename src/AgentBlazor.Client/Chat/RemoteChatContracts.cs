namespace AgentBlazor.Client.Chat;

internal sealed record RemoteChatRunRequest(
    string UserMessage,
    string? AgentName,
    string SessionId,
    string? UserId,
    IReadOnlyDictionary<string, string>? Context);

internal sealed record RemoteChatRunResponse(
    string AgentName,
    string ResponseText,
    bool RequiresClarification,
    string? ClarificationQuestion,
    bool RequiresApproval,
    int PendingApprovalCount);
