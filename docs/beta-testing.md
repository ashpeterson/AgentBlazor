# Beta Testing

Use this guide if you are testing AgentBlazor before launch.

Hosted demo: https://demo.agentblazor.com/demo/workflows/support-inbox

## Goal

Prove four things:

1. the package installs cleanly
2. the app builds cleanly
3. one support route works end to end
4. the first prompt and approval step make sense to a new developer

## 20-Minute Test Path

Use a fresh app first. Do not start with an existing codebase.

```bash
dotnet new blazor -o FreshAgentBlazor
cd FreshAgentBlazor
dotnet add package AgentBlazor --version 0.2.0-preview.2
```

Then follow:

- [Quickstart](quickstart.md)

Use the support-inbox shape only:

- one workflow
- one chat surface
- one approval-gated action

Compare against the hosted support-inbox demo if you want a known-running reference:

- https://demo.agentblazor.com/demo/workflows/support-inbox

## Exact Checks

Please report pass or fail for each of these:

1. `dotnet add package AgentBlazor --version 0.2.0-preview.2`
2. `dotnet build`
3. app starts with AgentBlazor assets loaded
4. chat surface opens
5. one prompt visibly changes the page
6. approval flow is understandable

## Prompts To Try

Use these exact prompts first:

- `Show open tickets from this week`
- `Explain why they need attention`
- `Draft a reply for the highlighted tickets`
- `Escalate the blocked tickets`

## What To Report

The most useful report includes:

- the exact command that failed
- the first confusing sentence in the docs
- the first point where you stopped trusting the install
- whether the support-inbox story felt obvious or abstract
- whether the approval step made sense

## Report Links

Use one of these issue forms:

- [Install friction](https://github.com/ashpeterson/AgentBlazor/issues/new?template=install-friction.yml)
- [Beta feedback](https://github.com/ashpeterson/AgentBlazor/issues/new?template=beta-feedback.yml)

Include:

- your .NET SDK version
- app type: fresh app or existing app
- host model: server, Web App, or hosted WebAssembly
- provider: OpenAI, Azure OpenAI, Ollama, or none
- exact failure text or screenshot
