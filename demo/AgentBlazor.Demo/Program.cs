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
builder.Services.AddScoped<DojoExperienceState>();
builder.Services.AddSingleton<DojoWorkspaceService>();
builder.Services.AddScoped<DemoFileWorkflowService>();
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

builder.Services.AddDbContextFactory<DemoWorkflowDbContext>(options =>
    options.UseSqlite(workflowConnectionString));
builder.Services.AddSingleton<DemoWorkflowDatabaseSeeder>();

builder.Services.AddAgentBlazor(options =>
{
    options.UseInstructionsFile("agent-instructions.txt");

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
       // options.UseDevTools();
    }

    if (!string.IsNullOrWhiteSpace(proLicenseKey))
    {
        options.UseProLicense(proLicenseKey);
    }

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddRuntimeEventSubscriber<DojoRuntimeEventSubscriber>();

        agentBuilder.AddAgent("Dojo Workspace Agent", agent =>
        {
            agent.WithDescription("Focused on the interactive dojo recipe workspace.");
            agent.WithAllowedComponents("AgentForm", "AgentDataGrid", "AgentDialog", "AgentTabs", "AgentNavMenu", "DojoRecipe");
            agent.WithMetadata("route_prefixes", "/demo/dojo");
        });

        agentBuilder.AddAgent("Supplier Analyst Agent", agent =>
        {
            agent.WithDescription("Focused on data-centric component exploration and selection-style controls.");
            agent.WithAllowedComponents("AgentDataGrid", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentSelect", "AgentAutocomplete");
            agent.WithMetadata("route_prefixes", "/demo/components,/demo/components/datagrid,/demo/components/select,/demo/components/autocomplete,/demo/components/date-picker,/demo/components/date-range-picker,/demo/components/tree-view");
        });

        agentBuilder.AddAgent("Workflow Orchestrator Agent", agent =>
        {
            agent.WithDescription("Focused on form/dialog/command orchestration across the component explorer.");
            agent.WithAllowedComponents("AgentStepper", "AgentForm", "AgentDialog", "AgentTabs", "AgentNavMenu", "AgentTreeView", "AgentCommandBar", "AgentFileUpload");
            agent.WithMetadata("route_prefixes", "/demo/components,/demo/components/form,/demo/components/dialog,/demo/components/tabs,/demo/components/stepper,/demo/components/command-bar,/demo/components/file-upload,/demo/components/attribute-based");
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
