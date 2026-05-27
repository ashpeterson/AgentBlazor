using AgentBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<SupportTicketService>();
builder.Services.AddScoped<InventoryWorkflowService>();
builder.Services.AddAgentBlazor(options =>
{
    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<OrderOperationsCapabilities>("order-operations");
        agentBuilder.AddWorkflow<SupportQueueCapabilities>("support-queue");
    });
});

var app = builder.Build();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();
app.Run();
