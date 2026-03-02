namespace AgentBlazor.Core.Models;

public record ActionRenderContext(
    string AgentId,
    string ActionId,
    ActionStatus Status,
    IReadOnlyDictionary<string, object?> Args,
    object? Result);
