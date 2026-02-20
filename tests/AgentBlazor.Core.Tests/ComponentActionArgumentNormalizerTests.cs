using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class ComponentActionArgumentNormalizerTests
{
    [Fact]
    public void Normalize_UnwrapsJsonElements_WithoutChangingSchemaKeys()
    {
        using var document = JsonDocument.Parse("""
            {
              "column": "RiskScore",
              "operator": ">=",
              "value": 70,
              "active": true,
              "notes": null
            }
            """);
        var root = document.RootElement;
        var arguments = new Dictionary<string, object?>
        {
            ["column"] = root.GetProperty("column"),
            ["operator"] = root.GetProperty("operator"),
            ["value"] = root.GetProperty("value"),
            ["active"] = root.GetProperty("active"),
            ["notes"] = root.GetProperty("notes")
        };

        var normalized = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            arguments,
            reason: "show me all suppliers that are high risk");

        Assert.Equal("RiskScore", normalized["column"]?.ToString());
        Assert.Equal(">=", normalized["operator"]?.ToString());
        Assert.Equal(70, Assert.IsType<int>(normalized["value"]));
        Assert.True(Assert.IsType<bool>(normalized["active"]));
        Assert.Null(normalized["notes"]);
    }

    [Fact]
    public void Normalize_AppliesCanonicalAliasesAcrossComponents()
    {
        var nav = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
            new Dictionary<string, object?>
            {
                ["route"] = "/supplier-onboarding",
                ["replaceHistory"] = "true"
            },
            reason: "open supplier onboarding");

        Assert.Equal("/supplier-onboarding", nav["uri"]?.ToString());
        Assert.True(Assert.IsType<bool>(nav["replaceHistory"]));

        var gridFilter = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            new Dictionary<string, object?>
            {
                ["field"] = "RiskScore",
                ["comparison"] = ">=",
                ["fieldValue"] = 70
            });

        Assert.Equal("RiskScore", gridFilter["column"]?.ToString());
        Assert.Equal(">=", gridFilter["operator"]?.ToString());
        Assert.Equal(70, Assert.IsType<int>(gridFilter["value"]));

        var formSetField = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentFormComponentId,
            AgentComponentV1CapabilityProfile.FormSetFieldActionId,
            new Dictionary<string, object?>
            {
                ["name"] = "SupplierName",
                ["input"] = "Contoso"
            });

        Assert.Equal("SupplierName", formSetField["field"]?.ToString());
        Assert.Equal("Contoso", formSetField["value"]?.ToString());

        var tabs = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
            new Dictionary<string, object?>
            {
                ["tab"] = "second"
            });

        Assert.Equal(1, Assert.IsType<int>(tabs["index"]));
    }

    [Fact]
    public void Normalize_DataGridSort_PromotesOperatorDirectionAlias()
    {
        var normalized = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridSortActionId,
            new Dictionary<string, object?>
            {
                ["field"] = "RiskScore",
                ["operator"] = "descending"
            });

        Assert.Equal("RiskScore", normalized["column"]?.ToString());
        Assert.Equal("desc", normalized["direction"]?.ToString());
    }

    [Fact]
    public async Task ComponentActionExecutor_UnwrapsJsonArguments_BeforeDispatch()
    {
        using var document = JsonDocument.Parse("""
            {
              "column": "RiskScore",
              "operator": ">=",
              "value": 70
            }
            """);
        var root = document.RootElement;

        var services = new ServiceCollection();
        services.AddSingleton<CapturingDataGridExecutor>();
        services.AddSingleton<IDataGridActionExecutor>(sp => sp.GetRequiredService<CapturingDataGridExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var componentExecutor = provider.GetRequiredService<IComponentActionExecutor>();
        var capturing = provider.GetRequiredService<CapturingDataGridExecutor>();

        var result = await componentExecutor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            "normalize json only",
            new Dictionary<string, object?>
            {
                ["column"] = root.GetProperty("column"),
                ["operator"] = root.GetProperty("operator"),
                ["value"] = root.GetProperty("value")
            }));

        Assert.True(result.Succeeded);
        Assert.NotNull(capturing.LastRequest);
        Assert.Equal("RiskScore", capturing.LastRequest!.Arguments!["column"]?.ToString());
        Assert.Equal(">=", capturing.LastRequest.Arguments!["operator"]?.ToString());
        Assert.Equal(70, Assert.IsType<int>(capturing.LastRequest.Arguments!["value"]));
    }

    private sealed class CapturingDataGridExecutor : IDataGridActionExecutor
    {
        public DataGridActionRequest? LastRequest { get; private set; }

        public Task<ComponentActionExecutionResult> ExecuteAsync(
            DataGridActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                request.ActionId,
                Succeeded: true,
                Message: "captured"));
        }
    }
}
