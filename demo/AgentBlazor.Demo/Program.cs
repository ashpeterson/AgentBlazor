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
builder.Services.AddScoped<DojoWorkspaceService>();

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
builder.Services.AddSingleton<SupplierWorkflowService>();
builder.Services.AddSingleton<DemoChartDataSources>();
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

    if (!string.IsNullOrWhiteSpace(proLicenseKey))
    {
        options.UseProLicense(proLicenseKey);
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
