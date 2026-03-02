namespace AgentBlazor.Core.Runtime.Tools;

/// <summary>Default no-op MCP tool provider used when no MCP server is configured.</summary>
internal sealed class NullMcpToolProvider : IMcpToolProvider
{
    public Task<IReadOnlyList<AgentServiceTool>> GetToolsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentServiceTool>>([]);
}
