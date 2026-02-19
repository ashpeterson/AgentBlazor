using AgentBlazor.Components;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class ComponentActionArgumentNormalizerTests
{
    [Fact]
    public void Normalize_DataGridFilterAliases_MapsCanonicalKeys()
    {
        var normalized = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            new Dictionary<string, object?>
            {
                ["field"] = "RiskScore",
                ["comparison"] = "highest",
                ["threshold"] = 70
            });

        Assert.Equal("RiskScore", normalized["column"]?.ToString());
        Assert.Equal(">=", normalized["operator"]?.ToString());
        Assert.Equal(70L, Convert.ToInt64(normalized["value"]));
    }

    [Fact]
    public void Normalize_FormNavigationAndTabsAliases_MapsCanonicalKeys()
    {
        var form = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentFormComponentId,
            AgentComponentV1CapabilityProfile.FormSetFieldActionId,
            new Dictionary<string, object?>
            {
                ["name"] = "SupplierName",
                ["fieldValue"] = "Contoso"
            });
        Assert.Equal("SupplierName", form["field"]?.ToString());
        Assert.Equal("Contoso", form["value"]?.ToString());

        var nav = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
            new Dictionary<string, object?>
            {
                ["route"] = "/suppliers"
            });
        Assert.Equal("/suppliers", nav["uri"]?.ToString());

        var tabs = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
            new Dictionary<string, object?>
            {
                ["tab"] = "2"
            });
        Assert.Equal(2, Assert.IsType<int>(tabs["index"]));
    }

    [Fact]
    public void Normalize_DataGridFilterFromIntent_InfersRiskParameters()
    {
        var highest = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            arguments: null,
            reason: "filter by highest risk supplier");

        Assert.Equal("RiskScore", highest["column"]?.ToString());
        Assert.Equal(">=", highest["operator"]?.ToString());
        Assert.Equal(70, Assert.IsType<int>(highest["value"]));

        var lowest = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            arguments: null,
            reason: "filter by lowest risk supplier");

        Assert.Equal("RiskScore", lowest["column"]?.ToString());
        Assert.Equal("<=", lowest["operator"]?.ToString());
        Assert.Equal(30, Assert.IsType<int>(lowest["value"]));
    }

    [Fact]
    public void Normalize_DataGridSort_UsesStateHints_WhenColumnMissing()
    {
        var normalized = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridSortActionId,
            new Dictionary<string, object?>
            {
                ["currentFilterColumn"] = "RiskScore"
            },
            reason: "now sort from highest to lowest");

        Assert.Equal("RiskScore", normalized["column"]?.ToString());
        Assert.Equal("desc", normalized["direction"]?.ToString());
    }

    [Fact]
    public void Normalize_NavigationAndTabsFromIntent_InfersRequiredParameters()
    {
        var nav = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
            arguments: null,
            reason: "go to suppliers");
        Assert.Equal("/suppliers", nav["uri"]?.ToString());

        var tabs = ComponentActionArgumentNormalizer.Normalize(
            AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
            arguments: null,
            reason: "switch to second tab");
        Assert.Equal(1, Assert.IsType<int>(tabs["index"]));
    }

    [Fact]
    public async Task ComponentActionExecutor_NormalizesArguments_BeforeDispatch()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapturingDataGridExecutor>();
        services.AddSingleton<IDataGridActionExecutor>(sp => sp.GetRequiredService<CapturingDataGridExecutor>());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var componentExecutor = provider.GetRequiredService<IComponentActionExecutor>();
        var capturing = provider.GetRequiredService<CapturingDataGridExecutor>();

        var aliasResult = await componentExecutor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            "normalize aliases",
            new Dictionary<string, object?>
            {
                ["field"] = "RiskScore",
                ["comparison"] = "high",
                ["threshold"] = "70"
            }));

        Assert.True(aliasResult.Succeeded);
        Assert.NotNull(capturing.LastRequest);
        Assert.Equal("RiskScore", capturing.LastRequest!.Arguments!["column"]?.ToString());
        Assert.Equal(">=", capturing.LastRequest.Arguments!["operator"]?.ToString());
        Assert.Equal("70", capturing.LastRequest.Arguments!["value"]?.ToString());

        var intentResult = await componentExecutor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            "filter by lowest risk supplier"));

        Assert.True(intentResult.Succeeded);
        Assert.NotNull(capturing.LastRequest);
        Assert.Equal("RiskScore", capturing.LastRequest!.Arguments!["column"]?.ToString());
        Assert.Equal("<=", capturing.LastRequest.Arguments!["operator"]?.ToString());
        Assert.Equal(30, Assert.IsType<int>(capturing.LastRequest.Arguments!["value"]));
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
