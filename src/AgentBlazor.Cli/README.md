# AgentBlazor CLI

`agentblazor` analyzes a Blazor solution and generates `.agentblazor/AGENT.md`.

## Commands

- `agentblazor init ./MySolution.sln --host MyBlazorApp`
- `agentblazor update`
- `agentblazor watch`

## What It Does

- scans routes, services, and agent-exposable surfaces
- generates `.agentblazor/AGENT.md`
- helps validate what the agent can see in the current app

## What It Does Not Do

- it does not add the AgentBlazor package
- it does not wire `AddAgentBlazor(...)`
- it does not map `app.MapAgentBlazorEndpoints()`
- it does not add chat components to your app

Use the main quickstart for runtime setup:

- `docs/quickstart.md`
