namespace AgentBlazor.Core.Runtime.Tools;

public sealed class InMemoryAgentServiceToolRegistry : IAgentServiceToolRegistry
{
    private readonly Dictionary<string, AgentServiceTool> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(AgentServiceTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
    }

    public IReadOnlyList<AgentServiceTool> GetTools() => [.. _tools.Values];

    public bool TryGetTool(string name, out AgentServiceTool tool)
    {
#pragma warning disable CS8601
        return _tools.TryGetValue(name, out tool);
#pragma warning restore CS8601
    }
}
