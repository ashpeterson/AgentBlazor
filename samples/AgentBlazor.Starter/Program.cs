using AgentBlazor;
using AgentBlazor.Starter.Components;
using AgentBlazor.Starter.Services;
using AgentBlazor.Starter.Workflows;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddScoped<OpsReviewService>();

var openAiApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-5.4-mini";
var ollamaModel = builder.Configuration["Ollama:Model"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL");
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
    ?? "http://127.0.0.1:11434/v1";
var ollamaApiKey = builder.Configuration["Ollama:ApiKey"]
    ?? Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var conversationStorePath = Path.Combine(dataDirectory, "conversations.json");
var sharedStateStorePath = Path.Combine(dataDirectory, "shared-state.json");
var usingOpenAi = !string.IsNullOrWhiteSpace(openAiApiKey);
var usingOllama = !usingOpenAi && !string.IsNullOrWhiteSpace(ollamaModel);

builder.Services.AddSingleton(new StarterRuntimeStatus(
    ProviderLabel: usingOpenAi
        ? $"OpenAI · {openAiModel}"
        : usingOllama
            ? $"Ollama · {ollamaModel}"
            : "No provider configured",
    ConversationStorePath: conversationStorePath,
    SharedStateStorePath: sharedStateStorePath,
    DevToolsEnabled: builder.Environment.IsDevelopment()));

builder.Services.AddAgentBlazor(options =>
{
    if (usingOpenAi && openAiApiKey is { Length: > 0 } configuredOpenAiApiKey)
    {
        options.UseOpenAI(configuredOpenAiApiKey, openAiModel);
    }
    else if (usingOllama)
    {
        options.UseOllama(ollamaModel!, ollamaEndpoint, ollamaApiKey);
    }

    if (builder.Environment.IsDevelopment())
    {
        options.UseDevTools();
    }

    options.ConfigureBuilder(agentBuilder =>
    {
        // Golden-path starter: one workflow, one route-scoped agent, one live page.
        agentBuilder.UseJsonFileConversationStore(conversationStorePath);
        agentBuilder.UseJsonFileSharedStateStore(sharedStateStorePath);
        agentBuilder.AddWorkflow<OpsReviewCapabilities>("ops", agent =>
        {
            agent.WithDescription("Guide the operator through the ops review workflow and explain the approval step.");
            agent.WithRoutePrefixes("/ops-review");
        });
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();

app.Run();
