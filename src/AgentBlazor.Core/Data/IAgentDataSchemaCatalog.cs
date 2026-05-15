namespace AgentBlazor.Core.Data;

public interface IAgentDataSchemaCatalog
{
    IReadOnlyList<AgentDataSchemaSet> GetAll();

    IReadOnlyList<AgentDataSchemaSet> GetAllowed(IEnumerable<string> schemaNames);

    bool TryGet(string name, out AgentDataSchemaSet schemaSet);
}
