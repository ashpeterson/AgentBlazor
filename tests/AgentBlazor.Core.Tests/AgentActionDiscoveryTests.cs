using System.Text.Json;
using AgentBlazor.Attributes;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Discovery;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for AgentActionDiscovery: AvailableWhen gating (1a) and array/list parameter support (1b).
/// </summary>
public class AgentActionDiscoveryTests
{
    // -------------------------------------------------------------------------
    // 1a: AvailableWhen gating
    // -------------------------------------------------------------------------

    [Fact]
    public void GetDiscoveredActions_WhenAvailabilityPropertyIsFalse_ExcludesAction()
    {
        var component = new GatedComponent("gated-1") { CanDelete = false };

        var actions = AgentActionDiscovery.GetDiscoveredActions(component);

        Assert.DoesNotContain(actions, a => a.ActionId == "delete_item");
        Assert.Contains(actions, a => a.ActionId == "always_available");
    }

    [Fact]
    public void GetDiscoveredActions_WhenAvailabilityPropertyIsTrue_IncludesAction()
    {
        var component = new GatedComponent("gated-2") { CanDelete = true };

        var actions = AgentActionDiscovery.GetDiscoveredActions(component);

        Assert.Contains(actions, a => a.ActionId == "delete_item");
        Assert.Contains(actions, a => a.ActionId == "always_available");
    }

    [Fact]
    public void GetDiscoveredActions_WhenAvailabilityMethodReturnsFalse_ExcludesAction()
    {
        var component = new MethodGatedComponent("method-gated-1") { IsApproved = false };

        var actions = AgentActionDiscovery.GetDiscoveredActions(component);

        Assert.DoesNotContain(actions, a => a.ActionId == "approve_action");
    }

    [Fact]
    public void GetDiscoveredActions_WhenAvailabilityMethodReturnsTrue_IncludesAction()
    {
        var component = new MethodGatedComponent("method-gated-2") { IsApproved = true };

        var actions = AgentActionDiscovery.GetDiscoveredActions(component);

        Assert.Contains(actions, a => a.ActionId == "approve_action");
    }

    [Fact]
    public void GetDiscoveredActions_AvailabilityIsReEvaluatedEachCall()
    {
        var component = new GatedComponent("gated-dynamic") { CanDelete = false };

        var before = AgentActionDiscovery.GetDiscoveredActions(component);
        Assert.DoesNotContain(before, a => a.ActionId == "delete_item");

        component.CanDelete = true;

        var after = AgentActionDiscovery.GetDiscoveredActions(component);
        Assert.Contains(after, a => a.ActionId == "delete_item");
    }

    [Fact]
    public void BuildCapability_WhenAvailabilityIsFalse_ExcludesActionFromCapability()
    {
        var component = new GatedComponent("cap-gated") { CanDelete = false };

        var capability = AgentActionDiscovery.BuildCapability(component);

        Assert.DoesNotContain(capability.Actions, a => a.ActionId == "delete_item");
        Assert.Contains(capability.Actions, a => a.ActionId == "always_available");
    }

    // -------------------------------------------------------------------------
    // 1b: Array / List parameter type support
    // -------------------------------------------------------------------------

    [Fact]
    public void GetDiscoveredActions_StringArrayParam_ShowsArrayOfStringType()
    {
        var component = new ArrayParamComponent("array-comp");

        var actions = AgentActionDiscovery.GetDiscoveredActions(component);
        var action = Assert.Single(actions, a => a.ActionId == "tag_items");
        var param = Assert.Single(action.Parameters, p => p.Name == "tags");

        Assert.Equal("array of string", param.Type);
    }

    [Fact]
    public void GetDiscoveredActions_ListIntParam_ShowsArrayOfIntegerType()
    {
        var component = new ArrayParamComponent("array-comp-2");

        var actions = AgentActionDiscovery.GetDiscoveredActions(component);
        var action = Assert.Single(actions, a => a.ActionId == "select_ids");
        var param = Assert.Single(action.Parameters, p => p.Name == "ids");

        Assert.Equal("array of integer", param.Type);
    }

    [Fact]
    public async Task ExecuteActionAsync_StringArrayParam_CoercesJsonElementArray()
    {
        var component = new ArrayParamComponent("array-exec");
        var jsonArray = JsonSerializer.Deserialize<JsonElement>("""["alpha","beta","gamma"]""");

        var action = AgentAction.Create("tag_items", new Dictionary<string, object?>
        {
            ["tags"] = jsonArray
        });

        var result = await AgentActionDiscovery.ExecuteActionAsync(component, action);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(component.LastTags);
        Assert.Equal(["alpha", "beta", "gamma"], component.LastTags);
    }

    [Fact]
    public async Task ExecuteActionAsync_ListIntParam_CoercesJsonElementArray()
    {
        var component = new ArrayParamComponent("array-exec-int");
        var jsonArray = JsonSerializer.Deserialize<JsonElement>("""[1,2,3]""");

        var action = AgentAction.Create("select_ids", new Dictionary<string, object?>
        {
            ["ids"] = jsonArray
        });

        var result = await AgentActionDiscovery.ExecuteActionAsync(component, action);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(component.LastIds);
        Assert.Equal([1, 2, 3], component.LastIds);
    }

    [Fact]
    public async Task ExecuteActionAsync_StringArrayParam_CoercesFromRawList()
    {
        // Simulates when NormalizeArgs has already unwrapped JsonElement → List<object?>
        var component = new ArrayParamComponent("array-raw");

        var action = AgentAction.Create("tag_items", new Dictionary<string, object?>
        {
            ["tags"] = new List<object?> { "x", "y" }
        });

        var result = await AgentActionDiscovery.ExecuteActionAsync(component, action);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["x", "y"], component.LastTags);
    }

    // -------------------------------------------------------------------------
    // Test components
    // -------------------------------------------------------------------------

    private sealed class GatedComponent(string agentId) : IAgentControllable
    {
        public string AgentId { get; } = agentId;
        public string ComponentType => "GatedComponent";

        public bool CanDelete { get; set; }

        [AgentAction("Delete item", AvailableWhen = nameof(CanDelete))]
        public Task DeleteItem() => Task.CompletedTask;

        [AgentAction("Always available action")]
        public Task AlwaysAvailable() => Task.CompletedTask;

        public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);
        public ComponentState GetCurrentState() => new();
        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
            => AgentActionDiscovery.ExecuteActionAsync(this, action, ct);
    }

    private sealed class MethodGatedComponent(string agentId) : IAgentControllable
    {
        public string AgentId { get; } = agentId;
        public string ComponentType => "MethodGatedComponent";

        public bool IsApproved { get; set; }

        private bool CanApprove() => IsApproved;

        [AgentAction("Approve the action", AvailableWhen = nameof(CanApprove))]
        public Task ApproveAction() => Task.CompletedTask;

        public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);
        public ComponentState GetCurrentState() => new();
        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
            => AgentActionDiscovery.ExecuteActionAsync(this, action, ct);
    }

    private sealed class ArrayParamComponent(string agentId) : IAgentControllable
    {
        public string AgentId { get; } = agentId;
        public string ComponentType => "ArrayParamComponent";

        public string[]? LastTags { get; private set; }
        public List<int>? LastIds { get; private set; }

        [AgentAction("Tag items with labels")]
        public Task TagItems([AgentParam("Comma-separated tags")] string[] tags)
        {
            LastTags = tags;
            return Task.CompletedTask;
        }

        [AgentAction("Select items by ID")]
        public Task SelectIds([AgentParam("List of item IDs")] List<int> ids)
        {
            LastIds = ids;
            return Task.CompletedTask;
        }

        public ComponentCapability GetCapability() => AgentActionDiscovery.BuildCapability(this);
        public ComponentState GetCurrentState() => new();
        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
            => AgentActionDiscovery.ExecuteActionAsync(this, action, ct);
    }
}
