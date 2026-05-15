namespace AgentBlazor.Core.Data;

public sealed record AgentDataSchemaSet
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<AgentEntitySchema> Entities { get; init; } = [];
}

public sealed record AgentEntitySchema
{
    public required string Name { get; init; }

    public string? ClrTypeName { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<AgentEntityPropertySchema> Properties { get; init; } = [];
}

public sealed record AgentEntityPropertySchema
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool IsNullable { get; init; }

    public bool IsKey { get; init; }

    public string? Description { get; init; }
}
