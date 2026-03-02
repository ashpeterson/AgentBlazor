using AgentBlazor.Core.Runtime.Tools;

namespace AgentBlazor.Core.Tests;

file sealed class NoOpServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}

/// <summary>
/// Tests for Part 2: Service Tool Registration.
/// </summary>
public class ServiceToolTests
{
    [Fact]
    public void InMemoryRegistry_StoresAndRetrievesTool()
    {
        var registry = new InMemoryAgentServiceToolRegistry();
        var tool = new AgentServiceTool(
            Name: "count_suppliers",
            Description: "Count suppliers",
            Parameters: [],
            Handler: (_, _, _) => Task.FromResult("42"));

        registry.Register(tool);

        Assert.True(registry.TryGetTool("count_suppliers", out var found));
        Assert.Equal("count_suppliers", found.Name);
        Assert.Equal("Count suppliers", found.Description);
    }

    [Fact]
    public void InMemoryRegistry_GetTools_ReturnsAllRegistered()
    {
        var registry = new InMemoryAgentServiceToolRegistry();
        registry.Register(new AgentServiceTool("tool_a", "A", [], (_, _, _) => Task.FromResult("a")));
        registry.Register(new AgentServiceTool("tool_b", "B", [], (_, _, _) => Task.FromResult("b")));

        var tools = registry.GetTools();

        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public void InMemoryRegistry_TryGetTool_ReturnsFalseForUnknown()
    {
        var registry = new InMemoryAgentServiceToolRegistry();

        var found = registry.TryGetTool("nonexistent", out _);

        Assert.False(found);
    }

    [Fact]
    public async Task ToolHandler_IsCalledWithCorrectArgs()
    {
        var capturedArgs = new Dictionary<string, object?>();
        var tool = new AgentServiceTool(
            Name: "echo",
            Description: "Echo args",
            Parameters: [new AgentToolParameter("msg", "The message")],
            Handler: (args, _, _) =>
            {
                foreach (var kv in args) capturedArgs[kv.Key] = kv.Value;
                return Task.FromResult("ok");
            });

        var args = new Dictionary<string, object?> { ["msg"] = "hello" };
        var sp = new NoOpServiceProvider();
        var result = await tool.Handler(args, sp, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal("hello", capturedArgs["msg"]);
    }

    [Fact]
    public void AgentToolParameter_DefaultsToStringRequired()
    {
        var param = new AgentToolParameter("name", "desc");

        Assert.Equal("string", param.Type);
        Assert.True(param.Required);
    }

    [Fact]
    public void InMemoryRegistry_RegisterOverwritesDuplicate()
    {
        var registry = new InMemoryAgentServiceToolRegistry();
        registry.Register(new AgentServiceTool("dup", "first", [], (_, _, _) => Task.FromResult("first")));
        registry.Register(new AgentServiceTool("dup", "second", [], (_, _, _) => Task.FromResult("second")));

        var tools = registry.GetTools();
        Assert.Single(tools);
        Assert.Equal("second", tools[0].Description);
    }
}
