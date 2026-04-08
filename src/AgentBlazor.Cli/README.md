# AgentBlazor CLI

`agentblazor` is meant to onboard an existing Blazor app through one standard path:

1. `agentblazor init`
2. `agentblazor scaffold`
3. `agentblazor scaffold --approve`
4. `agentblazor doctor`

## Commands

- `agentblazor init ./MySolution.slnx --host MyBlazorApp`
- `agentblazor doctor ./MySolution.slnx --host MyBlazorApp`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --diff`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --diff --use-local-source /path/to/AgentBlazor`
- `agentblazor scaffold ./MySolution.slnx --host MyBlazorApp --approve`
- `agentblazor update`
- `agentblazor watch`

## What It Does

- scans routes, services, and agent-exposable surfaces
- generates `.agentblazor/AGENT.md`
- inspects whether the baseline AgentBlazor runtime wiring exists in an app
- shows installer-style next steps from `init`
- previews exact file-level baseline install edits for a standard Blazor host
- applies the baseline install for a standard Blazor host and writes `.agentblazor/scaffold-manifest.json`
- helps validate what the agent can see in the current app

When the CLI is run from a local AgentBlazor checkout it will auto-detect the source tree and scaffold `ProjectReference`s instead of an `AgentBlazor` package reference. You can also force that path with `--use-local-source /path/to/AgentBlazor`.

## What It Does Not Do

- it does not safely patch arbitrary nonstandard hosts yet
- it does not fully automate provider selection or secret management

Use the main quickstart for runtime setup:

- `docs/quickstart.md`
