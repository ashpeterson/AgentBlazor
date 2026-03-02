namespace AgentBlazor.Core.Runtime.Tools;

/// <summary>
/// Provides tools sourced from an MCP (Model Context Protocol) server.
/// Tools are converted to <see cref="AgentServiceTool"/> records compatible with the agent's tool dispatch.
/// </summary>
public interface IMcpToolProvider
{
    Task<IReadOnlyList<AgentServiceTool>> GetToolsAsync(CancellationToken ct = default);
}
