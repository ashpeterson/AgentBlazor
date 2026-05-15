namespace AgentBlazor.Core.Data;

public sealed class InMemoryAgentDataSchemaCatalog : IAgentDataSchemaCatalog
{
    private readonly Dictionary<string, AgentDataSchemaSet> _schemas = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryAgentDataSchemaCatalog(IEnumerable<AgentDataSchemaSet>? schemas = null)
    {
        foreach (var schema in schemas ?? [])
        {
            AddOrUpdate(schema);
        }
    }

    public void AddOrUpdate(AgentDataSchemaSet schemaSet)
    {
        ArgumentNullException.ThrowIfNull(schemaSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaSet.Name);

        _schemas[schemaSet.Name] = schemaSet;
    }

    public IReadOnlyList<AgentDataSchemaSet> GetAll() => [.. _schemas.Values];

    public IReadOnlyList<AgentDataSchemaSet> GetAllowed(IEnumerable<string> schemaNames)
    {
        ArgumentNullException.ThrowIfNull(schemaNames);

        var allowed = new List<AgentDataSchemaSet>();
        foreach (var name in schemaNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && _schemas.TryGetValue(name, out var schema))
            {
                allowed.Add(schema);
            }
        }

        return allowed;
    }

    public bool TryGet(string name, out AgentDataSchemaSet schemaSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
#pragma warning disable CS8601
        return _schemas.TryGetValue(name, out schemaSet);
#pragma warning restore CS8601
    }
}
