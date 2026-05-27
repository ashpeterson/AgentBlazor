# AgentBlazor

Last updated: 2026-05-27

Add an agent chat surface and deterministic app actions to a Blazor app.

AgentBlazor gives a Blazor route a chat surface, explicit capabilities, approval-gated actions, and deterministic UI execution without replacing the normal app UI.

Try the hosted demo:

- https://demo.agentblazor.com/demo/workflows/support-inbox

## Install

```bash
dotnet add package AgentBlazor
```

Use `0.2.1` or later. This release includes the mobile chat input stability fix, corrected EF package shape, and tool-friendly schemas for `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, and `Guid` workflow parameters.

The CLI is optional. Keep it out of the critical path unless you want scaffold help for an existing app.

## Dependency Stability

AgentBlazor builds on the Microsoft Agent Framework packages rather than a custom agent runtime. The core package currently depends on four GA packages: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Abstractions`, `Microsoft.Agents.AI.OpenAI`, and `Microsoft.Agents.AI.Workflows`.

Two hosting packages are still preview dependencies: `Microsoft.Agents.AI.Hosting` and `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`. AgentBlazor keeps those behind its own registration and endpoint surface so app code does not need to bind directly to the preview hosting APIs for normal setup. Expect preview-version churn around hosted AG-UI transport before 1.0; the component/capability model is the stable surface this package is trying to protect.

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
- Live demo: `/demo`
- Hosted demo: https://demo.agentblazor.com/demo/workflows/support-inbox
- Starter sample: `samples/AgentBlazor.Starter`
- Starter sample route: `/ops-review`

Run the starter sample:

```bash
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj
```

## Docs

- [Quickstart](docs/quickstart.md)
- [0.2.1 release notes](docs/releases/0.2.1.md)
- [0.2.0 release notes](docs/releases/0.2.0.md)
- [Recoverable capability errors](docs/capability-errors.md)
- [Entity Framework Core schema exposure](docs/entity-framework.md)
- [Beta testing](docs/beta-testing.md)
- [Starter sample](samples/AgentBlazor.Starter/README.md)
- [Advanced CLI](docs/advanced/cli.md)

## Optional EF Core Schema Exposure

If your app uses EF Core, install `AgentBlazor.EntityFrameworkCore` to expose selected entity shapes as planning context:

```bash
dotnet add package AgentBlazor.EntityFrameworkCore
```

This is schema-only. It helps an agent understand safe entity fields, but it does not execute queries, generate LINQ or SQL, scan every `DbSet`, or grant write access. Data access still goes through your typed `[AgentAction]` methods.

## Beta

If you are doing a first-pass install review, use the narrow beta path:

1. install the package
2. wire one support route
3. submit one prompt
4. report the first point of friction

Start here:

- [Beta testing](docs/beta-testing.md)
