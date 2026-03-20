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
builder.Services.AddSingleton<DojoWorkspaceService>();
builder.Services.AddScoped<DemoFileWorkflowService>();
builder.Services.AddScoped<DojoRecipeReleaseWorkflowService>();
builder.Services.AddScoped<IncidentEscalationWorkflowService>();
builder.Services.AddScoped<SupplierComplianceWorkflowService>();
builder.Services.AddScoped<ResponseOrchestrationWorkflowService>();
builder.Services.AddScoped<ReleaseDossierWorkflowService>();
builder.Services.Configure<DemoRemoteStorageOptions>(builder.Configuration.GetSection(DemoRemoteStorageOptions.SectionName));
builder.Services.AddHttpClient("demo-remote-storage");
builder.Services.AddSingleton<IDemoRemoteStorageAdapter, DemoRemoteStorageAdapter>();

var proLicenseKey = builder.Configuration["AgentBlazor:LicenseKey"]
    ?? Environment.GetEnvironmentVariable("AGENTBLAZOR_LICENSE_KEY");

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
var sharedAgentInstructionsPath = Path.Combine(builder.Environment.ContentRootPath, "agent-instructions.txt");
var sharedAgentInstructions = File.Exists(sharedAgentInstructionsPath)
    ? File.ReadAllText(sharedAgentInstructionsPath)
    : null;

builder.Services.AddDbContextFactory<DemoWorkflowDbContext>(options =>
    options.UseSqlite(workflowConnectionString));
builder.Services.AddSingleton<DemoWorkflowDatabaseSeeder>();

builder.Services.AddAgentBlazor(options =>
{
    if (!string.IsNullOrWhiteSpace(openAiApiKey))
    {
        options.UseOpenAI(openAiApiKey, openAiModel);
    }
    else if (!string.IsNullOrWhiteSpace(ollamaModel))
    {
        options.UseOllama(ollamaModel, ollamaEndpoint, ollamaApiKey);
    }

    if (builder.Environment.IsDevelopment())
    {
        options.UseDevTools();
    }

    if (!string.IsNullOrWhiteSpace(proLicenseKey))
    {
        options.UseProLicense(proLicenseKey);
    }

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.EnablePromptTracing();
        agentBuilder.AddRuntimeEventSubscriber<DojoRuntimeEventSubscriber>();
        agentBuilder.AddCapability<DemoFileWorkflowCapabilities>();
        agentBuilder.AddCapability<DojoRecipeReleaseCapabilities>();
        agentBuilder.AddCapability<IncidentEscalationCapabilities>();
        agentBuilder.AddCapability<SupplierComplianceCapabilities>();
        agentBuilder.AddCapability<ResponseOrchestrationCapabilities>();
        agentBuilder.AddCapability<ReleaseDossierCapabilities>();

        agentBuilder.AddAgent("Workflow Hub Agent", agent =>
        {
            agent.WithDescription("Focused on routing users toward the right semantic workflow showcase and explaining the workflow-first product story.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
        });

        agentBuilder.AddAgent("Dojo Workspace Agent", agent =>
        {
            agent.WithDescription("Focused on the three-pillar dojo workspace.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("DojoIncident");
            agent.WithMetadata("route_prefixes", "/demo/dojo");
        });

        agentBuilder.AddAgent("Supplier Analyst Agent", agent =>
        {
            agent.WithDescription("Focused on the component reference surface for data-centric controls and selection patterns.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDataGrid", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentSelect", "AgentAutocomplete");
            agent.WithMetadata("route_prefixes", "/demo/components,/demo/components/datagrid,/demo/components/select,/demo/components/autocomplete,/demo/components/date-picker,/demo/components/date-range-picker,/demo/components/tree-view");
        });

        agentBuilder.AddAgent("Workflow Orchestrator Agent", agent =>
        {
            agent.WithDescription("Focused on the component reference surface for form, dialog, command, and file workflow primitives.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentStepper", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentTreeView", "AgentCommandBar", "AgentFileUpload");
            agent.WithMetadata("route_prefixes", "/demo/components,/demo/components/form,/demo/components/dialog,/demo/components/tabs,/demo/components/stepper,/demo/components/command-bar,/demo/components/file-upload,/demo/components/attribute-based");
        });

        agentBuilder.AddAgent("Supplier Compliance Agent", agent =>
        {
            agent.WithDescription("Focused on supplier risk review, explanation, recovery-playbook guidance, and remediation preparation.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDataGrid", "AgentDialog");
            agent.WithMetadata("route_prefixes", "/demo/workflows/supplier-compliance");
        });

        agentBuilder.AddAgent("File Workflow Agent", agent =>
        {
            agent.WithDescription("Focused on file audit bundles, remote handoff, and token verification workflows.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentFileUpload", "AgentCommandBar");
            agent.WithMetadata("route_prefixes", "/demo/workflows/file-audit-bundle");
        });

        agentBuilder.AddAgent("Recipe Release Agent", agent =>
        {
            agent.WithDescription("Focused on dojo recipe readiness, release blockers, recovery-playbook guidance, and publish-ready draft preparation.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentForm", "AgentDataGrid", "AgentDialog");
            agent.WithMetadata("route_prefixes", "/demo/workflows/recipe-release");
        });

        agentBuilder.AddAgent("Incident Escalation Agent", agent =>
        {
            agent.WithDescription("Focused on incident triage, evidence review, escalation brief preparation, and recovery from blocked review-board handoffs.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentTreeView", "AgentTabs", "AgentStepper", "AgentCommandBar", "AgentDialog");
            agent.WithMetadata("route_prefixes", "/demo/workflows/incident-escalation");
        });

        agentBuilder.AddAgent("Response Orchestration Agent", agent =>
        {
            agent.WithDescription("Focused on cross-system orchestration across supplier risk, audit evidence, and incident escalation, including guided subsystem-stage advancement before operational handoff.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDialog");
            agent.WithMetadata("route_prefixes", "/demo/workflows/response-orchestration");
        });

        agentBuilder.AddAgent("Release Dossier Agent", agent =>
        {
            agent.WithDescription("Focused on recipe release readiness and audit evidence orchestration before release dossier handoff.");
            if (!string.IsNullOrWhiteSpace(sharedAgentInstructions))
            {
                agent.WithInstructions(sharedAgentInstructions);
            }
            agent.WithAllowedComponents("AgentDialog");
            agent.WithMetadata("route_prefixes", "/demo/workflows/release-dossier");
        });
    });
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

