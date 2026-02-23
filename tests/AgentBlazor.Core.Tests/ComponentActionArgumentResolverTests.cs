using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests for deterministic column resolution by discovery (reflection-derived columns + token overlap).
/// No app-configured aliases are required for LLM-style names like "riskLevel" to resolve to "RiskScore".
/// </summary>
public class ComponentActionArgumentResolverTests
{
    private static ComponentState StateWithColumnsOnly(string[] columns)
    {
        var state = new ComponentState();
        state[ComponentActionArgumentResolver.StateKeyColumns] = columns;
        return state;
    }

    private static ComponentState StateWithColumnsAndValueMappings(
        string[] columns,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> valueMappings)
    {
        var state = StateWithColumnsOnly(columns);
        state[ComponentActionArgumentResolver.StateKeyValueMappings] = valueMappings;
        return state;
    }

    [Fact]
    public void Resolve_Column_ExactMatch_WhenHintMatchesDiscoveredColumn()
    {
        var resolver = new ComponentActionArgumentResolver();
        var state = StateWithColumnsOnly(["SupplierId", "SupplierName", "Region", "RiskScore", "LastAuditDate"]);
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = "RiskScore",
            ["operator"] = "eq",
            ["value"] = "high"
        };

        var result = resolver.Resolve(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            args,
            state);

        Assert.Equal("RiskScore", result["column"]);
    }

    [Fact]
    public void Resolve_Column_DiscoveryMatch_WhenHintIsLLMStyleName_NoAliasesRequired()
    {
        var resolver = new ComponentActionArgumentResolver();
        var state = StateWithColumnsOnly(["SupplierId", "SupplierName", "Region", "RiskScore", "LastAuditDate"]);
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = "riskLevel",
            ["operator"] = "eq",
            ["value"] = "high"
        };

        var result = resolver.Resolve(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            args,
            state);

        Assert.Equal("RiskScore", result["column"]);
    }

    [Fact]
    public void Resolve_Column_DiscoveryMatch_WhenHintIsRisk_Underscore_Level_Static()
    {
        var state = StateWithColumnsOnly(["SupplierId", "SupplierName", "Region", "RiskScore", "LastAuditDate"]);
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = "risk_level",
            ["operator"] = "eq",
            ["value"] = "high"
        };

        var result = ComponentActionArgumentResolver.ResolveArguments(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            args,
            state);

        Assert.Equal("RiskScore", result["column"]);
    }

    [Fact]
    public void Resolve_Column_DiscoveryMatch_Deterministic_SameHintSameColumns_SameResult()
    {
        var resolver = new ComponentActionArgumentResolver();
        var state = StateWithColumnsOnly(["SupplierId", "RiskScore", "LastAuditDate"]);
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = "risk_level",
            ["operator"] = "eq",
            ["value"] = "x"
        };

        var result1 = resolver.Resolve(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            args,
            state);

        var result2 = resolver.Resolve(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            args,
            state);

        Assert.Equal("RiskScore", result1["column"]);
        Assert.Equal(result1["column"], result2["column"]);
    }

    [Fact]
    public void Resolve_Filter_ValueMapping_Applied_WhenColumnResolvedByDiscovery()
    {
        var resolver = new ComponentActionArgumentResolver();
        var valueMappings = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["RiskScore"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["High"] = 70,
                ["Medium"] = 50,
                ["Low"] = 30
            }
        };
        var state = StateWithColumnsAndValueMappings(
            ["SupplierId", "SupplierName", "Region", "RiskScore", "LastAuditDate"],
            valueMappings);
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["column"] = "riskLevel",
            ["operator"] = "eq",
            ["value"] = "high"
        };

        var result = resolver.Resolve(
            AgentComponentCapabilityProfile.AgentDataGridComponentId,
            AgentComponentCapabilityProfile.DataGridFilterActionId,
            args,
            state);

        Assert.Equal("RiskScore", result["column"]);
        Assert.Equal(70, result["value"]);
    }
}
