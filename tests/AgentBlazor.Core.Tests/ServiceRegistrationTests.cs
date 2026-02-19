using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using AgentBlazor.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddAgentBlazorServices_RegistersBuiltInDefaultAgent()
    {
        var services = new ServiceCollection();

        services.AddAgentBlazorServices(options =>
        {
            options.Provider.Kind = AgentProviderKind.OpenAI;
            options.Provider.Model = "gpt-4o-mini";
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal("gpt-4o-mini", options.Provider.Model);

        var registry = provider.GetRequiredService<IAgentRegistry>();
        Assert.True(registry.TryGet("AgentBlazor UI Agent", out _));

        var runtime = provider.GetRequiredService<IAgentRuntime>();
        Assert.NotNull(runtime);
        var componentRegistry = provider.GetRequiredService<IAgentComponentRegistry>();
        Assert.NotNull(componentRegistry);

    }

    [Fact]
    public void AgentComponentRegistry_RegisterAndUnregister_Works()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        var component = new StubAgentControllable("supplier-grid");

        registry.Register(component);

        Assert.True(registry.TryGet("supplier-grid", out var resolved));
        Assert.Same(component, resolved);

        Assert.True(registry.Unregister("supplier-grid"));
        Assert.False(registry.TryGet("supplier-grid", out _));
    }

    [Fact]
    public void AddAgent_AndCatalogConfiguration_AreApplied()
    {
        var services = new ServiceCollection();

        services
            .AddAgentBlazorServices()
            .AddAgent("supplier-risk-agent", agent =>
            {
                agent.WithInstructions("Supplier risk agent instructions.");
                agent.WithAllowedComponents("AgentGrid", "AgentStatePanel");
                agent.WithAllowedActions("AgentGrid.filter", "AgentGrid.sort");
            })
            .ConfigureComponentCatalog(catalog => catalog.Enable("AgentGrid", "filter", "sort"));

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAgentRegistry>();
        Assert.True(registry.TryGet("supplier-risk-agent", out var custom));
        Assert.Contains("AgentGrid", custom.AllowedComponents);
        Assert.Contains("AgentGrid.filter", custom.AllowedActions);
        Assert.Contains("AgentGrid.sort", custom.AllowedActions);

        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();
        Assert.True(componentCatalog.TryGet("AgentGrid", out var grid));
        Assert.Contains(grid.Actions, a => a.ActionId.Equals("filter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(grid.Actions, a => a.ActionId.Equals("sort", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultCatalog_ContainsMudBlazorV1Capabilities_WithSchemasAndApprovalFlags()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();

        Assert.Equal("agentblazor.components.v1", AgentComponentV1CapabilityProfile.ProfileId);

        foreach (var componentId in AgentComponentV1CapabilityProfile.ComponentIds)
        {
            Assert.True(componentCatalog.TryGet(componentId, out var capability));
            Assert.All(capability.Actions, action =>
                Assert.False(string.IsNullOrWhiteSpace(action.InputSchema)));
        }

        Assert.True(componentCatalog.TryGet(AgentComponentV1CapabilityProfile.AgentFormComponentId, out var form));
        var submit = Assert.Single(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase));
        Assert.True(submit.RequiresApproval);

        Assert.True(componentCatalog.TryGet(AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, out var nav));
        var navigateExternal = Assert.Single(nav.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase));
        Assert.True(navigateExternal.RequiresApproval);

        Assert.True(componentCatalog.TryGet(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, out var grid));
        var filter = Assert.Single(grid.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase));
        Assert.False(filter.RequiresApproval);
    }

    [Fact]
    public void UseAgentCapabilityPreset_V1Minimal_AddsExpectedSafeSubset()
    {
        var services = new ServiceCollection();
        services
            .AddAgentBlazorServices(options =>
            {
                options.DefaultAgent.ComponentCatalogMode = ComponentCatalogMode.WhitelistOnly;
            })
            .UseAgentCapabilityPreset(AgentCapabilityPreset.V1Minimal);

        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();

        Assert.True(componentCatalog.TryGet(AgentComponentV1CapabilityProfile.AgentFormComponentId, out var form));
        Assert.Contains(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.FormValidateActionId, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase));

        Assert.True(componentCatalog.TryGet(AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, out var nav));
        Assert.Contains(nav.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nav.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UseAgentCapabilityPreset_IsAdditiveWithCustomCatalogOverrides()
    {
        var services = new ServiceCollection();
        services
            .AddAgentBlazorServices(options =>
            {
                options.DefaultAgent.ComponentCatalogMode = ComponentCatalogMode.WhitelistOnly;
            })
            .UseAgentCapabilityPreset(AgentCapabilityPreset.V1Minimal)
            .ConfigureComponentCatalog(catalog => catalog.AddComponent(
                AgentComponentV1CapabilityProfile.AgentFormComponentId,
                "Custom AgentForm override.",
                new ComponentActionCapability(
                    AgentComponentV1CapabilityProfile.FormSubmitActionId,
                    "Submit AgentForm via custom override.",
                    RequiresApproval: true,
                    InputSchema: AgentComponentV1CapabilityProfile.FormSubmitInputSchema)));

        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<IComponentCapabilityCatalog>();

        Assert.True(componentCatalog.TryGet(AgentComponentV1CapabilityProfile.AgentFormComponentId, out var form));
        Assert.Contains(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.FormValidateActionId, StringComparison.OrdinalIgnoreCase));
        var submit = Assert.Single(form.Actions, static a =>
            a.ActionId.Equals(AgentComponentV1CapabilityProfile.FormSubmitActionId, StringComparison.OrdinalIgnoreCase));
        Assert.True(submit.RequiresApproval);
    }

    [Fact]
    public void ComponentActionPolicy_EvaluateAllowedCapabilities_TracksBlockedActionKeys()
    {
        var catalog = DefaultShippedComponents.CreateCatalog();
        var allowedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AgentComponentV1CapabilityProfile.AgentDialogComponentId
        };
        var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{AgentComponentV1CapabilityProfile.AgentDialogComponentId}.{AgentComponentV1CapabilityProfile.DialogOpenActionId}"
        };

        var evaluation = ComponentActionPolicy.EvaluateAllowedCapabilities(
            catalog.GetComponents(),
            allowedComponents,
            allowedActions);

        Assert.True(evaluation.HasAllowedActions);
        Assert.Single(evaluation.AllowedComponents);

        var dialog = Assert.Single(evaluation.AllowedComponents);
        Assert.Equal(AgentComponentV1CapabilityProfile.AgentDialogComponentId, dialog.ComponentId);
        var onlyAction = Assert.Single(dialog.Actions);
        Assert.Equal(AgentComponentV1CapabilityProfile.DialogOpenActionId, onlyAction.ActionId, ignoreCase: true);

        Assert.Contains(
            $"{AgentComponentV1CapabilityProfile.AgentDialogComponentId}.{AgentComponentV1CapabilityProfile.DialogCloseActionId}",
            evaluation.BlockedActionKeys);
        Assert.Contains(
            $"{AgentComponentV1CapabilityProfile.AgentFormComponentId}.{AgentComponentV1CapabilityProfile.FormValidateActionId}",
            evaluation.BlockedActionKeys);
    }

    [Fact]
    public void AgentComponentTierBoundaries_MapExpectedActionTiers()
    {
        Assert.Equal(
            AgentBlazorTier.Free,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridFilterActionId));

        Assert.Equal(
            AgentBlazorTier.Paid,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridSetPageActionId));

        Assert.Equal(
            AgentBlazorTier.Premium,
            AgentComponentTierBoundaries.GetRequiredTier(
                AgentComponentV1CapabilityProfile.AgentFormComponentId,
                AgentComponentV1CapabilityProfile.FormSubmitActionId));
    }

    [Fact]
    public async Task Runtime_RequiresFrameworkProvider_WhenNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest("open the chat widget"));

        Assert.Contains("No provider is configured", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(response.PlannedActions);
    }

    [Fact]
    public async Task AddAgentBlazorServices_RegistersTelemetrySink_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var telemetrySink = provider.GetRequiredService<IAgentBlazorTelemetrySink>();

        await telemetrySink.TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
        {
            Kind = AgentBlazorRunEventKind.Started,
            Source = AgentBlazorTelemetrySources.Runtime,
            AgentName = "test-agent"
        });

        Assert.NotNull(telemetrySink);
    }

    [Fact]
    public async Task AddAgentBlazorServices_RegistersAgentActionExecutors_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var dataGridExecutor = provider.GetRequiredService<IDataGridActionExecutor>();
        var dialogExecutor = provider.GetRequiredService<IDialogActionExecutor>();
        var formExecutor = provider.GetRequiredService<IFormActionExecutor>();
        var navigationExecutor = provider.GetRequiredService<INavigationActionExecutor>();
        var tabsExecutor = provider.GetRequiredService<ITabsActionExecutor>();

        var dataGridResult = await dataGridExecutor.ExecuteAsync(new DataGridActionRequest("filter"));
        var dialogResult = await dialogExecutor.ExecuteAsync(new DialogActionRequest("open"));
        var formResult = await formExecutor.ExecuteAsync(new FormActionRequest("validate"));
        var navigationResult = await navigationExecutor.ExecuteAsync(new NavigationActionRequest("navigate_to"));
        var tabsResult = await tabsExecutor.ExecuteAsync(new TabsActionRequest("switch_tab"));

        Assert.Equal(AgentComponentV1CapabilityProfile.AgentDataGridComponentId, dataGridResult.ComponentId);
        Assert.Equal(AgentComponentV1CapabilityProfile.AgentDialogComponentId, dialogResult.ComponentId);
        Assert.Equal(AgentComponentV1CapabilityProfile.AgentFormComponentId, formResult.ComponentId);
        Assert.Equal(AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, navigationResult.ComponentId);
        Assert.Equal(AgentComponentV1CapabilityProfile.AgentTabsComponentId, tabsResult.ComponentId);
    }

    [Fact]
    public async Task AddAgentBlazorServices_AllowsReplacingAgentActionExecutors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataGridActionExecutor, StubDataGridActionExecutor>();
        services.AddSingleton<IDialogActionExecutor, StubDialogActionExecutor>();
        services.AddSingleton<IFormActionExecutor, StubFormActionExecutor>();
        services.AddSingleton<INavigationActionExecutor, StubNavigationActionExecutor>();
        services.AddSingleton<ITabsActionExecutor, StubTabsActionExecutor>();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();

        var dataGridResult = await provider.GetRequiredService<IDataGridActionExecutor>()
            .ExecuteAsync(new DataGridActionRequest("filter"));
        var dialogResult = await provider.GetRequiredService<IDialogActionExecutor>()
            .ExecuteAsync(new DialogActionRequest("open"));
        var formResult = await provider.GetRequiredService<IFormActionExecutor>()
            .ExecuteAsync(new FormActionRequest("validate"));
        var navigationResult = await provider.GetRequiredService<INavigationActionExecutor>()
            .ExecuteAsync(new NavigationActionRequest("navigate_to"));
        var tabsResult = await provider.GetRequiredService<ITabsActionExecutor>()
            .ExecuteAsync(new TabsActionRequest("switch_tab"));

        Assert.Equal("custom-grid", dataGridResult.Message);
        Assert.Equal("custom-dialog", dialogResult.Message);
        Assert.Equal("custom-form", formResult.Message);
        Assert.Equal("custom-nav", navigationResult.Message);
        Assert.Equal("custom-tabs", tabsResult.Message);
    }

    [Fact]
    public async Task ComponentActionExecutor_DispatchesKnownMudActions_ToTypedExecutors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataGridActionExecutor, StubDataGridActionExecutor>();
        services.AddSingleton<IDialogActionExecutor, StubDialogActionExecutor>();
        services.AddSingleton<IFormActionExecutor, StubFormActionExecutor>();
        services.AddSingleton<INavigationActionExecutor, StubNavigationActionExecutor>();
        services.AddSingleton<ITabsActionExecutor, StubTabsActionExecutor>();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IComponentActionExecutor>();

        var grid = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
            AgentComponentV1CapabilityProfile.DataGridFilterActionId,
            "test"));
        var dialog = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentDialogComponentId,
            AgentComponentV1CapabilityProfile.DialogOpenActionId,
            "test"));
        var form = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentFormComponentId,
            AgentComponentV1CapabilityProfile.FormValidateActionId,
            "test"));
        var nav = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
            AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
            "test"));
        var tabs = await executor.ExecuteAsync(new PlannedComponentAction(
            AgentComponentV1CapabilityProfile.AgentTabsComponentId,
            AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
            "test"));

        Assert.Equal("custom-grid", grid.Message);
        Assert.Equal("custom-dialog", dialog.Message);
        Assert.Equal("custom-form", form.Message);
        Assert.Equal("custom-nav", nav.Message);
        Assert.Equal("custom-tabs", tabs.Message);
    }

    [Fact]
    public async Task ComponentActionExecutor_ExecutesAgentChatWidgetActions_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IComponentActionExecutor>();

        var result = await executor.ExecuteAsync(new PlannedComponentAction(
            "AgentChatWidget",
            "open_widget",
            "test"));
        var chatState = provider.GetRequiredService<IAgentChatWidgetState>();

        Assert.True(result.Succeeded);
        Assert.Equal("AgentChatWidget", result.ComponentId);
        Assert.Equal("open_widget", result.ActionId);
        Assert.Contains("Opened chat widget", result.Message, StringComparison.Ordinal);
        Assert.True(chatState.IsOpen);
    }

    [Fact]
    public async Task ComponentActionExecutor_ReturnsSafeFailure_ForUnknownActionMapping()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IComponentActionExecutor>();

        var result = await executor.ExecuteAsync(new PlannedComponentAction(
            "UnknownComponent",
            "unknown_action",
            "test"));

        Assert.False(result.Succeeded);
        Assert.Contains("No executor mapping is registered", result.Message, StringComparison.Ordinal);
        Assert.Contains("UnknownComponent.unknown_action", result.Message, StringComparison.Ordinal);
    }

    private sealed class StubDataGridActionExecutor : IDataGridActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            DataGridActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-grid"));
        }
    }

    private sealed class StubDialogActionExecutor : IDialogActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            DialogActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentV1CapabilityProfile.AgentDialogComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-dialog"));
        }
    }

    private sealed class StubFormActionExecutor : IFormActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            FormActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentV1CapabilityProfile.AgentFormComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-form"));
        }
    }

    private sealed class StubNavigationActionExecutor : INavigationActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            NavigationActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-nav"));
        }
    }

    private sealed class StubTabsActionExecutor : ITabsActionExecutor
    {
        public Task<ComponentActionExecutionResult> ExecuteAsync(
            TabsActionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ComponentActionExecutionResult(
                AgentComponentV1CapabilityProfile.AgentTabsComponentId,
                "stub",
                Succeeded: true,
                Message: "custom-tabs"));
        }
    }

    private sealed class StubAgentControllable(string agentId) : IAgentControllable
    {
        public string AgentId { get; } = agentId;

        public string ComponentType => "Stub";

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Stub component");
            capability.UpsertAction(new ComponentActionCapability("noop", "No-op action."));
            return capability;
        }

        public ComponentState GetCurrentState() => new();

        public Task<ActionResult> ExecuteActionAsync(
            AgentAction action,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(ActionResult.Success($"Handled {action.Name}."));
        }
    }
}
