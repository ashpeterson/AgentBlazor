namespace AgentBlazor.Core.Runtime.Tools;

public interface IAgentServiceToolRegistry
{
    IReadOnlyList<AgentServiceTool> GetTools();
    bool TryGetTool(string name, out AgentServiceTool tool);
}
