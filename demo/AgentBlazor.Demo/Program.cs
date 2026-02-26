using AgentBlazor;
using AgentBlazor.Demo.Components;
using AgentBlazor.Demo.Data;
using AgentBlazor.Demo.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure [AgentFlow] logs are visible when running prompts
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Agents.AgentRuntime", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor.Core.Runtime.Interfaces.InMemoryAgentNavigationIntentService", LogLevel.Information);
builder.Logging.AddFilter("AgentBlazor", LogLevel.Information);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var ollamaModel = builder.Configuration["Ollama:Model"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL");
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
    ?? "http://127.0.0.1:11434/v1";
var ollamaApiKey = builder.Configuration["Ollama:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
var workflowConnectionString = builder.Configuration.GetConnectionString("DemoWorkflow")
    ?? "Data Source=agentblazor-demo.db";

builder.Services.AddDbContextFactory<DemoWorkflowDbContext>(options =>
    options.UseSqlite(workflowConnectionString));
builder.Services.AddSingleton<SupplierWorkflowService>();
builder.Services.AddSingleton<DemoChartDataSources>();
builder.Services.AddSingleton<DemoWorkflowDatabaseSeeder>();

builder.Services.AddAgentBlazor(options =>
{
    options.AgentInstructions =
        """
        Execute only deterministic component actions and generated UI tools.
        Never fabricate operational metrics or chart values.
        For generated charts, use chart.view with dataSource values:
        - demo.suppliers.risk.by-region
        - demo.suppliers.risk.tier-distribution
        - demo.onboarding.volume.monthly (optional dataArguments.months: 3..18)
        - demo.mitigation.volume.monthly (optional dataArguments.months: 3..18)
        Prefer these data sources over inline chart labels/series whenever possible.

        For incident triage prompts on /demo/workflow:
        - Navigate to /demo/workflow when needed.
        - Use workflow-tabs, workflow-supplier-grid, and workflow-mitigation-grid component instances.
        - For triage/high-risk operations, AgentDataGrid actions MUST target agentId workflow-supplier-grid.
        - Never target workflow-intake-grid for risk-score triage operations.
        - For unhealthy/high-risk requests, focus Supplier Risk context (tab index 1), filter RiskScore >= 80, and sort desc.
        - For prompts that include triage + mitigation plan (for example "triage unhealthy services and draft a mitigation plan"):
          - after selecting the high-risk supplier, also open workflow-mitigation-dialog and set workflow-mitigation-runtime-form fields from selected row context,
          - include AgentForm.submit in the same turn so the chat shows approval-required controls immediately.
        - Return generated UI with deterministic tool calls:
          1) summary.card (triage summary)
          2) chart.view (risk trend; prefer dataSource demo.suppliers.risk.by-region)
          3) form.draft (mitigation plan draft)
        - Mitigation follow-up should open workflow-mitigation-dialog, set workflow-mitigation-runtime-form fields, and submit only when explicitly requested/approved.
        - For mitigation follow-up, avoid asking for SupplierId/SupplierName when workflow-supplier-grid state contains focusedRow/currentViewRows.
          If a supplier is not already selected, first target workflow-supplier-grid with filter/sort/select_row, then use that selected row to fill SupplierId and SupplierName.

        For complex workflow prompts on /demo/workflow, keep outputs deterministic and action-oriented:
        - Quarterly review builder:
          - switch to onboarding queue (tab index 0), sort onboarding by Priority desc.
          - for onboarding operations use workflow-intake-grid.
          - return summary.card + chart.view (demo.onboarding.volume.monthly) + form.draft (Owner, Audience, Notes).
        - Change request lifecycle:
          - focus high-priority onboarding or supplier-risk records using filter/sort/select_row.
          - return summary.card + form.draft for the change request and request approval before submit.
        - Forecast-driven planning:
          - always return at least two generated blocks:
            1) summary.card (forecast summary + next-step actions)
            2) chart.view (forecast visual from named data source)
          - use named data sources (demo.suppliers.risk.by-region or demo.mitigation.volume.monthly).
          - include drill-down or scenario-comparison follow-up actions on the summary card.
        - Ops daily standup copilot:
          - return summary.card + table.view + form.draft checklist.
          - include an action that can trigger a follow-up confirmation card.

        For these workflows avoid unnecessary clarifications when defaults are acceptable:
        - Owner default: Ops Lead
        - Audience default: Operations Leadership
        - Notes default: Draft generated from live workflow state
        """;

    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        options.UseOpenAI(openAiApiKey, openAiModel);
    }
    else if (!string.IsNullOrWhiteSpace(ollamaModel))
    {
        options.UseOllama(ollamaModel, ollamaEndpoint, ollamaApiKey);
    }

    options.UseChartDataResolver(sp => sp.GetRequiredService<DemoChartDataSources>().ResolveAsync);
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DemoWorkflowDatabaseSeeder>();
    await seeder.InitializeAsync(CancellationToken.None);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();

app.Run();
