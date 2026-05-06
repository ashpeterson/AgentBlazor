# AgentBlazor

Last updated: 2026-04-29

Add an agent chat surface and deterministic app actions to a Blazor app.

AgentBlazor gives a Blazor route a chat surface, explicit capabilities, approval-gated actions, and deterministic UI execution without replacing the normal app UI.

## Install

```bash
dotnet add package AgentBlazor --version 0.1.0-preview.11
```

The CLI is optional. Keep it out of the critical path unless you want scaffold help for an existing app.

## Minimal Setup

Register the runtime in `Program.cs`:

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

Add one capability class:

```csharp
[AgentCapability("support_inbox")]
public sealed class SupportInboxCapabilities
{
    [AgentAction("Draft a reply for the highlighted tickets", RequiresApproval = true)]
    public Task<CapabilityResult> DraftReplyAsync(string[] ticketIds)
    {
        return Task.FromResult(
            CapabilityResult.Success("Prepared the reply draft.")
                .WithWarning("One ticket still needs escalation evidence.")
                .WithNextActions("Review the reply", "Approve the reply draft"));
    }
}
```

Render one chat surface:

```razor
@using AgentBlazor.Components

<AgentChatWidget
    Title="Support inbox"
    Placeholder="Show open tickets, explain the queue, or draft a reply..."
    Width="30rem"
    Height="68vh" />
```

## What To Open

- Home quickstart: `/`
- Docs: `/docs`
- Live demo: `/demo/workflows/support-inbox`
- Starter sample: `samples/AgentBlazor.Starter`
- Starter sample route: `/ops-review`

Run the starter sample:

```bash
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj
```

## Docs

- [Quickstart](docs/quickstart.md)
- [Beta testing](docs/beta-testing.md)
- [Starter sample](samples/AgentBlazor.Starter/README.md)
- [Advanced CLI](docs/advanced/cli.md)

## Beta

If you are doing a first-pass install review, use the narrow beta path:

1. install the package
2. wire one support route
3. submit one prompt
4. report the first point of friction

Start here:

- [Beta testing](docs/beta-testing.md)
