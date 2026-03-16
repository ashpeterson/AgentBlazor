#pragma warning disable ASP0006
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Runtime;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using System.Linq.Expressions;

namespace AgentBlazor.Components.Tests;

public sealed class CompatibilityRenderParityTests : TestContext
{
    public CompatibilityRenderParityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IAgentComponentRegistry, TestAgentComponentRegistry>();
        Services.AddSingleton<IAgentNavigationIntentService, TestAgentNavigationIntentService>();
        Services.AddSingleton<IAgentDeferredActionEvents, TestDeferredActionEvents>();
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public void AgentForm_RendersSameSharedControlsAsMudForm_AndAddsAgentMetadata()
    {
        var mudModel = new ParityFormModel { SupplierName = "Baseline Supplier" };
        var agentModel = new ParityFormModel { SupplierName = "Agent Supplier" };
        var mudSubmitCount = 0;
        var agentSubmitCount = 0;

        var mud = RenderComponent<MudForm>(parameters => parameters
            .Add(p => p.Model, mudModel)
            .AddChildContent(CreateSharedFormContent(
                mudModel,
                EventCallback.Factory.Create<MouseEventArgs>(this, () => mudSubmitCount++))));

        var agent = RenderComponent<AgentForm>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-form")
            .Add(p => p.Model, agentModel)
            .AddChildContent(CreateSharedFormContent(
                agentModel,
                EventCallback.Factory.Create<MouseEventArgs>(this, () => agentSubmitCount++))));

        Assert.Single(mud.FindAll("input"));
        Assert.Single(agent.FindAll("input"));
        Assert.Contains("Supplier Name", mud.Markup);
        Assert.Contains("Supplier Name", agent.Markup);
        Assert.Contains("Submit", mud.Markup);
        Assert.Contains("Submit", agent.Markup);

        agent.Find("[data-ab-type='form']");
        var formRoot = agent.Find("[data-ab-type='form']");
        Assert.Equal("parity-agent-form", formRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-form", formRoot.GetAttribute("id"));

        Assert.Contains("Baseline Supplier", mud.Markup);
        Assert.Contains("Agent Supplier", agent.Markup);
        Assert.Equal(0, mudSubmitCount);
        Assert.Equal(0, agentSubmitCount);
    }

    [Fact]
    public void AgentDataGrid_RendersSharedToolbarPagerAndRowsLikeMudDataGrid()
    {
        var rows = new[]
        {
            new ParityGridRow("SUP-001", "Alpine Components", "EMEA", 82),
            new ParityGridRow("SUP-002", "Beacon Industrial", "APAC", 55),
            new ParityGridRow("SUP-003", "Cinder Logistics", "NA", 34)
        };

        var mud = RenderComponent<MudDataGrid<ParityGridRow>>(parameters => parameters
            .Add(p => p.Items, rows)
            .Add(p => p.Dense, true)
            .Add(p => p.Hover, true)
            .Add(p => p.Bordered, true)
            .Add(p => p.Striped, true)
            .Add(p => p.RowsPerPage, 2)
            .Add(p => p.ToolBarContent, CreateToolbarContent())
            .Add(p => p.PagerContent, CreatePagerContent())
            .Add(p => p.Columns, CreateGridColumns()));

        var agent = RenderComponent<AgentDataGrid<ParityGridRow>>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-grid")
            .Add(p => p.Items, rows)
            .Add(p => p.Dense, true)
            .Add(p => p.Hover, true)
            .Add(p => p.Bordered, true)
            .Add(p => p.Striped, true)
            .Add(p => p.RowsPerPage, 2)
            .Add(p => p.RowKeyProperty, nameof(ParityGridRow.Id))
            .Add(p => p.ToolBarContent, CreateToolbarContent())
            .Add(p => p.PagerContent, CreatePagerContent())
            .Add(p => p.Columns, CreateGridColumns()));

        Assert.Contains("Shared supplier toolbar", mud.Markup);
        Assert.Contains("Shared supplier toolbar", agent.Markup);
        Assert.Contains("Alpine Components", mud.Markup);
        Assert.Contains("Alpine Components", agent.Markup);
        Assert.Contains("Beacon Industrial", mud.Markup);
        Assert.Contains("Beacon Industrial", agent.Markup);

        var gridRoot = agent.Find("[data-ab-type='datagrid']");
        Assert.Equal("parity-agent-grid", gridRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-grid", gridRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentDialog_RendersSharedTitleContentAndActionsLikeMudDialog()
    {
        var mudVisible = true;
        var agentVisible = true;

        var mud = Render(builder =>
        {
            builder.OpenComponent<MudDialogProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialog>(10);
            builder.AddAttribute(11, nameof(MudDialog.Visible), mudVisible);
            builder.AddAttribute(12, nameof(MudDialog.VisibleChanged), EventCallback.Factory.Create<bool>(this, value => mudVisible = value));
            builder.AddAttribute(13, nameof(MudDialog.TitleContent), CreateDialogTitleContent());
            builder.AddAttribute(14, nameof(MudDialog.DialogContent), CreateDialogBodyContent());
            builder.AddAttribute(15, nameof(MudDialog.DialogActions), CreateDialogActionContent());
            builder.CloseComponent();
        });

        var agent = Render(builder =>
        {
            builder.OpenComponent<MudDialogProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<AgentDialog>(10);
            builder.AddAttribute(11, nameof(AgentDialog.AgentId), "parity-agent-dialog");
            builder.AddAttribute(12, nameof(AgentDialog.Visible), agentVisible);
            builder.AddAttribute(13, nameof(AgentDialog.VisibleChanged), EventCallback.Factory.Create<bool>(this, value => agentVisible = value));
            builder.AddAttribute(14, nameof(AgentDialog.OnConfirm), (Func<Task<ActionResult>>)(() => Task.FromResult(ActionResult.Applied("Confirmed."))));
            builder.AddAttribute(15, nameof(AgentDialog.TitleContent), CreateDialogTitleContent());
            builder.AddAttribute(16, nameof(AgentDialog.DialogContent), CreateDialogBodyContent());
            builder.AddAttribute(17, nameof(AgentDialog.DialogActions), CreateDialogActionContent());
            builder.CloseComponent();
        });

        Assert.Contains("Supplier Approval Review", mud.Markup);
        Assert.Contains("Supplier Approval Review", agent.Markup);
        Assert.Contains("Review the supplier evidence bundle before moving this request to approval.", mud.Markup);
        Assert.Contains("Review the supplier evidence bundle before moving this request to approval.", agent.Markup);
        Assert.Contains("Confirm", mud.Markup);
        Assert.Contains("Confirm", agent.Markup);
        Assert.Contains("Cancel", mud.Markup);
        Assert.Contains("Cancel", agent.Markup);

        var dialogRoot = agent.Find("[data-ab-type='dialog']");
        Assert.Equal("parity-agent-dialog", dialogRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-dialog", dialogRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentSelect_RendersSharedLabelAndSelectedValueLikeMudSelect()
    {
        var mud = RenderComponent<MudSelect<string>>(parameters => parameters
            .Add(p => p.Label, "Country")
            .Add(p => p.Value, "Germany")
            .Add(p => p.Clearable, true)
            .AddChildContent(CreateSelectOptions()));

        var agent = RenderComponent<AgentSelect<string>>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-select")
            .Add(p => p.Label, "Country")
            .Add(p => p.Value, "Germany")
            .Add(p => p.Clearable, true)
            .AddChildContent(CreateSelectOptions()));

        Assert.Contains("Country", mud.Markup);
        Assert.Contains("Country", agent.Markup);

        Assert.Equal("Germany", mud.Find("input").GetAttribute("value"));
        Assert.Equal("Germany", agent.Find("input").GetAttribute("value"));

        var selectRoot = agent.Find("[data-ab-type='select']");
        Assert.Equal("parity-agent-select", selectRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-select", selectRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentAutocomplete_RendersSharedPlaceholderAndCurrentQueryLikeMudAutocomplete()
    {
        var mud = RenderComponent<MudAutocomplete<string>>(parameters => parameters
            .Add(p => p.Label, "Supplier search")
            .Add(p => p.Placeholder, "Search suppliers...")
            .Add(p => p.Text, "Apex")
            .Add(p => p.Value, "Apex Components")
            .Add(p => p.SearchFunc, SearchSupplierOptionsAsync)
            .Add(p => p.Clearable, true));

        var agent = RenderComponent<AgentAutocomplete<string>>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-autocomplete")
            .Add(p => p.Label, "Supplier search")
            .Add(p => p.Placeholder, "Search suppliers...")
            .Add(p => p.Text, "Apex")
            .Add(p => p.Value, "Apex Components")
            .Add(p => p.SearchFunc, SearchSupplierOptionsAsync)
            .Add(p => p.Clearable, true));

        Assert.Contains("Supplier search", mud.Markup);
        Assert.Contains("Supplier search", agent.Markup);

        Assert.Equal(mud.Find("input").GetAttribute("value"), agent.Find("input").GetAttribute("value"));
        Assert.Equal("Apex Components", agent.Find("input").GetAttribute("value"));

        var autocompleteRoot = agent.Find("[data-ab-type='autocomplete']");
        Assert.Equal("parity-agent-autocomplete", autocompleteRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-autocomplete", autocompleteRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentFileUpload_RendersSharedSelectedTemplateAndFileNamesLikeMudFileUpload()
    {
        var files = CreateParityFiles();

        var mud = RenderComponent<MudFileUpload<IReadOnlyList<IBrowserFile>>>(parameters => parameters
            .Add(p => p.Files, files)
            .Add(p => p.Hidden, false)
            .Add(p => p.Accept, ".pdf,.csv")
            .Add(p => p.AppendMultipleFiles, true)
            .Add(p => p.SelectedTemplate, CreateFileSelectedTemplate()));

        var agent = RenderComponent<AgentFileUpload<IReadOnlyList<IBrowserFile>>>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-upload")
            .Add(p => p.Files, files)
            .Add(p => p.Hidden, false)
            .Add(p => p.Accept, ".pdf,.csv")
            .Add(p => p.AppendMultipleFiles, true)
            .Add(p => p.SelectedTemplate, CreateFileSelectedTemplate()));

        Assert.Contains("q1-risk-summary.pdf", mud.Markup);
        Assert.Contains("q1-risk-summary.pdf", agent.Markup);
        Assert.Contains("vendor-checklist.csv", mud.Markup);
        Assert.Contains("vendor-checklist.csv", agent.Markup);

        var uploadRoot = agent.Find("[data-ab-type='file-upload']");
        Assert.Equal("parity-agent-upload", uploadRoot.GetAttribute("data-ab-agentid"));
    }

    [Fact]
    public void AgentTabs_RendersSharedPanelsAndActiveStateLikeMudTabs()
    {
        var mud = RenderComponent<MudTabs>(parameters => parameters
            .Add(p => p.ActivePanelIndex, 1)
            .Add(p => p.ActivePanelIndexChanged, EventCallback.Factory.Create<int>(this, _ => { }))
            .AddChildContent(CreateTabPanels()));

        var agent = RenderComponent<AgentTabs>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-tabs")
            .Add(p => p.ActivePanelIndex, 1)
            .Add(p => p.ActivePanelIndexChanged, EventCallback.Factory.Create<int>(this, _ => { }))
            .AddChildContent(CreateTabPanels()));

        Assert.Contains("Summary", mud.Markup);
        Assert.Contains("Summary", agent.Markup);
        Assert.Contains("Policy", mud.Markup);
        Assert.Contains("Policy", agent.Markup);
        Assert.Contains("Policy controls and compliance checks.", mud.Markup);
        Assert.Contains("Policy controls and compliance checks.", agent.Markup);

        var tabsRoot = agent.Find("[data-ab-type='tabs']");
        Assert.Equal("parity-agent-tabs", tabsRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-tabs", tabsRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentStepper_RendersSharedStepsAndActiveStateLikeMudStepper()
    {
        var mud = RenderComponent<MudStepper>(parameters => parameters
            .Add(p => p.ActiveIndex, 2)
            .Add(p => p.ActiveIndexChanged, EventCallback.Factory.Create<int>(this, _ => { }))
            .AddChildContent(CreateStepperSteps()));

        var agent = RenderComponent<AgentStepper>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-stepper")
            .Add(p => p.CurrentStepIndex, 2)
            .Add(p => p.CurrentStepIndexChanged, EventCallback.Factory.Create<int>(this, _ => { }))
            .AddChildContent(CreateStepperSteps()));

        Assert.Contains("Intake", mud.Markup);
        Assert.Contains("Intake", agent.Markup);
        Assert.Contains("Documents", mud.Markup);
        Assert.Contains("Documents", agent.Markup);
        Assert.Contains("Run the review checks and prepare the final submission.", mud.Markup);
        Assert.Contains("Run the review checks and prepare the final submission.", agent.Markup);

        var stepperRoot = agent.Find("[data-ab-type='stepper']");
        Assert.Equal("parity-agent-stepper", stepperRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-stepper", stepperRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentNavMenu_RendersSharedLinksLikeMudNavMenu()
    {
        var mud = RenderComponent<MudNavMenu>(parameters => parameters
            .Add(p => p.Dense, true)
            .AddChildContent(CreateNavMenuLinks()));

        var agent = RenderComponent<AgentNavMenu>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-navmenu")
            .Add(p => p.Dense, true)
            .AddChildContent(CreateNavMenuLinks()));

        Assert.Contains("Summary", mud.Markup);
        Assert.Contains("Summary", agent.Markup);
        Assert.Contains("Policy", mud.Markup);
        Assert.Contains("Policy", agent.Markup);
        Assert.Contains("Audit", mud.Markup);
        Assert.Contains("Audit", agent.Markup);

        var navRoot = agent.Find("[data-ab-type='navmenu']");
        Assert.Equal("parity-agent-navmenu", navRoot.GetAttribute("data-ab-agentid"));
    }

    [Fact]
    public void AgentTreeView_RendersSharedNodesLikeMudTreeView()
    {
        var mud = RenderComponent<MudTreeView<string>>(parameters => parameters
            .Add(p => p.SelectedValue, "Audit")
            .Add(p => p.SelectedValueChanged, EventCallback.Factory.Create<string?>(this, _ => { }))
            .AddChildContent(CreateTreeViewItems()));

        var agent = RenderComponent<AgentTreeView<string>>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-treeview")
            .Add(p => p.SelectedValue, "Audit")
            .Add(p => p.SelectedValueChanged, EventCallback.Factory.Create<string?>(this, _ => { }))
            .AddChildContent(CreateTreeViewItems()));

        Assert.Contains("Summary", mud.Markup);
        Assert.Contains("Summary", agent.Markup);
        Assert.Contains("Policy", mud.Markup);
        Assert.Contains("Policy", agent.Markup);
        Assert.Contains("Audit", mud.Markup);
        Assert.Contains("Audit", agent.Markup);

        var treeRoot = agent.Find("[data-ab-type='tree-view']");
        Assert.Equal("parity-agent-treeview", treeRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-treeview", treeRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentDatePicker_RendersSharedLabelAndSelectedDateLikeMudDatePicker()
    {
        var selectedDate = new DateTime(2026, 3, 18);
        var minDate = new DateTime(2026, 3, 10);
        var maxDate = new DateTime(2026, 3, 31);

        var mud = RenderComponent<MudDatePicker>(parameters => parameters
            .Add(p => p.Label, "Review date")
            .Add(p => p.Date, selectedDate)
            .Add(p => p.MinDate, minDate)
            .Add(p => p.MaxDate, maxDate));

        var agent = RenderComponent<AgentDatePicker>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-date-picker")
            .Add(p => p.Label, "Review date")
            .Add(p => p.Value, selectedDate)
            .Add(p => p.MinDate, minDate)
            .Add(p => p.MaxDate, maxDate));

        Assert.Contains("Review date", mud.Markup);
        Assert.Contains("Review date", agent.Markup);
        Assert.Equal(mud.Find("input").GetAttribute("value"), agent.Find("input").GetAttribute("value"));

        var datePickerRoot = agent.Find("[data-ab-type='date-picker']");
        Assert.Equal("parity-agent-date-picker", datePickerRoot.GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-agent-date-picker", datePickerRoot.GetAttribute("id"));
    }

    [Fact]
    public void AgentDateRangePicker_RendersSharedLabelAndSelectedRangeLikeMudDateRangePicker()
    {
        var range = new DateRange(new DateTime(2026, 3, 20), new DateTime(2026, 3, 24));
        var minDate = new DateTime(2026, 3, 10);
        var maxDate = new DateTime(2026, 3, 31);

        var mud = RenderComponent<MudDateRangePicker>(parameters => parameters
            .Add(p => p.Label, "Review range")
            .Add(p => p.DateRange, range)
            .Add(p => p.MinDate, minDate)
            .Add(p => p.MaxDate, maxDate));

        var agent = RenderComponent<AgentDateRangePicker>(parameters => parameters
            .Add(p => p.AgentId, "parity-agent-date-range-picker")
            .Add(p => p.Label, "Review range")
            .Add(p => p.StartDate, range.Start)
            .Add(p => p.EndDate, range.End)
            .Add(p => p.MinDate, minDate)
            .Add(p => p.MaxDate, maxDate));

        Assert.Contains("Review range", mud.Markup);
        Assert.Contains("Review range", agent.Markup);
        Assert.Equal(mud.Find("input").GetAttribute("value"), agent.Find("input").GetAttribute("value"));

        var dateRangeRoot = agent.Find("[data-ab-type='date-range-picker']");
        Assert.Equal("parity-agent-date-range-picker", dateRangeRoot.GetAttribute("data-ab-agentid"));
    }

    [Fact]
    public void AgentComponents_ComposeIntoAWorkflowScreenWhilePreservingMudBlazorShapes()
    {
        var mudModel = new WorkflowParityModel
        {
            SupplierName = "Northwind Components",
            RiskTier = "High"
        };
        var agentModel = new WorkflowParityModel
        {
            SupplierName = "Northwind Components",
            RiskTier = "High"
        };
        var reviewDate = new DateTime(2026, 3, 26);
        var files = CreateWorkflowFiles();

        var mud = Render(builder =>
        {
            builder.OpenComponent<MudTabs>(0);
            builder.AddAttribute(1, nameof(MudTabs.ActivePanelIndex), 1);
            builder.AddAttribute(2, nameof(MudTabs.ActivePanelIndexChanged), EventCallback.Factory.Create<int>(this, _ => { }));
            builder.AddAttribute(3, nameof(MudTabs.ChildContent), CreateWorkflowTabPanels());
            builder.CloseComponent();

            builder.OpenComponent<MudStepper>(10);
            builder.AddAttribute(11, nameof(MudStepper.ActiveIndex), 2);
            builder.AddAttribute(12, nameof(MudStepper.ActiveIndexChanged), EventCallback.Factory.Create<int>(this, _ => { }));
            builder.AddAttribute(13, nameof(MudStepper.ChildContent), CreateWorkflowStepperSteps());
            builder.CloseComponent();

            builder.OpenComponent<MudForm>(20);
            builder.AddAttribute(21, nameof(MudForm.Model), mudModel);
            builder.AddAttribute(22, nameof(MudForm.ChildContent), CreateWorkflowFormContent(mudModel));
            builder.CloseComponent();

            builder.OpenComponent<MudDatePicker>(30);
            builder.AddAttribute(31, nameof(MudDatePicker.Label), "Review date");
            builder.AddAttribute(32, nameof(MudDatePicker.Date), reviewDate);
            builder.CloseComponent();

            builder.OpenComponent<MudFileUpload<IReadOnlyList<IBrowserFile>>>(40);
            builder.AddAttribute(41, nameof(MudFileUpload<IReadOnlyList<IBrowserFile>>.Files), files);
            builder.AddAttribute(42, nameof(MudFileUpload<IReadOnlyList<IBrowserFile>>.Hidden), false);
            builder.AddAttribute(43, nameof(MudFileUpload<IReadOnlyList<IBrowserFile>>.Accept), ".pdf,.csv");
            builder.AddAttribute(44, nameof(MudFileUpload<IReadOnlyList<IBrowserFile>>.AppendMultipleFiles), true);
            builder.AddAttribute(45, nameof(MudFileUpload<IReadOnlyList<IBrowserFile>>.SelectedTemplate), CreateFileSelectedTemplate());
            builder.CloseComponent();
        });

        var agent = Render(builder =>
        {
            builder.OpenComponent<AgentTabs>(0);
            builder.AddAttribute(1, nameof(AgentTabs.AgentId), "parity-workflow-tabs");
            builder.AddAttribute(2, nameof(AgentTabs.ActivePanelIndex), 1);
            builder.AddAttribute(3, nameof(AgentTabs.ActivePanelIndexChanged), EventCallback.Factory.Create<int>(this, _ => { }));
            builder.AddAttribute(4, nameof(AgentTabs.ChildContent), CreateWorkflowTabPanels());
            builder.CloseComponent();

            builder.OpenComponent<AgentStepper>(10);
            builder.AddAttribute(11, nameof(AgentStepper.AgentId), "parity-workflow-stepper");
            builder.AddAttribute(12, nameof(AgentStepper.CurrentStepIndex), 2);
            builder.AddAttribute(13, nameof(AgentStepper.CurrentStepIndexChanged), EventCallback.Factory.Create<int>(this, _ => { }));
            builder.AddAttribute(14, nameof(AgentStepper.ChildContent), CreateWorkflowStepperSteps());
            builder.CloseComponent();

            builder.OpenComponent<AgentForm>(20);
            builder.AddAttribute(21, nameof(AgentForm.AgentId), "parity-workflow-form");
            builder.AddAttribute(22, nameof(AgentForm.Model), agentModel);
            builder.AddAttribute(23, nameof(AgentForm.ChildContent), CreateWorkflowFormContent(agentModel));
            builder.CloseComponent();

            builder.OpenComponent<AgentDatePicker>(30);
            builder.AddAttribute(31, nameof(AgentDatePicker.AgentId), "parity-workflow-date");
            builder.AddAttribute(32, nameof(AgentDatePicker.Label), "Review date");
            builder.AddAttribute(33, nameof(AgentDatePicker.Value), reviewDate);
            builder.CloseComponent();

            builder.OpenComponent<AgentFileUpload<IReadOnlyList<IBrowserFile>>>(40);
            builder.AddAttribute(41, nameof(AgentFileUpload<IReadOnlyList<IBrowserFile>>.AgentId), "parity-workflow-upload");
            builder.AddAttribute(42, nameof(AgentFileUpload<IReadOnlyList<IBrowserFile>>.Files), files);
            builder.AddAttribute(43, nameof(AgentFileUpload<IReadOnlyList<IBrowserFile>>.Hidden), false);
            builder.AddAttribute(44, nameof(AgentFileUpload<IReadOnlyList<IBrowserFile>>.Accept), ".pdf,.csv");
            builder.AddAttribute(45, nameof(AgentFileUpload<IReadOnlyList<IBrowserFile>>.AppendMultipleFiles), true);
            builder.AddAttribute(46, nameof(AgentFileUpload<IReadOnlyList<IBrowserFile>>.SelectedTemplate), CreateFileSelectedTemplate());
            builder.CloseComponent();
        });

        Assert.Contains("Documents", mud.Markup);
        Assert.Contains("Documents", agent.Markup);
        Assert.Contains("Review", mud.Markup);
        Assert.Contains("Review", agent.Markup);
        Assert.Contains("Supplier Name", mud.Markup);
        Assert.Contains("Supplier Name", agent.Markup);
        Assert.Contains("Northwind Components", mud.Markup);
        Assert.Contains("Northwind Components", agent.Markup);
        Assert.Contains("High", mud.Markup);
        Assert.Contains("High", agent.Markup);
        Assert.Contains("Review date", mud.Markup);
        Assert.Contains("Review date", agent.Markup);
        Assert.Contains("risk-summary-q1.pdf", mud.Markup);
        Assert.Contains("risk-summary-q1.pdf", agent.Markup);
        Assert.Contains("vendor-checklist.csv", mud.Markup);
        Assert.Contains("vendor-checklist.csv", agent.Markup);

        Assert.Equal("parity-workflow-tabs", agent.Find("[data-ab-type='tabs']").GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-workflow-stepper", agent.Find("[data-ab-type='stepper']").GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-workflow-form", agent.Find("[data-ab-type='form']").GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-workflow-date", agent.Find("[data-ab-type='date-picker']").GetAttribute("data-ab-agentid"));
        Assert.Equal("parity-workflow-upload", agent.Find("[data-ab-type='file-upload']").GetAttribute("data-ab-agentid"));
    }

    private static RenderFragment CreateSharedFormContent(
        ParityFormModel model,
        EventCallback<MouseEventArgs> submitCallback)
    {
        return builder =>
        {
            builder.OpenComponent(0, typeof(MudTextField<string>));
            builder.AddAttribute(1, "Label", "Supplier Name");
            builder.AddAttribute(2, "Immediate", true);
            builder.AddAttribute(3, "Value", model.SupplierName);
            builder.AddAttribute(4, "ValueChanged", EventCallback.Factory.Create<string>(
                model,
                value => model.SupplierName = value));
            builder.CloseComponent();

            builder.OpenComponent(5, typeof(MudButton));
            builder.AddAttribute(6, "Variant", Variant.Filled);
            builder.AddAttribute(7, "Color", Color.Primary);
            builder.AddAttribute(8, "OnClick", submitCallback);
            builder.AddAttribute(9, "ChildContent", (RenderFragment)(contentBuilder =>
            {
                contentBuilder.AddContent(10, "Submit");
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateToolbarContent()
    {
        return builder =>
        {
            builder.OpenComponent(0, typeof(MudText));
            builder.AddAttribute(1, "Typo", Typo.subtitle2);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(contentBuilder =>
            {
                contentBuilder.AddContent(3, "Shared supplier toolbar");
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreatePagerContent()
    {
        return builder =>
        {
            builder.OpenComponent(0, typeof(MudDataGridPager<ParityGridRow>));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateDialogTitleContent()
    {
        return builder =>
        {
            builder.OpenComponent(0, typeof(MudText));
            builder.AddAttribute(1, "Typo", Typo.h6);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(contentBuilder =>
            {
                contentBuilder.AddContent(3, "Supplier Approval Review");
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateDialogBodyContent()
    {
        return builder =>
        {
            builder.OpenComponent(0, typeof(MudText));
            builder.AddAttribute(1, "Typo", Typo.body2);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(contentBuilder =>
            {
                contentBuilder.AddContent(3, "Review the supplier evidence bundle before moving this request to approval.");
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateDialogActionContent()
    {
        return builder =>
        {
            builder.OpenComponent(0, typeof(MudButton));
            builder.AddAttribute(1, "Variant", Variant.Filled);
            builder.AddAttribute(2, "Color", Color.Primary);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(contentBuilder =>
            {
                contentBuilder.AddContent(4, "Confirm");
            }));
            builder.CloseComponent();

            builder.OpenComponent(5, typeof(MudButton));
            builder.AddAttribute(6, "Variant", Variant.Text);
            builder.AddAttribute(7, "Color", Color.Secondary);
            builder.AddAttribute(8, "ChildContent", (RenderFragment)(contentBuilder =>
            {
                contentBuilder.AddContent(9, "Cancel");
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateTabPanels()
    {
        return builder =>
        {
            AddTabPanel(builder, 0, "Summary", "Supplier overview and risk summary.");
            AddTabPanel(builder, 10, "Policy", "Policy controls and compliance checks.");
            AddTabPanel(builder, 20, "Audit", "Audit logs and evidence bundle state.");
        };
    }

    private static void AddTabPanel(RenderTreeBuilder builder, int sequence, string text, string body)
    {
        builder.OpenComponent<MudTabPanel>(sequence);
        builder.AddAttribute(sequence + 1, nameof(MudTabPanel.Text), text);
        builder.AddAttribute(sequence + 2, nameof(MudTabPanel.ChildContent), (RenderFragment)(contentBuilder =>
        {
            contentBuilder.OpenComponent<MudText>(0);
            contentBuilder.AddAttribute(1, nameof(MudText.Typo), Typo.body2);
            contentBuilder.AddAttribute(2, nameof(MudText.ChildContent), (RenderFragment)(textBuilder =>
            {
                textBuilder.AddContent(0, body);
            }));
            contentBuilder.CloseComponent();
        }));
        builder.CloseComponent();
    }

    private static RenderFragment CreateStepperSteps()
    {
        return builder =>
        {
            AddStepperStep(builder, 0, "Intake", "Create the onboarding kit and confirm the intake owner.");
            AddStepperStep(builder, 10, "Documents", "Collect the required documents and verify policy coverage.");
            AddStepperStep(builder, 20, "Review", "Run the review checks and prepare the final submission.");
        };
    }

    private static RenderFragment CreateWorkflowTabPanels()
    {
        return builder =>
        {
            AddTabPanel(builder, 0, "Overview", "Supplier summary and review intake.");
            AddTabPanel(builder, 10, "Documents", "Evidence bundle and supporting files.");
            AddTabPanel(builder, 20, "Approval", "Final review and sign-off.");
        };
    }

    private static RenderFragment CreateWorkflowStepperSteps()
    {
        return builder =>
        {
            AddStepperStep(builder, 0, "Intake", "Capture supplier context and assign a risk tier.");
            AddStepperStep(builder, 10, "Documents", "Collect evidence and confirm the review window.");
            AddStepperStep(builder, 20, "Review", "Prepare the final workflow handoff.");
        };
    }

    private static void AddStepperStep(RenderTreeBuilder builder, int sequence, string title, string body)
    {
        builder.OpenComponent<MudStep>(sequence);
        builder.AddAttribute(sequence + 1, nameof(MudStep.Title), title);
        builder.AddAttribute(sequence + 2, nameof(MudStep.ChildContent), (RenderFragment)(contentBuilder =>
        {
            contentBuilder.OpenComponent<MudText>(0);
            contentBuilder.AddAttribute(1, nameof(MudText.Typo), Typo.body2);
            contentBuilder.AddAttribute(2, nameof(MudText.ChildContent), (RenderFragment)(textBuilder =>
            {
                textBuilder.AddContent(0, body);
            }));
            contentBuilder.CloseComponent();
        }));
        builder.CloseComponent();
    }

    private static RenderFragment CreateNavMenuLinks()
    {
        return builder =>
        {
            AddNavLink(builder, 0, "#summary", "Summary");
            AddNavLink(builder, 10, "#policy", "Policy");
            AddNavLink(builder, 20, "#audit", "Audit");
        };
    }

    private static void AddNavLink(RenderTreeBuilder builder, int sequence, string href, string text)
    {
        builder.OpenComponent<MudNavLink>(sequence);
        builder.AddAttribute(sequence + 1, nameof(MudNavLink.Href), href);
        builder.AddAttribute(sequence + 2, nameof(MudNavLink.ChildContent), (RenderFragment)(contentBuilder =>
        {
            contentBuilder.AddContent(0, text);
        }));
        builder.CloseComponent();
    }

    private static RenderFragment CreateTreeViewItems()
    {
        return builder =>
        {
            builder.OpenComponent<MudTreeViewItem<string>>(0);
            builder.AddAttribute(1, nameof(MudTreeViewItem<string>.Text), "Summary");
            builder.AddAttribute(2, nameof(MudTreeViewItem<string>.Value), "Summary");
            builder.CloseComponent();

            builder.OpenComponent<MudTreeViewItem<string>>(10);
            builder.AddAttribute(11, nameof(MudTreeViewItem<string>.Text), "Policy");
            builder.AddAttribute(12, nameof(MudTreeViewItem<string>.Value), "Policy");
            builder.AddAttribute(13, nameof(MudTreeViewItem<string>.Expanded), true);
            builder.AddAttribute(14, nameof(MudTreeViewItem<string>.ChildContent), (RenderFragment)(contentBuilder =>
            {
                contentBuilder.OpenComponent<MudTreeViewItem<string>>(0);
                contentBuilder.AddAttribute(1, nameof(MudTreeViewItem<string>.Text), "Audit");
                contentBuilder.AddAttribute(2, nameof(MudTreeViewItem<string>.Value), "Audit");
                contentBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateGridColumns()
    {
        return builder =>
        {
            AddPropertyColumn(builder, 0, (ParityGridRow row) => row.Id, "ID");
            AddPropertyColumn(builder, 10, (ParityGridRow row) => row.Name, "Supplier");
            AddPropertyColumn(builder, 20, (ParityGridRow row) => row.Region, "Region");
            AddPropertyColumn(builder, 30, (ParityGridRow row) => row.RiskScore, "Risk Score");
        };
    }

    private static void AddPropertyColumn<TProperty>(
        RenderTreeBuilder builder,
        int sequence,
        Expression<Func<ParityGridRow, TProperty>> property,
        string title)
    {
        builder.OpenComponent(0, typeof(PropertyColumn<ParityGridRow, TProperty>));
        builder.AddAttribute(1, "Property", property);
        builder.AddAttribute(2, "Title", title);
        builder.CloseComponent();
    }

    private static RenderFragment CreateSelectOptions()
    {
        return builder =>
        {
            AddSelectOption(builder, 0, "United Kingdom");
            AddSelectOption(builder, 10, "Germany");
            AddSelectOption(builder, 20, "Japan");
        };
    }

    private static void AddSelectOption(RenderTreeBuilder builder, int sequence, string value)
    {
        builder.OpenComponent<MudSelectItem<string>>(sequence);
        builder.AddAttribute(sequence + 1, nameof(MudSelectItem<string>.Value), value);
        builder.AddAttribute(sequence + 2, nameof(MudSelectItem<string>.ChildContent), (RenderFragment)(contentBuilder =>
        {
            contentBuilder.AddContent(0, value);
        }));
        builder.CloseComponent();
    }

    private static RenderFragment CreateWorkflowFormContent(WorkflowParityModel model)
    {
        return builder =>
        {
            builder.OpenComponent<MudGrid>(0);
            builder.AddAttribute(1, nameof(MudGrid.Spacing), 2);
            builder.AddAttribute(2, nameof(MudGrid.ChildContent), (RenderFragment)(contentBuilder =>
            {
                contentBuilder.OpenComponent<MudItem>(0);
                contentBuilder.AddAttribute(1, nameof(MudItem.xs), 12);
                contentBuilder.AddAttribute(2, nameof(MudItem.md), 6);
                contentBuilder.AddAttribute(3, nameof(MudItem.ChildContent), (RenderFragment)(fieldBuilder =>
                {
                    fieldBuilder.OpenComponent<MudTextField<string>>(0);
                    fieldBuilder.AddAttribute(1, nameof(MudTextField<string>.Label), "Supplier Name");
                    fieldBuilder.AddAttribute(2, nameof(MudTextField<string>.Value), model.SupplierName);
                    fieldBuilder.AddAttribute(3, nameof(MudTextField<string>.ValueChanged), EventCallback.Factory.Create<string>(
                        model,
                        value => model.SupplierName = value));
                    fieldBuilder.CloseComponent();
                }));
                contentBuilder.CloseComponent();

                contentBuilder.OpenComponent<MudItem>(10);
                contentBuilder.AddAttribute(11, nameof(MudItem.xs), 12);
                contentBuilder.AddAttribute(12, nameof(MudItem.md), 6);
                contentBuilder.AddAttribute(13, nameof(MudItem.ChildContent), (RenderFragment)(fieldBuilder =>
                {
                    fieldBuilder.OpenComponent<MudSelect<string>>(0);
                    fieldBuilder.AddAttribute(1, nameof(MudSelect<string>.Label), "Risk Tier");
                    fieldBuilder.AddAttribute(2, nameof(MudSelect<string>.Value), model.RiskTier);
                    fieldBuilder.AddAttribute(3, nameof(MudSelect<string>.ValueChanged), EventCallback.Factory.Create<string>(
                        model,
                        value => model.RiskTier = value));
                    fieldBuilder.AddAttribute(4, nameof(MudSelect<string>.ChildContent), CreateWorkflowRiskTierOptions());
                    fieldBuilder.CloseComponent();
                }));
                contentBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        };
    }

    private static RenderFragment CreateWorkflowRiskTierOptions()
    {
        return builder =>
        {
            AddSelectOption(builder, 0, "Low");
            AddSelectOption(builder, 10, "Medium");
            AddSelectOption(builder, 20, "High");
        };
    }

    private static Task<IEnumerable<string>> SearchSupplierOptionsAsync(string? value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new[] { "Apex Components", "Atlas Metals", "Beacon Industrial" }.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(value))
        {
            options = options.Where(option => option.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(options);
    }

    private static RenderFragment<IReadOnlyList<IBrowserFile>?> CreateFileSelectedTemplate()
    {
        return files => builder =>
        {
            builder.OpenComponent<MudStack>(0);
            builder.AddAttribute(1, nameof(MudStack.Spacing), 1);
            builder.AddAttribute(2, nameof(MudStack.ChildContent), (RenderFragment)(contentBuilder =>
            {
                if (files is null)
                {
                    return;
                }

                var sequence = 0;
                foreach (var file in files)
                {
                    contentBuilder.OpenComponent<MudText>(sequence++);
                    contentBuilder.AddAttribute(sequence++, nameof(MudText.Typo), Typo.body2);
                    contentBuilder.AddAttribute(sequence++, nameof(MudText.ChildContent), (RenderFragment)(textBuilder =>
                    {
                        textBuilder.AddContent(0, file.Name);
                    }));
                    contentBuilder.CloseComponent();
                }
            }));
            builder.CloseComponent();
        };
    }

    private static IReadOnlyList<IBrowserFile> CreateParityFiles() =>
    [
        new ParityBrowserFile("q1-risk-summary.pdf", "application/pdf", 128_000),
        new ParityBrowserFile("vendor-checklist.csv", "text/csv", 24_000)
    ];

    private static IReadOnlyList<IBrowserFile> CreateWorkflowFiles() =>
    [
        new ParityBrowserFile("risk-summary-q1.pdf", "application/pdf", 128_000),
        new ParityBrowserFile("vendor-checklist.csv", "text/csv", 24_000)
    ];

    private sealed class ParityFormModel
    {
        public string SupplierName { get; set; } = string.Empty;
    }

    private sealed class WorkflowParityModel
    {
        public string SupplierName { get; set; } = string.Empty;

        public string RiskTier { get; set; } = string.Empty;
    }

    private sealed record ParityGridRow(
        string Id,
        string Name,
        string Region,
        int RiskScore);

    private sealed class ParityBrowserFile(string name, string contentType, long size) : IBrowserFile
    {
        public string Name { get; } = name;

        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;

        public long Size { get; } = size;

        public string ContentType { get; } = contentType;

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new MemoryStream();
        }
    }

    private sealed class TestAgentComponentRegistry : IAgentComponentRegistry
    {
        private readonly Dictionary<string, IAgentControllable> _components = new(StringComparer.OrdinalIgnoreCase);

        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public void Register(IAgentControllable component)
        {
            _components[component.AgentId] = component;
        }

        public bool Unregister(string agentId) => _components.Remove(agentId);

        public bool TryGet(string agentId, out IAgentControllable component) => _components.TryGetValue(agentId, out component!);

        public IReadOnlyCollection<IAgentControllable> GetAll() => _components.Values.ToArray();
    }

    private sealed class TestAgentNavigationIntentService : IAgentNavigationIntentService
    {
        public void Enqueue(string componentType, AgentAction action)
        {
        }

        public void Enqueue(string componentType, string? agentId, AgentAction action)
        {
        }

        public void Enqueue(string componentType, string? agentId, AgentAction action, PendingActionOptions options)
        {
        }

        public IReadOnlyList<AgentAction> Dequeue(string componentType) => [];

        public IReadOnlyList<AgentAction> Dequeue(string componentType, string? agentId) => [];

        public bool HasPending(string componentType) => false;

        public bool HasPending(string componentType, string? agentId) => false;

        public void MarkNavigationCompleted(string? path)
        {
        }
    }

    private sealed class TestDeferredActionEvents : IAgentDeferredActionEvents
    {
        public event Action<DeferredComponentActionEvent>? DeferredActionCompleted;

        public void Publish(DeferredComponentActionEvent actionEvent)
        {
            DeferredActionCompleted?.Invoke(actionEvent);
        }
    }
}
#pragma warning restore ASP0006
