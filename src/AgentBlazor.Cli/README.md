# AgentBlazor CLI

`agentblazor` is meant to onboard an existing Blazor app through one standard path:

1. `agentblazor init`
2. `agentblazor scaffold`
3. `agentblazor scaffold --approve`
4. `agentblazor doctor`
5. `agentblazor validate`

Current status as of 2026-04-09:

- CLI analysis tests: `126/126`
- CLI integration tests: `9/9`
- standard existing Blazor hosts are scaffoldable end to end
- standard hosted WebAssembly server+client hosts are scaffoldable end to end
- advanced/custom hosts still fall back to review-first or blocked modes depending on how confidently the CLI can classify them

## Commands

- `agentblazor init ./MySolution.slnx --host MyBlazorApp`
- `agentblazor doctor ./MySolution.slnx --host MyBlazorApp`
- `agentblazor validate ./MySolution.slnx --host MyBlazorApp`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --diff`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --diff --use-local-source /path/to/AgentBlazor`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --provider openai --approve`
- `agentblazor update`
- `agentblazor watch`

## What It Does

- scans routes, services, and agent-exposable surfaces
- generates `.agentblazor/AGENT.md`
- inspects whether the baseline AgentBlazor runtime wiring exists in an app
- validates the current install state and scaffold audit trail when available
- shows installer-style next steps from `init`
- previews exact file-level baseline install edits for a standard Blazor host
- applies the baseline install for a standard Blazor host and the standard hosted WebAssembly server+client path, and writes `.agentblazor/scaffold-manifest.json`
- scaffolds provider-specific `Program.cs` registration for `openai`, `azure-openai`, or `ollama`
- helps validate what the agent can see in the current app

When the CLI is run from a local AgentBlazor checkout it will auto-detect the source tree and scaffold `ProjectReference`s instead of an `AgentBlazor` package reference. You can also force that path with `--use-local-source /path/to/AgentBlazor`.

## What It Does Not Do

- it does not safely patch arbitrary nonstandard hosts yet
- it does not populate provider secrets or app-specific configuration values for you

Use the main quickstart for runtime setup:

- `docs/quickstart.md`
