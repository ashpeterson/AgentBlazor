# CLI Guide

The CLI is designed to take an existing Blazor app through a standard onboarding path:

1. `agentblazor init`
2. `agentblazor scaffold`
3. `agentblazor scaffold --approve`
4. `agentblazor doctor`

That flow is meant to be predictable for any existing Blazor app. A developer should not have to infer the required setup steps manually.

## Install

```bash
dotnet tool install --global AgentBlazor.Cli --prerelease
```

If you already have it:

```bash
dotnet tool update --global AgentBlazor.Cli --prerelease
```

## Standard Flow

Start with `init`:

```bash
agentblazor init ./MySolution.slnx --host MyBlazorApp
```

`init` now does two things:

- generates `.agentblazor/AGENT.md` and `.agentblazor/state.json`
- shows an installer-style setup summary with the exact next commands to run

Then run scaffold preview:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp
```

Review exact file-level changes when needed:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --diff
```

Apply the baseline install:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --approve
```

Verify the app:

```bash
agentblazor doctor ./MySolution.slnx --host MyBlazorApp
```

## Initialize A Project

From your solution root:

```bash
agentblazor init ./MySolution.slnx --host MyBlazorApp
```

The CLI accepts:

- `.sln`
- `.slnx`
- `.csproj`

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

## Inspect Installation Readiness

Use `doctor` to verify what is already installed or to confirm the app after scaffold runs:

```bash
agentblazor doctor ./MySolution.slnx --host MyBlazorApp
```

The command currently checks for:

- `AgentBlazor` and `MudBlazor` references
- `AddMudServices()`
- `AddAgentBlazor(...)`
- `AddWorkflow<T>()`
- `MapAgentBlazorEndpoints()`
- shell asset references
- MudBlazor layout providers
- a mounted chat surface

This command is intentionally non-mutating. It reports gaps before you run the scaffold step.

## Preview A Scaffold Plan

`scaffold` is preview-first by default. Running it with no flags does not mutate files:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp
```

Show the exact file-level diff before applying:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --diff
```

When you are evaluating from a local AgentBlazor checkout, you can install against source projects instead of a published package:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --diff --use-local-source /path/to/AgentBlazor
```

Apply the standard-host scaffold:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --approve
```

The CLI will also auto-detect a local AgentBlazor source checkout when you run it from this repository, and use local project references for scaffolded installs.

The current scaffold slice handles standard host files and proposes or applies edits for:

- package references
- `Program.cs`
- `App.razor`
- `MainLayout.razor`
- a starter workflow file
- a chat entry point page

When scaffold applies changes it also writes `.agentblazor/scaffold-manifest.json` in the host project so the install step has an audit trail.

It still expects you to connect a model provider in `Program.cs` after scaffolding.

## What The CLI Is For

- discovers routes, services, and agent-exposable surfaces
- generates an `AGENT.md` summary for the app
- helps validate what the agent can see in the current solution
- reports whether an existing app has the baseline AgentBlazor wiring in place
- previews the exact baseline install edits for standard Blazor hosts
- applies the standard-host baseline install with a written manifest

## What The CLI Is Not For

- it does not add the AgentBlazor package
- it does not yet patch arbitrary nonstandard hosts safely
- it does not fully automate provider selection or secret management

## Recommended Workflow

1. Run `agentblazor init` to generate `.agentblazor/AGENT.md` and get the next setup commands.
2. Run `agentblazor scaffold` to preview the baseline install edits.
3. Run `agentblazor scaffold --approve` once you are satisfied with the preview.
4. Run `agentblazor doctor` to verify the resulting setup.
5. Use `agentblazor update` as your app changes.

## Example

The runnable reference app is:

- `samples/AgentBlazor.Starter`

The most important files are:

- [Program.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Program.cs)
- [OpsReviewCapabilities.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs)
- [OpsReview.razor](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor)
