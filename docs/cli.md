# CLI Guide

The CLI is optional, but it is useful when you want the agent to understand your app structure and generate a local `.agentblazor/AGENT.md`.

The CLI does not replace runtime wiring. You still need `AddAgentBlazor(...)`, a workflow registration, a chat surface, and `app.MapAgentBlazorEndpoints()`.

## Install

```bash
dotnet tool install --global AgentBlazor.Cli --prerelease
```

If you already have it:

```bash
dotnet tool update --global AgentBlazor.Cli --prerelease
```

## Initialize A Project

From your solution root:

```bash
agentblazor init ./MySolution.sln --host MyBlazorApp
```

The CLI currently expects a traditional `.sln` file. If your repo only has `.slnx`, create a classic solution file first:

```bash
dotnet new sln --format sln -n MySolution
```

That generates:

- `.agentblazor/AGENT.md`
- `.agentblazor/state.json`

Use `--host` to point the CLI at the Blazor host project inside a larger solution.

## Refresh After Code Changes

```bash
agentblazor update
```

## Watch During Development

```bash
agentblazor watch
```

## What The CLI Is For

- discovers routes, services, and agent-exposable surfaces
- generates an `AGENT.md` summary for the app
- helps validate what the agent can see in the current solution

## What The CLI Is Not For

- it does not add the AgentBlazor package
- it does not call `AddAgentBlazor(...)`
- it does not map AG-UI endpoints
- it does not add `AgentChatWidget` or `AgentChatSurface`
- it does not create your capability classes for you

## Recommended Workflow

1. Add the package and host wiring.
2. Register one workflow with `AddWorkflow<T>()`.
3. Add one chat surface to the route you want to validate.
4. Run `agentblazor init` to generate `.agentblazor/AGENT.md`.
5. Use `agentblazor update` as your app changes.

## Example

The runnable reference app is:

- `samples/AgentBlazor.Starter`

The most important files are:

- [Program.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Program.cs)
- [OpsReviewCapabilities.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs)
- [OpsReview.razor](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor)
