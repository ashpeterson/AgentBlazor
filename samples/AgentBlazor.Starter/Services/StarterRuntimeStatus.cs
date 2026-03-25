namespace AgentBlazor.Starter.Services;

public sealed record StarterRuntimeStatus(
    string ProviderLabel,
    string ConversationStorePath,
    string SharedStateStorePath,
    bool DevToolsEnabled);
