# AgentBlazor

Make your Blazor app agent-capable.

AgentBlazor is the Blazor-native execution and UX layer for agent workflows. It gives external or host-provided agents live app context, deterministic UI execution, approvals, and in-app workflow surfaces without turning your app into a chat-for-clicking gimmick.

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
- `/demo` for the workflow hub
- `/demo/workflows/response-orchestration?reset=true` for the featured live demo
- `/demo/workflows/release-dossier?reset=true` for the second orchestration proof

Suggested prompts:

- `Assess cross-system response readiness`
- `Advance the next guided subsystem stage`
- `Prepare the response packet`
- `Prepare the release dossier`

## Ship The Free Layer

The free tier should be enough to prove value in a real app:

- add the runtime and chat surface
- register one explicit agent
- expose one semantic workflow or capability
- let the agent coordinate the app with deterministic execution

### 1. Register AgentBlazor

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: "gpt-5.4-mini");
});
```

### 2. Register An Agent And A Capability

```csharp
builder.Services.AddAgentBlazor()
    .AddAgent("operations", agent =>
    {
        agent.WithDisplayName("Operations Agent");
        agent.WithInstructions("Guide the operator through the workflow and explain each approval.");
    })
    .AddCapability<OperationsCapabilities>();
```

```csharp
[AgentCapability("operations")]
public sealed class OperationsCapabilities
{
    [AgentAction(
        Name = "Prepare remediation draft",
        RequiresApproval = true,
        Category = "Operations")]
    public Task<CapabilityResult> PrepareRemediationDraftAsync(Guid[] supplierIds)
    {
        // host-owned workflow logic here
        throw new NotImplementedException();
    }
}
```

### 3. Add The Agent Surface

```razor
<AgentChatWidget
    Title="Operations"
    Placeholder="Ask the agent to assess, prepare, or advance the workflow..."
    Width="30rem"
    Height="68vh" />
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

As of 2026-03-20:

- the adapter-first runtime path is the default path
- semantic capabilities are a first-class authoring surface
- normalized execution, approval, policy, and context-freshness contracts are in place
- the old planner/runtime path is no longer the product center
- the demo is now led by orchestration workflows instead of primitive component control

The biggest remaining product gap is not UI execution. It is durable paid intelligence:

- persistent action history
- stronger cross-session memory
- mature adaptive workflow guidance

## Docs

- [Status](docs/STATUS.md)
- [Architecture](docs/architecture.md)
- [Runtime Realignment Plan](docs/runtime-realignment-plan.md)
- [Pricing Tiers](docs/pricing-tiers.md)
- [MudBlazor Compatibility Roadmap](docs/mudblazor-compatibility-roadmap.md)

## License

MIT
