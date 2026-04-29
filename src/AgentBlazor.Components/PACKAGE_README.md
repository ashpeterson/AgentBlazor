# AgentBlazor

Add an agent chat surface and deterministic app actions to a Blazor app.

Install:

```bash
dotnet add package AgentBlazor
```

Minimal setup:

```csharp
using AgentBlazor;
using MudBlazor.Services;

builder.Services.AddMudServices();

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"]!);

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<SupportInboxCapabilities>("support-inbox", agent =>
        {
            agent.WithRoutePrefixes("/support");
        });
    });
});

var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();
```

```csharp
[AgentCapability("support_inbox")]
public sealed class SupportInboxCapabilities
{
    [AgentAction("Show open tickets that still need a reply")]
    public Task<CapabilityResult> ShowOpenTicketsAsync(int days = 7)
        => Task.FromResult(
            CapabilityResult.Success($"Highlighted tickets from the last {days} days."));

    [AgentAction("Draft a reply for the highlighted tickets", RequiresApproval = true)]
    public Task<CapabilityResult> DraftReplyAsync()
        => Task.FromResult(
            CapabilityResult.Success("Prepared the reply draft.")
                .WithNextActions("Review the reply", "Approve the draft"));
}
```

```razor
@using AgentBlazor.Components

<AgentChatWidget
    Title="Support inbox"
    Placeholder="Show open tickets, explain the queue, or draft a reply..." />
```

Docs and demo:

- Repository: https://github.com/ashpeterson/AgentBlazor
- Quickstart: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/quickstart.md
- Starter sample: https://github.com/ashpeterson/AgentBlazor/tree/master/samples/AgentBlazor.Starter
