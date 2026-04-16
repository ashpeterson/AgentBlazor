# AgentBlazor

Last updated: 2026-04-15

Make your Blazor app agent-capable.

AgentBlazor is the Blazor-native execution and UX layer for agent workflows. It gives external or host-provided agents live app context, deterministic UI execution, approvals, and in-app workflow surfaces without turning your app into a chat-for-clicking gimmick.

Today, the most validated production provider path is OpenAI-compatible chat tools via `options.UseOpenAI(...)`. Azure OpenAI is also supported as a first-class provider through the Microsoft Azure OpenAI client and the same `IChatClient` runtime path; other providers remain secondary validation targets until they have matching real-app proof.

## What It Is

- Semantic workflow execution for Blazor apps
- Live UI context and deterministic component actions
- In-app chat, approvals, inspector, and workflow surfaces
- A free path that is useful on day one
- A paid path that gets smarter with use

## What It Is Not

- A general-purpose .NET agent runtime
- A replacement for your normal UI
- A product built around primitive chat-driven clicking

## Fast Demo

Run the demo:

```bash
cd demo/AgentBlazor.Demo
dotnet run
```

Open the primary routes:

- `/` for the product story
- `/demo` to jump straight into the featured live demo
- `/demo/workflows/response-orchestration?reset=true` for the featured live demo
- `/demo/workflows/release-dossier?reset=true` for the secondary release proof

Starter sample:

- `samples/AgentBlazor.Starter`
- `dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj`

Suggested prompts:

- `Assess cross-system response readiness`
- `Advance the next guided subsystem stage`
- `Prepare the response packet`
- `Prepare the release dossier`

## Ship The Free Layer

The free tier should be enough to prove value in a real app:

- install one package
- optionally run the CLI to generate `AGENT.md`
- add the runtime, host shell, and chat surface
- register one workflow agent
- let the agent coordinate the app with deterministic execution

### 1. Install

```bash
dotnet add package AgentBlazor
```

### 2. Register AgentBlazor

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMudServices();

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-5.4-mini");

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<OperationsCapabilities>("operations", agent =>
        {
            agent.WithDescription("Guide the operator through the workflow and explain each approval.");
            agent.WithRoutePrefixes("/ops");
        });
    });
});
```

### 3. Add The Host Shell

For a Blazor Web App, add the CSS and JS assets in `Components/App.razor`:

```razor
<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
<link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
...
<script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
<script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
```

Add the MudBlazor providers in your main layout:

```razor
<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />
```

### 4. Map The Runtime Endpoint

```csharp
var app = builder.Build();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();
```

### 5. Expose One Capability

```csharp
[AgentCapability("operations")]
public sealed class OperationsCapabilities
{
    [AgentAction("Prepare remediation draft", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareRemediationDraftAsync(Guid[] supplierIds)
    {
        return Task.FromResult(
            CapabilityResult.Success("Prepared the remediation draft.")
                .WithWarning("Two suppliers still need manual review.")
                .WithNextActions("Review the draft", "Approve the submission"));
    }
}
```

### 6. Add The Agent Surface

Add the namespace once in your component imports or page:

```razor
@using AgentBlazor.Components
```

Use `AgentChatWidget` when you want a floating assistant:

```razor
<AgentChatWidget
    Title="Operations"
    Placeholder="Ask the agent to assess, prepare, or advance the workflow..."
    Width="30rem"
    Height="68vh" />
```

Use `AgentChatSurface` when the chat should be embedded in the page layout:

```razor
<AgentChatSurface
    Title="Operations"
    Description="Assess, prepare, and approve the next workflow step."
    LockAgentToCurrentRoute="true"
    ShowAgentSelector="false"
    EnableGeneratedUi="true" />
```

That gives you the free hook:

- workflow chat
- deterministic execution
- approvals and execution visibility
- live Blazor-native workflow UX

## Why Teams Upgrade

Free gets the workflow layer into the app.

Paid should make the app better every week it is used:

- action history
- adaptive suggestions
- proactive workflow prompts
- memory-backed guidance over time

Premium is the team layer:

- governance
- analytics
- audit intelligence
- deeper operational oversight

## Current Product Story

The strongest current proof is workflow-first:

- response orchestration across supplier, evidence, and incident surfaces
- release dossier orchestration across readiness and evidence
- approval-gated execution with recovery paths
- inspector, trace, and execution-plan visibility in the app

Supporting references remain available:

- `/demo/components`

## Current Status

As of 2026-04-15:

- the adapter-first runtime path is the default path
- semantic capabilities are a first-class authoring surface
- normalized execution, approval, policy, and context-freshness contracts are in place
- the old planner/runtime path is no longer the product center
- the demo is now led by orchestration workflows instead of primitive component control
- `0.1.0-preview.8` is the current source/package version; `0.1.0-preview.7` remains the latest fully published-feed validated package
- published-feed validation now covers CSP nonce-aware host shells as well as Clean Architecture-style real apps
- runtime execution now preserves caller-owned scoped services across turns instead of silently replacing them with a fresh internal scope
- middleware is now wired through both normal and streaming runtime turns
- OpenAI-compatible endpoint validation now rejects non-HTTP(S) URI shapes such as `file:///...`
- the full non-demo test matrix is currently green:
  - `AgentBlazor.Core.Tests`: `261/261`
  - `AgentBlazor.Components.Tests`: `98/99`, `1` skipped
  - `AgentBlazor.Cli.Analysis.Tests`: `132/132`
  - `AgentBlazor.Cli.IntegrationTests`: `9/9`
  - `AgentBlazor.IntegrationTests`: `105/105`

The biggest remaining product gap is not UI execution. It is durable paid intelligence:

- persistent action history
- stronger cross-session memory
- mature adaptive workflow guidance

## Docs

- [Quickstart](docs/quickstart.md)
- [CLI Guide](docs/cli.md)
- [Status](docs/STATUS.md)
- [Architecture](docs/architecture.md)
- [Runtime Realignment Plan](docs/runtime-realignment-plan.md)
- [Pricing Tiers](docs/pricing-tiers.md)
- [MudBlazor Compatibility Roadmap](docs/mudblazor-compatibility-roadmap.md)

## Template Direction

We should not build templates for every domain.

The intended approach is:

- one golden-path starter
- a few workflow scaffolds
- examples for everything else

The starter should generate:

- one route
- one workflow agent
- one capability class
- one service class
- one chat surface
- one approval boundary

The current starter lives in:

- `samples/AgentBlazor.Starter`

Scaffolds should cover workflow shapes, not industries:

- assessment
- approval-gated mutation
- recovery/retry
- orchestration

## License

MIT
