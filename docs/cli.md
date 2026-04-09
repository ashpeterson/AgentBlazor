# CLI Guide

Status as of 2026-04-09:

- `AgentBlazor.Cli.Analysis.Tests`: `126/126`
- `AgentBlazor.Cli.IntegrationTests`: `9/9`
- standard Blazor hosts are fully scaffoldable
- standard hosted WebAssembly server+client hosts are fully scaffoldable
- advanced/custom hosts remain review-first unless the CLI can safely classify and patch them

The CLI is designed to take an existing Blazor app through a standard onboarding path:

1. `agentblazor init`
2. `agentblazor scaffold`
3. `agentblazor scaffold --approve`
4. `agentblazor doctor`
5. `agentblazor validate`

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
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai
```

Review exact file-level changes when needed:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
```

Apply the baseline install:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
```

Verify the app:

```bash
agentblazor doctor ./MySolution.slnx --host MyBlazorApp
```

Then validate the install state and scaffold audit trail:

```bash
agentblazor validate ./MySolution.slnx --host MyBlazorApp
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

## Validate An Install

Use `validate` after install work when you want a higher-level check than `doctor`:

```bash
agentblazor validate ./MySolution.slnx --host MyBlazorApp
```

`validate` combines:

- the baseline readiness checks from `doctor`
- scaffold manifest verification when `.agentblazor/scaffold-manifest.json` exists
- a file audit to confirm scaffold-tracked files still exist

If the app was installed manually and no manifest exists, `validate` reports that as a warning rather than a failure.

## Preview A Scaffold Plan

`scaffold` is preview-first by default. Running it with no flags does not mutate files:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp
```

Add `--provider openai` for the validated default path. `azure-openai` and `ollama` are also supported.

Show the exact file-level diff before applying:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
```

When you are evaluating from a local AgentBlazor checkout, you can install against source projects instead of a published package:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff --use-local-source /path/to/AgentBlazor
```

Apply the standard-host scaffold:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve
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

If you pass `--provider`, scaffold writes the matching provider registration into `Program.cs` and leaves only the configuration values for you to supply. If you omit `--provider`, scaffold leaves concrete OpenAI, Azure OpenAI, and Ollama examples in comments.

If the CLI detects an advanced or legacy Blazor host, scaffold now stays review-first: it previews safe file additions such as package/workflow changes and downgrades risky host-specific wiring to manual review. Oqtane, legacy `_Host.cshtml` server apps, and hosted WebAssembly server hosts are examples, but the path is meant to cover recognizable nonstandard Blazor hosts more broadly. For hosted WebAssembly servers, the CLI now infers the companion client project from project references, patches the standard server `Program.cs` startup path, and can patch safe client files such as `_Imports.razor`, `wwwroot/index.html`, layout, and page files there. Only hosts the CLI cannot classify into a Blazor scaffold path still stop early.

## What The CLI Is For

- discovers routes, services, and agent-exposable surfaces
- generates an `AGENT.md` summary for the app
- helps validate what the agent can see in the current solution
- reports whether an existing app has the baseline AgentBlazor wiring in place
- validates the current install state plus scaffold audit trail when available
- previews the exact baseline install edits for standard Blazor hosts
- applies the standard-host baseline install with a written manifest
- can scaffold provider-specific `Program.cs` registration with `--provider openai|azure-openai|ollama`

## What The CLI Is Not For

- it does not add the AgentBlazor package
- it does not yet patch arbitrary nonstandard hosts safely
- it does not generate provider secrets or environment-specific configuration for you

## Recommended Workflow

1. Run `agentblazor init` to generate `.agentblazor/AGENT.md` and get the next setup commands.
2. Run `agentblazor scaffold --provider openai` to preview the baseline install edits with the validated provider path.
3. Run `agentblazor scaffold --provider openai --approve` once you are satisfied with the preview.
4. Run `agentblazor doctor` to verify the resulting setup.
5. Run `agentblazor validate` to verify the install state and scaffold audit trail.
6. Use `agentblazor update` as your app changes.

## Example

The runnable reference app is:

- `samples/AgentBlazor.Starter`

The most important files are:

- [Program.cs](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Program.cs)
- [OpsReviewCapabilities.cs](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs)
- [OpsReview.razor](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor)
