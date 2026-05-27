using AgentBlazor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();
builder.Services.AddScoped<AuditBundleService>();
builder.Services.AddAgentBlazor(options =>
{
    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<AuditBundleCapabilities>("audit-bundle");
    });
});

var app = builder.Build();
app.MapAgentBlazorEndpoints();
app.MapFallbackToFile("index.html");
app.Run();
