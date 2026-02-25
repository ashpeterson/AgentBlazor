using AgentBlazor;
using AgentBlazor.Demo.Components;
using AgentBlazor.Demo.Services;
using AgentBlazor.Core.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
});

if (bool.TryParse(Environment.GetEnvironmentVariable("AGENTBLAZOR_DEMO_DETERMINISTIC_E2E"), out var useDeterministicE2e) &&
    useDeterministicE2e)
{
    builder.Services.Replace(ServiceDescriptor.Singleton<IChatClient, E2eDeterministicChatClient>());
}

builder.Services.Replace(ServiceDescriptor.Singleton<IAgentUiToolCatalog, DemoAgentUiToolCatalog>());

var app = builder.Build();

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
