# AgentBlazor

Add an agent chat surface and deterministic app actions to a Blazor app.

Hosted demo:

- https://demo.agentblazor.com/demo/workflows/support-inbox

Install:

```bash
dotnet add package AgentBlazor --prerelease
```

Current public releases are prerelease builds. If you prefer a pinned install, use:

```bash
dotnet add package AgentBlazor --version 0.1.0-preview.11
```

If `dotnet` still probes an old custom package source on your machine, remove or disable that source before testing the public NuGet install path.

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

`AddAgentBlazor(...)` alone does not create a responding agent. Register at least one workflow or agent inside `options.ConfigureBuilder(...)`.

Mount `AgentBlazorShell` in an interactive layout or page. It wraps the AgentBlazor providers and includes the floating chat widget.

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

<AgentBlazorShell>
    @Body
</AgentBlazorShell>
```

Docs and demo:

- Repository: https://github.com/ashpeterson/AgentBlazor
- Hosted demo: https://demo.agentblazor.com/demo/workflows/support-inbox
- Quickstart: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/quickstart.md
- Starter sample: https://github.com/ashpeterson/AgentBlazor/tree/master/samples/AgentBlazor.Starter
