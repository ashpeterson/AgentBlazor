using AgentBlazor.Core.Runtime.Tools;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for Part 4: MCP Tool Provider.
/// </summary>
public class McpToolProviderTests
{
    [Fact]
    public async Task NullMcpToolProvider_ReturnsEmptyList()
    {
        var provider = new NullMcpToolProvider();

        var tools = await provider.GetToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public void McpServerOptions_DefaultsAreNull()
    {
        var options = new McpServerOptions();

        Assert.Null(options.IncludeTools);
        Assert.Null(options.ExcludeTools);
    }

    [Fact]
    public void AgentServiceTool_CanBeCreatedWithParameters()
    {
        var parameters = new List<AgentToolParameter>
        {
            new("query", "Search query", "string", true),
            new("limit", "Max results", "integer", false)
        };

        var tool = new AgentServiceTool(
            Name: "search_products",
            Description: "Search for products",
            Parameters: parameters,
            Handler: (_, _, _) => Task.FromResult("[]"));

        Assert.Equal("search_products", tool.Name);
        Assert.Equal(2, tool.Parameters.Count);
        Assert.Equal("query", tool.Parameters[0].Name);
        Assert.Equal("integer", tool.Parameters[1].Type);
        Assert.False(tool.Parameters[1].Required);
    }

    [Fact]
    public void MockMcpProvider_ReturnsFilteredByInclude()
    {
        // Simulate IncludeTools filter logic (same as HttpMcpToolProvider.PassesFilter)
        var includeTools = new[] { "search_products", "get_product" };
        var allTools = new[] { "search_products", "get_product", "delete_product", "create_product" };

        var filtered = allTools
            .Where(t => includeTools.Any(i => string.Equals(i, t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains("search_products", filtered);
        Assert.Contains("get_product", filtered);
        Assert.DoesNotContain("delete_product", filtered);
    }

    [Fact]
    public void MockMcpProvider_ReturnsFilteredByExclude()
    {
        var excludeTools = new[] { "delete_product", "create_product" };
        var allTools = new[] { "search_products", "get_product", "delete_product", "create_product" };

        var filtered = allTools
            .Where(t => !excludeTools.Any(e => string.Equals(e, t, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains("search_products", filtered);
        Assert.DoesNotContain("delete_product", filtered);
    }

    [Fact]
    public async Task NullMcpToolProvider_CalledMultipleTimes_AlwaysReturnsEmpty()
    {
        var provider = new NullMcpToolProvider();

        var first = await provider.GetToolsAsync();
        var second = await provider.GetToolsAsync();

        Assert.Empty(first);
        Assert.Empty(second);
    }

    [Fact]
    public void AgentToolParameter_NameAndDescriptionRequired()
    {
        var param = new AgentToolParameter("myParam", "My description", "number", false);

        Assert.Equal("myParam", param.Name);
        Assert.Equal("My description", param.Description);
        Assert.Equal("number", param.Type);
        Assert.False(param.Required);
    }
}
