# CLI Guide

Status as of 2026-04-20:

- `AgentBlazor.Cli.Analysis.Tests`: `135/135`
- `AgentBlazor.Cli.IntegrationTests`: `9/9` discovered locally, skipped without an API key
- standard Blazor hosts are fully scaffoldable
- hosted WebAssembly server hosts can be scaffolded for server startup/workflow wiring; browser-client layout/assets/providers/chat remain explicit manual-review work
- scaffolded MudBlazor and AgentBlazor assets preserve existing `nonce="..."` attributes in CSP-aware shells
- Central Package Management apps are supported for package scaffolding: project files receive unversioned `PackageReference` entries and the nearest active `Directory.Packages.props` receives matching `PackageVersion` entries
- advanced/custom hosts remain review-first unless the CLI can safely classify and patch them

The CLI is designed to take an existing Blazor app through a standard onboarding path:

1. `agentblazor init`
2. `agentblazor scaffold`
3. `agentblazor scaffold --approve`
4. `agentblazor doctor`
5. `agentblazor validate`

That flow is meant to be predictable for any existing Blazor app. A developer should not have to infer the required setup steps manually.

## Install

For private-preview testing, install an exact CLI version from the same feed/version as the runtime package you add to the app. This avoids a CLI/runtime mismatch where scaffold generates `AgentBlazor.App` workflow code but the app restores an older runtime package.

```bash
dotnet tool install --global AgentBlazor.Cli --version 0.1.0-preview.8 --add-source https://nuget.pkg.github.com/ashpeterson/index.json
```

If you already have it:

```bash
dotnet tool update --global AgentBlazor.Cli --version 0.1.0-preview.8 --add-source https://nuget.pkg.github.com/ashpeterson/index.json
```

Add the matching runtime package to the host app before or during scaffold validation:

```bash
dotnet add ./MyBlazorApp/MyBlazorApp.csproj package AgentBlazor --version 0.1.0-preview.8 --source https://nuget.pkg.github.com/ashpeterson/index.json
```

## Standard Flow

The standard flow assumes the app and CLI are on the same AgentBlazor preview version.

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

Add `--provider openai` for the most validated default path. Use `--provider azure-openai` for Azure OpenAI or `--provider ollama` for local OpenAI-compatible Ollama.

Show the exact file-level diff before applying:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff
```

Azure OpenAI scaffold:

```bash
agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider azure-openai --diff
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

If the host uses Central Package Management with `ManagePackageVersionsCentrally=true`, scaffold keeps the project file valid by adding unversioned `PackageReference` entries and writing missing `PackageVersion` entries to the nearest imported `Directory.Packages.props`. This path was validated against `thecodewrapper/CH.CleanArchitectureBlazor`, a .NET 10 Blazor Server app with a legacy `Startup.cs`/`_Host.cshtml` host shape.

If you pass `--provider`, scaffold writes the matching provider registration into `Program.cs` and leaves only the configuration values for you to supply. Azure OpenAI uses `AzureOpenAI:Endpoint`, `AzureOpenAI:DeploymentName`, and `AzureOpenAI:ApiKey` by default; apps that use managed identity can replace the scaffolded API-key argument with a `TokenCredential` such as `new DefaultAzureCredential()`. If you omit `--provider`, scaffold leaves concrete OpenAI, Azure OpenAI, and Ollama examples in comments.

If the CLI detects an advanced or legacy Blazor host, scaffold now stays review-first where safety requires it: it previews safe file additions such as package/workflow changes and downgrades risky host-specific wiring to manual review. Oqtane, legacy `_Host.cshtml` server apps, and hosted WebAssembly server hosts are examples, but the path is meant to cover recognizable nonstandard Blazor hosts more broadly. For hosted WebAssembly servers, the CLI infers the companion client project from project references and patches the standard server `Program.cs` startup path, but browser-client `_Imports.razor`, `wwwroot/index.html`, layout/provider, and chat surface edits remain manual-review. The supported browser-client path is `AgentBlazor.Client` plus `MapAgentBlazorRemoteChat()` on the server, with `AgentRemoteChatWidget`, `AgentRemoteChatSurface`, `AgentRemoteChatPanel`, or `AgentRemoteChatBar` mounted in the client. The remote widget supports `CssClass` and `Style` overrides so a host can avoid fixed footer/action-bar collisions. Only hosts the CLI cannot classify into a Blazor scaffold path still stop early.

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

- it does not guarantee the runtime package and CLI versions match unless you install/pin them explicitly
- it does not yet patch arbitrary nonstandard hosts safely
- it does not generate provider secrets or environment-specific configuration for you

## Version Mismatch Symptoms

If a scaffolded app fails with errors such as:

- `The type or namespace name 'App' does not exist in the namespace 'AgentBlazor'`
- `The type or namespace name 'CapabilityResult' could not be found`
- `AgentBlazorBuilder does not contain a definition for AddWorkflow`

then the app is compiling against a stale or mismatched AgentBlazor package. Pin `AgentBlazor` and `AgentBlazor.Cli` to the same preview version, delete `bin`/`obj`, clear the cached `agentblazor` package folder, and restore with `--force-evaluate`.

## Recommended Workflow

1. Install matching `AgentBlazor` and `AgentBlazor.Cli` package versions.
2. Run `agentblazor init` to generate `.agentblazor/AGENT.md` and get the next setup commands.
3. Run `agentblazor scaffold --provider openai` to preview the baseline install edits with the validated provider path.
4. Run `agentblazor scaffold --provider openai --approve` once you are satisfied with the preview.
5. Run `agentblazor doctor` to verify the resulting setup.
6. Run `agentblazor validate` to verify the install state and scaffold audit trail.
7. Use `agentblazor update` as your app changes.

## Example

The runnable reference app is:

- `samples/AgentBlazor.Starter`

The most important files are:

- [Program.cs](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Program.cs)
- [OpsReviewCapabilities.cs](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs)
- [OpsReview.razor](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor)
