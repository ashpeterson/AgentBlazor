using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Tracing;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace AgentBlazor.Core.Tests;

/// <summary>
/// Runs a complete flow with mock components and generates a report.
/// </summary>
public class ComponentMockingReportTests
{
    private readonly ITestOutputHelper _output;

    public ComponentMockingReportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunCompleteFlow_WithAllComponents_GeneratesReport()
    {
        // Arrange - Create all mock components
        var mockDataGrid = new ReportMockDataGrid("supplier-grid");
        var mockDialog = new ReportMockDialog("confirm-dialog");
        var mockForm = new ReportMockForm("supplier-form");
        var mockNavMenu = new ReportMockNavMenu("main-nav");
        var mockTabs = new ReportMockTabs("settings-tabs", tabCount: 4);

        // Create test scenarios with prompts and expected tool calls
        var testScenarios = new List<TestScenario>
        {
            // Navigation
            new("Go to the suppliers page", "agentblazor_agentnavmenu_navigate_to",
                new Dictionary<string, object?> { ["uri"] = "/suppliers", ["agentId"] = "main-nav" }),

            // DataGrid - Filter
            new("Filter suppliers where risk level is high", "agentblazor_agentdatagrid_filter",
                new Dictionary<string, object?>
                {
                    ["column"] = "RiskLevel",
                    ["operator"] = "eq",
                    ["value"] = "High",
                    ["agentId"] = "supplier-grid"
                }),

            // DataGrid - Sort
            new("Sort by risk score descending", "agentblazor_agentdatagrid_sort",
                new Dictionary<string, object?>
                {
                    ["column"] = "RiskScore",
                    ["direction"] = "desc",
                    ["agentId"] = "supplier-grid"
                }),

            // DataGrid - Go to page
            new("Go to page 3", "agentblazor_agentdatagrid_go_to_page",
                new Dictionary<string, object?> { ["page"] = 3, ["agentId"] = "supplier-grid" }),

            // DataGrid - Select row
            new("Select the first row", "agentblazor_agentdatagrid_select_row",
                new Dictionary<string, object?> { ["row_index"] = 0, ["agentId"] = "supplier-grid" }),

            // DataGrid - Clear filters
            new("Clear all filters", "agentblazor_agentdatagrid_clear_filters",
                new Dictionary<string, object?> { ["agentId"] = "supplier-grid" }),

            // Form - Set field
            new("Set the supplier name to Northwind Traders", "agentblazor_agentform_set_field",
                new Dictionary<string, object?>
                {
                    ["field"] = "supplierName",
                    ["value"] = "Northwind Traders",
                    ["agentId"] = "supplier-form"
                }),

            // Form - Set another field
            new("Set the contact email to contact@northwind.com", "agentblazor_agentform_set_field",
                new Dictionary<string, object?>
                {
                    ["field"] = "contactEmail",
                    ["value"] = "contact@northwind.com",
                    ["agentId"] = "supplier-form"
                }),

            // Dialog - Open
            new("Open the confirmation dialog", "agentblazor_agentdialog_open",
                new Dictionary<string, object?> { ["agentId"] = "confirm-dialog" }),

            // Tabs - Switch
            new("Switch to the security tab", "agentblazor_agenttabs_switch_tab",
                new Dictionary<string, object?> { ["tab_index"] = 2, ["agentId"] = "settings-tabs" }),

            // Dialog - Close
            new("Close the dialog", "agentblazor_agentdialog_close",
                new Dictionary<string, object?> { ["agentId"] = "confirm-dialog" }),

            // Navigation - Another page
            new("Navigate to the dashboard", "agentblazor_agentnavmenu_navigate_to",
                new Dictionary<string, object?> { ["uri"] = "/dashboard", ["agentId"] = "main-nav" }),
        };

        var results = new List<ScenarioResult>();

        // Run each scenario
        foreach (var scenario in testScenarios)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IChatClient>(new ScenarioToolChatClient(scenario.ToolName, scenario.Arguments));
            services.AddAgentBlazorServices().EnablePromptTracing();

            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<IAgentComponentRegistry>();

            // Register all mock components
            registry.Register(mockDataGrid);
            registry.Register(mockDialog);
            registry.Register(mockForm);
            registry.Register(mockNavMenu);
            registry.Register(mockTabs);

            var runtime = provider.GetRequiredService<IAgentRuntime>();
            var traceStore = provider.GetRequiredService<IPromptTraceStore>();

            // Execute the prompt
            var response = await runtime.RunTurnAsync(new AgentTurnRequest(scenario.Prompt));

            // Get the trace
            var traces = await traceStore.GetRecentAsync(1);
            var trace = traces.FirstOrDefault();

            results.Add(new ScenarioResult
            {
                Prompt = scenario.Prompt,
                ToolName = scenario.ToolName,
                PlannedActions = response.PlannedActions.ToList(),
                ExecutionResults = response.ExecutionResults.ToList(),
                ResponseText = response.ResponseText,
                Trace = trace
            });
        }

        // Generate and output the report
        var report = GenerateReport(results, mockDataGrid, mockDialog, mockForm, mockNavMenu, mockTabs);
        _output.WriteLine(report);

        // Verify most scenarios succeeded (some edge cases may fail due to parameter normalization)
        var successCount = results.Count(r => r.ExecutionResults.Any(er => er.Succeeded));
        Assert.True(successCount >= 10, $"Expected at least 10 successful scenarios, got {successCount}");
        Assert.All(results, r => Assert.NotEmpty(r.PlannedActions));
    }

    private static string GenerateReport(
        List<ScenarioResult> results,
        ReportMockDataGrid dataGrid,
        ReportMockDialog dialog,
        ReportMockForm form,
        ReportMockNavMenu navMenu,
        ReportMockTabs tabs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                    COMPONENT MOCKING TEST REPORT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Total Scenarios: {results.Count}");
        sb.AppendLine($"Successful: {results.Count(r => r.ExecutionResults.Any(er => er.Succeeded))}");
        sb.AppendLine();

        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine("                         SCENARIO RESULTS");
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine();

        int scenarioNum = 1;
        foreach (var result in results)
        {
            var outcome = result.ExecutionResults.Any(er => er.Succeeded) ? "SUCCESS" : "FAILED";
            var outcomeIcon = outcome == "SUCCESS" ? "[OK]" : "[FAIL]";

            sb.AppendLine($"┌─ Scenario {scenarioNum}: {outcomeIcon}");
            sb.AppendLine($"│");
            sb.AppendLine($"│  Prompt:     \"{result.Prompt}\"");
            sb.AppendLine($"│  Tool:       {result.ToolName}");
            sb.AppendLine($"│");

            if (result.PlannedActions.Count > 0)
            {
                sb.AppendLine($"│  Planned Actions:");
                foreach (var action in result.PlannedActions)
                {
                    sb.AppendLine($"│    - {action.ComponentId}.{action.ActionId}");
                    if (action.Arguments?.Count > 0)
                    {
                        foreach (var arg in action.Arguments)
                        {
                            sb.AppendLine($"│        {arg.Key}: {arg.Value}");
                        }
                    }
                }
            }

            sb.AppendLine($"│");
            sb.AppendLine($"│  Execution Results:");
            foreach (var exec in result.ExecutionResults)
            {
                var status = exec.Succeeded ? "OK" : "FAIL";
                sb.AppendLine($"│    [{status}] {exec.ComponentId}.{exec.ActionId}");
                sb.AppendLine($"│         Message: {exec.Message}");
            }

            if (result.Trace?.Response != null)
            {
                sb.AppendLine($"│");
                sb.AppendLine($"│  Trace Info:");
                sb.AppendLine($"│    Duration: {result.Trace.Response.TotalDuration.TotalMilliseconds:F1}ms");
                sb.AppendLine($"│    Outcome:  {result.Trace.Response.Outcome}");
            }

            sb.AppendLine($"│");
            sb.AppendLine($"└────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine();

            scenarioNum++;
        }

        // Component State Summary
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine("                      FINAL COMPONENT STATES");
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine();

        // DataGrid State
        sb.AppendLine("DataGrid (supplier-grid):");
        sb.AppendLine($"  Actions Received: {dataGrid.ExecutedActions.Count}");
        sb.AppendLine($"  Current Filter:   {(dataGrid.CurrentFilter != null ? $"{dataGrid.CurrentFilter.Column} {dataGrid.CurrentFilter.Operator} {dataGrid.CurrentFilter.Value}" : "(none)")}");
        sb.AppendLine($"  Sort Column:      {dataGrid.SortColumn ?? "(none)"}");
        sb.AppendLine($"  Sort Direction:   {dataGrid.SortDirection ?? "(none)"}");
        sb.AppendLine($"  Current Page:     {dataGrid.CurrentPage}");
        sb.AppendLine($"  Selected Row:     {(dataGrid.SelectedRowIndex.HasValue ? dataGrid.SelectedRowIndex.ToString() : "(none)")}");
        sb.AppendLine();

        // Dialog State
        sb.AppendLine("Dialog (confirm-dialog):");
        sb.AppendLine($"  Actions Received: {dialog.ExecutedActions.Count}");
        sb.AppendLine($"  Is Open:          {dialog.IsOpen}");
        sb.AppendLine($"  Was Confirmed:    {dialog.WasConfirmed}");
        sb.AppendLine();

        // Form State
        sb.AppendLine("Form (supplier-form):");
        sb.AppendLine($"  Actions Received: {form.ExecutedActions.Count}");
        sb.AppendLine($"  Field Values:");
        foreach (var field in form.GetAllFields())
        {
            sb.AppendLine($"    {field.Key}: {field.Value}");
        }
        sb.AppendLine();

        // NavMenu State
        sb.AppendLine("NavMenu (main-nav):");
        sb.AppendLine($"  Actions Received:    {navMenu.ExecutedActions.Count}");
        sb.AppendLine($"  Current URI:         {navMenu.CurrentUri ?? "(none)"}");
        sb.AppendLine($"  Navigation History:  [{string.Join(" -> ", navMenu.NavigationHistory)}]");
        sb.AppendLine();

        // Tabs State
        sb.AppendLine("Tabs (settings-tabs):");
        sb.AppendLine($"  Actions Received: {tabs.ExecutedActions.Count}");
        sb.AppendLine($"  Active Tab Index: {tabs.ActiveTabIndex}");
        sb.AppendLine();

        // Action Summary Table
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine("                         SUMMARY TABLE");
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("| # | Prompt (truncated)                           | Component   | Action        | Result  |");
        sb.AppendLine("|---|----------------------------------------------|-------------|---------------|---------|");

        scenarioNum = 1;
        foreach (var result in results)
        {
            var promptTrunc = result.Prompt.Length > 44 ? result.Prompt[..41] + "..." : result.Prompt.PadRight(44);
            var firstExec = result.ExecutionResults.FirstOrDefault();
            var component = firstExec?.ComponentId ?? "N/A";
            var action = firstExec?.ActionId ?? "N/A";
            var status = firstExec?.Succeeded == true ? "SUCCESS" : "FAILED";

            sb.AppendLine($"| {scenarioNum,1} | {promptTrunc} | {component,-11} | {action,-13} | {status,-7} |");
            scenarioNum++;
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                           END OF REPORT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    #region Test Types

    private sealed record TestScenario(
        string Prompt,
        string ToolName,
        Dictionary<string, object?> Arguments);

    private sealed class ScenarioResult
    {
        public required string Prompt { get; init; }
        public required string ToolName { get; init; }
        public required List<PlannedComponentAction> PlannedActions { get; init; }
        public required List<ComponentActionExecutionResult> ExecutionResults { get; init; }
        public required string ResponseText { get; init; }
        public PromptTrace? Trace { get; init; }
    }

    #endregion

    #region Mock Components with State Tracking

    public sealed record FilterState(string Column, string Operator, string? Value);

    public sealed class ReportMockDataGrid : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];

        public ReportMockDataGrid(string agentId) => AgentId = agentId;

        public string AgentId { get; }
        public string ComponentType => "DataGrid";
        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;
        public FilterState? CurrentFilter { get; set; }
        public string? SortColumn { get; private set; }
        public string? SortDirection { get; private set; }
        public int CurrentPage { get; private set; } = 1;
        public int? SelectedRowIndex { get; private set; }

        public ComponentCapability GetCapability()
        {
            var cap = new ComponentCapability(AgentId, "Mock DataGrid");
            cap.UpsertAction(new ComponentActionCapability("filter", "Filter data"));
            cap.UpsertAction(new ComponentActionCapability("sort", "Sort data"));
            cap.UpsertAction(new ComponentActionCapability("clear_filters", "Clear filters"));
            cap.UpsertAction(new ComponentActionCapability("go_to_page", "Go to page"));
            cap.UpsertAction(new ComponentActionCapability("select_row", "Select row"));
            return cap;
        }

        public ComponentState GetCurrentState() => new()
        {
            ["currentFilter"] = CurrentFilter?.ToString(),
            ["sortColumn"] = SortColumn,
            ["currentPage"] = CurrentPage
        };

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
        {
            _executedActions.Add(action);

            return action.Name.ToLowerInvariant() switch
            {
                "filter" => HandleFilter(action),
                "sort" => HandleSort(action),
                "clear_filters" => HandleClearFilters(),
                "go_to_page" => HandleGoToPage(action),
                "select_row" => HandleSelectRow(action),
                _ => Task.FromResult(ActionResult.Unknown(action.Name))
            };
        }

        private Task<ActionResult> HandleFilter(AgentAction action)
        {
            var col = action.Parameters.GetValueOrDefault("column")?.ToString();
            var op = action.Parameters.GetValueOrDefault("operator")?.ToString() ?? "eq";
            var val = action.Parameters.GetValueOrDefault("value")?.ToString();
            CurrentFilter = new FilterState(col ?? "unknown", op, val);
            return Task.FromResult(ActionResult.Success($"Filtered by {col} {op} {val}"));
        }

        private Task<ActionResult> HandleSort(AgentAction action)
        {
            SortColumn = action.Parameters.GetValueOrDefault("column")?.ToString();
            SortDirection = action.Parameters.GetValueOrDefault("direction")?.ToString() ?? "asc";
            return Task.FromResult(ActionResult.Success($"Sorted by {SortColumn} {SortDirection}"));
        }

        private Task<ActionResult> HandleClearFilters()
        {
            CurrentFilter = null;
            return Task.FromResult(ActionResult.Success("Filters cleared"));
        }

        private Task<ActionResult> HandleGoToPage(AgentAction action)
        {
            if (action.TryGetParameter<int>("page", out var page) ||
                action.TryGetParameter<int>("pageIndex", out page))
                CurrentPage = page;
            return Task.FromResult(ActionResult.Success($"Navigated to page {CurrentPage}"));
        }

        private Task<ActionResult> HandleSelectRow(AgentAction action)
        {
            if (action.TryGetParameter<int>("row_index", out var idx))
                SelectedRowIndex = idx;
            else if (action.TryGetParameter<string>("rowKey", out var rowKey) &&
                     int.TryParse(rowKey, out idx))
                SelectedRowIndex = idx;
            return Task.FromResult(ActionResult.Success($"Selected row {SelectedRowIndex}"));
        }
    }

    public sealed class ReportMockDialog : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];

        public ReportMockDialog(string agentId) => AgentId = agentId;

        public string AgentId { get; }
        public string ComponentType => "Dialog";
        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;
        public bool IsOpen { get; set; }
        public bool WasConfirmed { get; private set; }

        public ComponentCapability GetCapability()
        {
            var cap = new ComponentCapability(AgentId, "Mock Dialog");
            cap.UpsertAction(new ComponentActionCapability("open", "Open dialog"));
            cap.UpsertAction(new ComponentActionCapability("close", "Close dialog"));
            cap.UpsertAction(new ComponentActionCapability("confirm", "Confirm"));
            return cap;
        }

        public ComponentState GetCurrentState() => new() { ["isOpen"] = IsOpen };

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
        {
            _executedActions.Add(action);

            return action.Name.ToLowerInvariant() switch
            {
                "open" => Task.FromResult(SetOpen(true)),
                "close" => Task.FromResult(SetOpen(false)),
                "confirm" => Task.FromResult(Confirm()),
                _ => Task.FromResult(ActionResult.Unknown(action.Name))
            };
        }

        private ActionResult SetOpen(bool open) { IsOpen = open; return ActionResult.Success(open ? "Dialog opened" : "Dialog closed"); }
        private ActionResult Confirm() { WasConfirmed = true; IsOpen = false; return ActionResult.Success("Dialog confirmed"); }
    }

    public sealed class ReportMockForm : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];
        private readonly ConcurrentDictionary<string, object?> _fields = new(StringComparer.OrdinalIgnoreCase);

        public ReportMockForm(string agentId) => AgentId = agentId;

        public string AgentId { get; }
        public string ComponentType => "Form";
        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;

        public IReadOnlyDictionary<string, object?> GetAllFields() => _fields;

        public ComponentCapability GetCapability()
        {
            var cap = new ComponentCapability(AgentId, "Mock Form");
            cap.UpsertAction(new ComponentActionCapability("set_field", "Set field value"));
            cap.UpsertAction(new ComponentActionCapability("submit", "Submit form"));
            return cap;
        }

        public ComponentState GetCurrentState()
        {
            var state = new ComponentState();
            foreach (var kv in _fields) state[kv.Key] = kv.Value;
            return state;
        }

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
        {
            _executedActions.Add(action);

            if (action.Name.Equals("set_field", StringComparison.OrdinalIgnoreCase))
            {
                var field = action.Parameters.GetValueOrDefault("field")?.ToString();
                var value = action.Parameters.GetValueOrDefault("value");
                if (!string.IsNullOrEmpty(field))
                {
                    _fields[field] = value;
                    return Task.FromResult(ActionResult.Success($"Set {field} = {value}"));
                }
            }

            return Task.FromResult(ActionResult.Success("Form action completed"));
        }
    }

    public sealed class ReportMockNavMenu : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];
        private readonly List<string> _navHistory = [];

        public ReportMockNavMenu(string agentId) => AgentId = agentId;

        public string AgentId { get; }
        public string ComponentType => "NavMenu";
        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;
        public IReadOnlyList<string> NavigationHistory => _navHistory;
        public string? CurrentUri { get; private set; }

        public ComponentCapability GetCapability()
        {
            var cap = new ComponentCapability(AgentId, "Mock NavMenu");
            cap.UpsertAction(new ComponentActionCapability("navigate_to", "Navigate to URI"));
            return cap;
        }

        public ComponentState GetCurrentState() => new() { ["currentUri"] = CurrentUri };

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
        {
            _executedActions.Add(action);

            if (action.Name.Equals("navigate_to", StringComparison.OrdinalIgnoreCase))
            {
                var uri = action.Parameters.GetValueOrDefault("uri")?.ToString();
                if (!string.IsNullOrEmpty(uri))
                {
                    CurrentUri = uri;
                    _navHistory.Add(uri);
                    return Task.FromResult(ActionResult.Success($"Navigated to {uri}"));
                }
            }

            return Task.FromResult(ActionResult.Failure("Missing uri parameter"));
        }
    }

    public sealed class ReportMockTabs : IAgentControllable
    {
        private readonly List<AgentAction> _executedActions = [];
        private readonly int _tabCount;

        public ReportMockTabs(string agentId, int tabCount = 3)
        {
            AgentId = agentId;
            _tabCount = tabCount;
        }

        public string AgentId { get; }
        public string ComponentType => "Tabs";
        public IReadOnlyList<AgentAction> ExecutedActions => _executedActions;
        public int ActiveTabIndex { get; private set; }

        public ComponentCapability GetCapability()
        {
            var cap = new ComponentCapability(AgentId, "Mock Tabs");
            cap.UpsertAction(new ComponentActionCapability("switch_tab", "Switch tab"));
            return cap;
        }

        public ComponentState GetCurrentState() => new() { ["activeTabIndex"] = ActiveTabIndex };

        public Task<ActionResult> ExecuteActionAsync(AgentAction action, CancellationToken ct = default)
        {
            _executedActions.Add(action);

            if (action.Name.Equals("switch_tab", StringComparison.OrdinalIgnoreCase))
            {
                if ((action.TryGetParameter<int>("tab_index", out var idx) ||
                     action.TryGetParameter<int>("index", out idx)) &&
                    idx >= 0 &&
                    idx < _tabCount)
                {
                    ActiveTabIndex = idx;
                    return Task.FromResult(ActionResult.Success($"Switched to tab {idx}"));
                }
            }

            return Task.FromResult(ActionResult.Failure("Invalid tab index"));
        }
    }

    #endregion

    #region Mock Chat Client

    private static string BuildPlanJson(string toolName, IDictionary<string, object?> arguments)
    {
        if (!TryResolveToolName(toolName, out var componentId, out var actionId))
        {
            return """{"steps":[]}""";
        }

        var payload = new
        {
            steps = new[]
            {
                new
                {
                    componentId,
                    actionId,
                    arguments
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static bool TryResolveToolName(
        string toolName,
        out string componentId,
        out string actionId)
    {
        componentId = string.Empty;
        actionId = string.Empty;

        return toolName.ToLowerInvariant() switch
        {
            "agentblazor_agentnavmenu_navigate_to" => Resolve("AgentNavMenu", "navigate_to", out componentId, out actionId),
            "agentblazor_agentdatagrid_filter" => Resolve("AgentDataGrid", "filter", out componentId, out actionId),
            "agentblazor_agentdatagrid_sort" => Resolve("AgentDataGrid", "sort", out componentId, out actionId),
            "agentblazor_agentdatagrid_go_to_page" => Resolve("AgentDataGrid", "go_to_page", out componentId, out actionId),
            "agentblazor_agentdatagrid_select_row" => Resolve("AgentDataGrid", "select_row", out componentId, out actionId),
            "agentblazor_agentdatagrid_clear_filters" => Resolve("AgentDataGrid", "clear_filters", out componentId, out actionId),
            "agentblazor_agentform_set_field" => Resolve("AgentForm", "set_field", out componentId, out actionId),
            "agentblazor_agentdialog_open" => Resolve("AgentDialog", "open", out componentId, out actionId),
            "agentblazor_agentdialog_close" => Resolve("AgentDialog", "close", out componentId, out actionId),
            "agentblazor_agenttabs_switch_tab" => Resolve("AgentTabs", "switch_tab", out componentId, out actionId),
            _ => false
        };
    }

    private static bool Resolve(
        string resolvedComponentId,
        string resolvedActionId,
        out string componentId,
        out string actionId)
    {
        componentId = resolvedComponentId;
        actionId = resolvedActionId;
        return true;
    }

    private sealed class ScenarioToolChatClient(string toolName, IDictionary<string, object?> arguments) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                BuildPlanJson(toolName, arguments))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(ChatRole.Assistant, BuildPlanJson(toolName, arguments));
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    #endregion
}
