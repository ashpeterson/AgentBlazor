# AgentBlazor

Last updated: 2026-04-28

Add an agent chat surface and deterministic app actions to a Blazor app.

AgentBlazor gives a Blazor route a chat surface, explicit capabilities, approval-gated actions, and deterministic UI execution without replacing the normal app UI.

## Current State

This repo is in private preview.

The current install feed is GitHub Packages. Publishing to `nuget.org` is the launch gate for June 9, 2026. Until that happens, the public install path requires a GitHub Packages source and a PAT.

## Install The Current Preview

```bash
dotnet nuget add source "https://nuget.pkg.github.com/ashpeterson/index.json" \
  --name github-agentblazor \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text

dotnet add package AgentBlazor --version 0.1.0-preview.9 --source github-agentblazor
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

- Home video: `/`
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
- [Starter sample](samples/AgentBlazor.Starter/README.md)
- [Advanced CLI](docs/advanced/cli.md)

## Launch Constraint

The launch path is a cut path, not an add-more path.

- keep the package install honest
- keep the public surface focused on one package, one workflow, and one demo
- keep the CLI as advanced/setup-only until the normal package path is public and stable
