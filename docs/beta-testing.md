# Beta Testing

Use this guide if you are testing AgentBlazor before launch.

## Goal

Prove three things:

1. the package installs cleanly
2. one Blazor route works end to end
3. the support-inbox demo story makes sense to a new developer

## Fastest Test Path

Create a fresh app:

```bash
dotnet new blazor -o FreshAgentBlazor
cd FreshAgentBlazor
dotnet add package AgentBlazor --version 0.1.0-preview.10
```

Then follow:

- [Quickstart](quickstart.md)

Use the support-inbox shape:

- one workflow
- one chat surface
- one approval-gated action

## Minimum Checks

Please report whether each of these worked:

1. `dotnet add package AgentBlazor --version 0.1.0-preview.10`
2. `dotnet build`
3. app renders with the AgentBlazor assets loaded
4. chat surface opens
5. one prompt produces a visible result on screen
6. approval flow is understandable

## Suggested Prompts

- `Show open tickets from this week`
- `Explain why they need attention`
- `Draft a reply for the highlighted tickets`
- `Escalate the blocked tickets`

## What Feedback Is Most Useful

- the exact command that failed
- the first confusing sentence in the docs
- the first point where you stopped trusting the install
- whether the support-inbox demo felt obvious or abstract
- whether the approval step made sense

## Where To Report It

Open a GitHub issue and include:

- your .NET SDK version
- app type: fresh app or existing app
- host model: server, Web App, or hosted WebAssembly
- provider: OpenAI, Azure OpenAI, Ollama, or none
- exact failure text or screenshot

If the problem is install friction, label it clearly as install/setup friction.
