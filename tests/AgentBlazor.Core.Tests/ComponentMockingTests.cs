using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Licensing;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Tests demonstrating how to mock components to verify actions are actually applied.
/// </summary>
public class ComponentMockingTests
{
    private const string BuiltInAgentName = "AgentBlazor UI Agent";

    [Fact]
    public async Task DataGrid_Filter_UpdatesComponentState()
    {
        // Arrange
        var mockGrid = new MockDataGrid("supplier-grid");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskLevel",
                ["operator"] = "eq",
                ["value"] = "High",
                ["target"] = "supplier-grid"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockGrid);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("filter by high risk", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.Single(mockGrid.ExecutedActions);
        Assert.Equal("filter", mockGrid.ExecutedActions[0].Name);
        Assert.Equal("RiskLevel", mockGrid.CurrentFilter?.Column);
        Assert.Equal("eq", mockGrid.CurrentFilter?.Operator);
        Assert.Equal("High", mockGrid.CurrentFilter?.Value);
    }

    [Fact]
    public async Task DataGrid_Sort_UpdatesComponentState()
    {
        // Arrange
        var mockGrid = new MockDataGrid("supplier-grid");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdatagrid_sort",
            new Dictionary<string, object?>
            {
                ["column"] = "RiskScore",
                ["direction"] = "desc",
                ["target"] = "supplier-grid"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockGrid);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("sort by risk score descending", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.Single(mockGrid.ExecutedActions);
        Assert.Equal("sort", mockGrid.ExecutedActions[0].Name);
        Assert.Equal("RiskScore", mockGrid.SortColumn);
        Assert.Equal("desc", mockGrid.SortDirection);
    }

    [Fact]
    public async Task DataGrid_GoToPage_UpdatesCurrentPage()
    {
        // Arrange
        var mockGrid = new MockDataGrid("supplier-grid");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdatagrid_go_to_page",
            new Dictionary<string, object?>
            {
                ["page"] = 3,
                ["target"] = "supplier-grid"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();
        services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockGrid);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("go to page 3", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.Equal(3, mockGrid.CurrentPage);
    }

    [Fact]
    public async Task DataGrid_ClearFilters_ResetsFilterState()
    {
        // Arrange
        var mockGrid = new MockDataGrid("supplier-grid");
        mockGrid.CurrentFilter = new FilterState("RiskLevel", "eq", "High");

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdatagrid_clear_filters",
            new Dictionary<string, object?>
            {
                ["target"] = "supplier-grid"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockGrid);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("clear all filters", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.Null(mockGrid.CurrentFilter);
    }

    [Fact]
    public async Task Dialog_Open_UpdatesOpenState()
    {
        // Arrange
        var mockDialog = new MockDialog("confirm-dialog");
        Assert.False(mockDialog.IsOpen);

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdialog_open",
            new Dictionary<string, object?>
            {
                ["target"] = "confirm-dialog"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockDialog);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("open the dialog", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.True(mockDialog.IsOpen);
        Assert.Single(mockDialog.ExecutedActions);
        Assert.Equal("open", mockDialog.ExecutedActions[0].Name);
    }

    [Fact]
    public async Task Dialog_Close_UpdatesOpenState()
    {
        // Arrange
        var mockDialog = new MockDialog("confirm-dialog") { IsOpen = true };
        Assert.True(mockDialog.IsOpen);

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdialog_close",
            new Dictionary<string, object?>
            {
                ["target"] = "confirm-dialog"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockDialog);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("close the dialog", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.False(mockDialog.IsOpen);
    }

    [Fact]
    public async Task Form_SetField_UpdatesFieldValue()
    {
        // Arrange
        var mockForm = new MockForm("supplier-form");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentform_set_field",
            new Dictionary<string, object?>
            {
                ["field"] = "supplierName",
                ["value"] = "Northwind Traders",
                ["target"] = "supplier-form"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockForm);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("set supplier name to Northwind Traders", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.Equal("Northwind Traders", mockForm.GetFieldValue("supplierName"));
        Assert.Single(mockForm.ExecutedActions);
    }

    [Fact]
    public async Task Form_SetMultipleFields_TracksAllChanges()
    {
        // Arrange
        var mockForm = new MockForm("supplier-form");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MultiFieldFormChatClient());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockForm);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act - simulate setting two fields
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("set supplier name to Acme and risk level to Low", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert - verify both actions were tracked
        Assert.Equal(2, mockForm.ExecutedActions.Count);
        Assert.Equal("Acme Corp", mockForm.GetFieldValue("supplierName"));
        Assert.Equal("Low", mockForm.GetFieldValue("riskLevel"));
    }

    [Fact]
    public async Task NavMenu_NavigateTo_UpdatesCurrentUri()
    {
        // Arrange
        var mockNav = new MockNavMenu("main-nav");
        Assert.Null(mockNav.CurrentUri);

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentnavmenu_navigate_to",
            new Dictionary<string, object?>
            {
                ["uri"] = "/suppliers",
                ["target"] = "main-nav"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockNav);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("go to suppliers page", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert
        Assert.Equal("/suppliers", mockNav.CurrentUri);
        Assert.Single(mockNav.NavigationHistory);
    }

    [Fact]
    public async Task Tabs_SwitchTab_ReceivesAction()
    {
        // Arrange - Tabs actions are queued when no component is registered
        // This test verifies the action flows through the system
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agenttabs_switch_tab",
            new Dictionary<string, object?>
            {
                ["index"] = 2
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        var response = await runtime.RunTurnAsync(new AgentTurnRequest("switch to the third tab", AgentName: BuiltInAgentName));

        // Assert - action was planned
        Assert.True(
            response.ExecutionPlan?.Steps.Any(step =>
                string.Equals(step.TargetId, "AgentTabs", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(step.ActionId, "switch_tab", StringComparison.OrdinalIgnoreCase)) is true ||
            response.LegacyPlannedActions.Any(a =>
                string.Equals(a.ComponentId, "AgentTabs", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ActionId, "switch_tab", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task MultipleComponents_WithoutAgentId_ReturnsNeedsClarification()
    {
        // Arrange - when multiple components of the same type are registered,
        // execution must not pick one implicitly without an explicit agentId.
        var grid1 = new MockDataGrid("alpha-grid");
        var grid2 = new MockDataGrid("beta-grid");

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "Status",
                ["operator"] = "eq",
                ["value"] = "Active"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(grid1);
        registry.Register(grid2);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        // Act
        var response = await runtime.RunTurnAsync(new AgentTurnRequest("filter by active status", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        Assert.Contains("Specify 'agentId' to target one", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(grid1.ExecutedActions);
        Assert.Empty(grid2.ExecutedActions);
    }

    [Fact]
    public async Task Component_StateIsAvailableInTrace_WhenTracingEnabled()
    {
        // Arrange
        var mockGrid = new MockDataGrid("traced-grid");
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new MockToolChatClient(
            "ui_agentdatagrid_filter",
            new Dictionary<string, object?>
            {
                ["column"] = "Category",
                ["operator"] = "contains",
                ["value"] = "Electronics",
                ["target"] = "traced-grid"
            }));
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddBuiltInUiAgent()
            .EnablePromptTracing();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentComponentRegistry>();
        registry.Register(mockGrid);

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var traceStore = provider.GetRequiredService<IPromptTraceStore>();

        // Act
        _ = await runtime.RunTurnAsync(new AgentTurnRequest("filter by electronics", AgentName: BuiltInAgentName, SessionId: registry.SessionId));

        // Assert - verify trace captured the execution
        var traces = await traceStore.GetRecentAsync(1);
        var trace = Assert.Single(traces);
        Assert.NotNull(trace.Execution);
        Assert.Contains(trace.Execution.Results, r =>
            r.ComponentId == "AgentDataGrid" &&
            r.ActionId == "filter" &&
            r.Succeeded);

        // And verify the component state was updated
        Assert.Equal("Category", mockGrid.CurrentFilter?.Column);
    }

    #region Mock Components

    public sealed record FilterState(string Column, string Operator, string? Value);

    public sealed class MockDataGrid : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];

        public MockDataGrid(string agentId)
        {
            AgentId = agentId;
        }

        public string AgentId { get; }
        public string ComponentType => "DataGrid";

        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;

        // Tracked state
        public FilterState? CurrentFilter { get; set; }
        public string? SortColumn { get; private set; }
        public string? SortDirection { get; private set; }
        public int CurrentPage { get; private set; } = 1;
        public int? SelectedRowIndex { get; private set; }

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Mock DataGrid for testing");
            capability.UpsertAction(new ComponentActionCapability("filter", "Filter data"));
            capability.UpsertAction(new ComponentActionCapability("sort", "Sort data"));
            capability.UpsertAction(new ComponentActionCapability("clear_filters", "Clear all filters"));
            capability.UpsertAction(new ComponentActionCapability("go_to_page", "Navigate to page"));
            capability.UpsertAction(new ComponentActionCapability("select_row", "Select a row"));
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            return new ComponentState
            {
                ["filterColumn"] = CurrentFilter?.Column,
                ["filterOperator"] = CurrentFilter?.Operator,
                ["filterValue"] = CurrentFilter?.Value,
                ["sortColumn"] = SortColumn,
                ["sortDirection"] = SortDirection,
                ["currentPage"] = CurrentPage,
                ["selectedRowIndex"] = SelectedRowIndex
            };
        }

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken cancellationToken = default)
        {
            _executedActions.Add(action);

            switch (action.Name.ToLowerInvariant())
            {
                case "filter":
                    var column = GetStringParam(action, "column");
                    var op = GetStringParam(action, "operator") ?? "eq";
                    var value = GetStringParam(action, "value");
                    CurrentFilter = new FilterState(column ?? "unknown", op, value);
                    return Task.FromResult(ActionResult.Success($"Filtered by {column} {op} {value}"));

                case "sort":
                    SortColumn = GetStringParam(action, "column");
                    SortDirection = GetStringParam(action, "direction") ?? "asc";
                    return Task.FromResult(ActionResult.Success($"Sorted by {SortColumn} {SortDirection}"));

                case "clear_filters":
                    CurrentFilter = null;
                    return Task.FromResult(ActionResult.Success("Filters cleared"));

                case "go_to_page":
                    if (action.TryGetParameter<int>("page", out var page) ||
                        action.TryGetParameter<int>("pageIndex", out page))
                    {
                        CurrentPage = page;
                    }
                    return Task.FromResult(ActionResult.Success($"Navigated to page {CurrentPage}"));

                case "select_row":
                    if (action.TryGetParameter<int>("row_index", out var rowIndex))
                    {
                        SelectedRowIndex = rowIndex;
                    }
                    else if (action.TryGetParameter<string>("rowKey", out var rowKey) &&
                             int.TryParse(rowKey, out rowIndex))
                    {
                        SelectedRowIndex = rowIndex;
                    }
                    return Task.FromResult(ActionResult.Success($"Selected row {SelectedRowIndex}"));

                default:
                    return Task.FromResult(ActionResult.Unknown(action.Name));
            }
        }

        private static string? GetStringParam(AgentAction action, string key)
        {
            if (action.Parameters.TryGetValue(key, out var value) && value is not null)
            {
                return value.ToString();
            }
            return null;
        }
    }

    public sealed class MockDialog : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];

        public MockDialog(string agentId)
        {
            AgentId = agentId;
        }

        public string AgentId { get; }
        public string ComponentType => "Dialog";

        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;

        // Tracked state
        public bool IsOpen { get; set; }
        public bool WasConfirmed { get; private set; }

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Mock Dialog for testing");
            capability.UpsertAction(new ComponentActionCapability("open", "Open dialog"));
            capability.UpsertAction(new ComponentActionCapability("close", "Close dialog"));
            capability.UpsertAction(new ComponentActionCapability("confirm", "Confirm dialog action"));
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            return new ComponentState
            {
                ["isOpen"] = IsOpen,
                ["wasConfirmed"] = WasConfirmed
            };
        }

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken cancellationToken = default)
        {
            _executedActions.Add(action);

            switch (action.Name.ToLowerInvariant())
            {
                case "open":
                    IsOpen = true;
                    return Task.FromResult(ActionResult.Success("Dialog opened"));

                case "close":
                    IsOpen = false;
                    return Task.FromResult(ActionResult.Success("Dialog closed"));

                case "confirm":
                    WasConfirmed = true;
                    IsOpen = false;
                    return Task.FromResult(ActionResult.Success("Dialog confirmed"));

                default:
                    return Task.FromResult(ActionResult.Unknown(action.Name));
            }
        }
    }

    public sealed class MockForm : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];
        private readonly ConcurrentDictionary<string, object?> _fieldValues = new(StringComparer.OrdinalIgnoreCase);

        public MockForm(string agentId)
        {
            AgentId = agentId;
        }

        public string AgentId { get; }
        public string ComponentType => "Form";

        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;

        public object? GetFieldValue(string fieldName) =>
            _fieldValues.TryGetValue(fieldName, out var value) ? value : null;

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Mock Form for testing");
            capability.UpsertAction(new ComponentActionCapability("set_field", "Set field value"));
            capability.UpsertAction(new ComponentActionCapability("validate", "Validate form"));
            capability.UpsertAction(new ComponentActionCapability("submit", "Submit form"));
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            var state = new ComponentState();
            foreach (var kvp in _fieldValues)
            {
                state[kvp.Key] = kvp.Value;
            }
            return state;
        }

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken cancellationToken = default)
        {
            _executedActions.Add(action);

            switch (action.Name.ToLowerInvariant())
            {
                case "set_field":
                    if (action.Parameters.TryGetValue("field", out var fieldName) &&
                        action.Parameters.TryGetValue("value", out var fieldValue))
                    {
                        _fieldValues[fieldName?.ToString() ?? ""] = fieldValue;
                        return Task.FromResult(ActionResult.Success($"Set {fieldName} to {fieldValue}"));
                    }
                    return Task.FromResult(ActionResult.Failure("Missing field or value parameter"));

                case "validate":
                    return Task.FromResult(ActionResult.Success("Form is valid"));

                case "submit":
                    return Task.FromResult(ActionResult.Success("Form submitted"));

                default:
                    return Task.FromResult(ActionResult.Unknown(action.Name));
            }
        }
    }

    public sealed class MockNavMenu : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];
        private readonly List<string> _navigationHistory = [];

        public MockNavMenu(string agentId)
        {
            AgentId = agentId;
        }

        public string AgentId { get; }
        public string ComponentType => "NavMenu";

        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;
        public IReadOnlyList<string> NavigationHistory => _navigationHistory;

        // Tracked state
        public string? CurrentUri { get; private set; }

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Mock NavMenu for testing");
            capability.UpsertAction(new ComponentActionCapability("navigate_to", "Navigate to URI"));
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            return new ComponentState
            {
                ["currentUri"] = CurrentUri,
                ["navigationCount"] = _navigationHistory.Count
            };
        }

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken cancellationToken = default)
        {
            _executedActions.Add(action);

            switch (action.Name.ToLowerInvariant())
            {
                case "navigate_to":
                    if (action.Parameters.TryGetValue("uri", out var uri))
                    {
                        CurrentUri = uri?.ToString();
                        if (CurrentUri != null)
                        {
                            _navigationHistory.Add(CurrentUri);
                        }
                        return Task.FromResult(ActionResult.Success($"Navigated to {CurrentUri}"));
                    }
                    return Task.FromResult(ActionResult.Failure("Missing uri parameter"));

                default:
                    return Task.FromResult(ActionResult.Unknown(action.Name));
            }
        }
    }

    public sealed class MockTabs : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];
        private readonly int _tabCount;

        public MockTabs(string agentId, int tabCount = 3)
        {
            AgentId = agentId;
            _tabCount = tabCount;
        }

        public string AgentId { get; }
        public string ComponentType => "Tabs";

        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;

        // Tracked state
        public int ActiveTabIndex { get; private set; }

        public ComponentCapability GetCapability()
        {
            var capability = new ComponentCapability(AgentId, "Mock Tabs for testing");
            capability.UpsertAction(new ComponentActionCapability("switch_tab", "Switch to tab"));
            return capability;
        }

        public ComponentState GetCurrentState()
        {
            return new ComponentState
            {
                ["activeTabIndex"] = ActiveTabIndex,
                ["tabCount"] = _tabCount
            };
        }

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken cancellationToken = default)
        {
            _executedActions.Add(action);

            switch (action.Name.ToLowerInvariant())
            {
                case "switch_tab":
                    if (action.TryGetParameter<int>("tab_index", out var tabIndex) ||
                        action.TryGetParameter<int>("index", out tabIndex))
                    {
                        if (tabIndex < 0 || tabIndex >= _tabCount)
                        {
                            return Task.FromResult(ActionResult.Failure($"Tab index {tabIndex} out of range (0-{_tabCount - 1})"));
                        }
                        ActiveTabIndex = tabIndex;
                        return Task.FromResult(ActionResult.Success($"Switched to tab {tabIndex}"));
                    }
                    return Task.FromResult(ActionResult.Failure("Missing tab_index parameter"));

                default:
                    return Task.FromResult(ActionResult.Unknown(action.Name));
            }
        }
    }

    #endregion

    #region Mock Chat Clients

    private sealed class MockToolChatClient(
        string functionName,
        IDictionary<string, object?>? arguments = null) : IChatClient
    {
        private readonly IDictionary<string, object?> _arguments = arguments ?? new Dictionary<string, object?>();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(function =>
                    string.Equals(function.Name, functionName, StringComparison.OrdinalIgnoreCase)) ??
                []);

            await tool.InvokeAsync(new AIFunctionArguments(_arguments), cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Executed {functionName}"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class MultiFieldFormChatClient : IChatClient
    {
        private static readonly IReadOnlyList<(string ToolName, Dictionary<string, object?> Arguments)> Invocations =
        [
            ("ui_agentform_set_field", new Dictionary<string, object?>
            {
                ["field"] = "supplierName",
                ["value"] = "Acme Corp",
                ["target"] = "supplier-form"
            }),
            ("ui_agentform_set_field", new Dictionary<string, object?>
            {
                ["field"] = "riskLevel",
                ["value"] = "Low",
                ["target"] = "supplier-form"
            })
        ];

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;

            foreach (var invocation in Invocations)
            {
                var tool = Assert.Single(
                    options?.Tools?.OfType<AIFunction>().Where(function =>
                        string.Equals(function.Name, invocation.ToolName, StringComparison.OrdinalIgnoreCase)) ??
                    []);

                await tool.InvokeAsync(new AIFunctionArguments(invocation.Arguments), cancellationToken);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Executed multiple form actions"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    #endregion
}

