namespace AgentBlazor.Core.Runtime.Tools;

/// <summary>Options for configuring an MCP server connection.</summary>
public sealed class McpServerOptions
{
    /// <summary>Only expose tools whose names are in this list. If null, all tools are included.</summary>
    public IReadOnlyList<string>? IncludeTools { get; set; }

    /// <summary>Exclude tools whose names are in this list. Applied after IncludeTools.</summary>
    public IReadOnlyList<string>? ExcludeTools { get; set; }
}
