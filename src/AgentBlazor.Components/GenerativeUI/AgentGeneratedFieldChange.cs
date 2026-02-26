namespace AgentBlazor.Components;

public sealed record AgentGeneratedFieldChange(
    string BlockId,
    string FieldName,
    string? Value);
