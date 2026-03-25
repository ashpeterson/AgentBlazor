# AgentBlazor Starter

This is the current golden-path free starter for AgentBlazor.

It is intentionally small:

- one route
- one workflow agent
- one capability class
- one service-backed state model
- one approval boundary
- one embedded chat surface

## Run It

From the repo root:

```powershell
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj
```

Open:

- `/`
- `/ops-review`

## Canonical Quickstart

For a package-first app, the intended entry path is:

```powershell
dotnet new blazor
dotnet add package AgentBlazor
```

Then copy the shape from:

- [Program.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Program.cs)
- [OpsReviewCapabilities.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs)
- [OpsReviewService.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Services/OpsReviewService.cs)
- [OpsReview.razor](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor)

The package-first path is the public path. The local source-project mode exists only so this repo can build and validate the sample before packages are published.

## Provider Setup

The starter supports two provider paths:

1. `OpenAI`
2. `Ollama`

The app prefers OpenAI when `OPENAI_API_KEY` is present.
If that is not configured, it falls back to Ollama when `OLLAMA_MODEL` or `Ollama:Model` is configured.

Useful environment variables:

```powershell
$env:OPENAI_API_KEY="sk-..."
$env:OLLAMA_MODEL="llama3.2"
$env:OLLAMA_ENDPOINT="http://127.0.0.1:11434/v1"
```

## What To Replace First

When copying this into a real app, replace these in order:

1. [OpsReviewCapabilities.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs)
2. [OpsReviewService.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Services/OpsReviewService.cs)
3. [OpsReview.razor](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor)
4. the route prefix and agent description in [Program.cs](C:/Git/repos/agentblazor/samples/AgentBlazor.Starter/Program.cs)

## What It Proves

This starter is meant to prove the free-plan value:

- route-scoped workflow registration with `AddWorkflow<T>()`
- structured capability results
- approval-gated workflow mutation
- persisted conversation and shared state
- in-app agent UX inside a live Blazor route

## Maintainer Note

Inside this repo, the starter defaults to local source-project references so the sample can build before packages are published.

To validate the package-first path from inside the repo:

```powershell
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj -p:UseLocalAgentBlazorSource=false -p:AgentBlazorPackageVersion=0.1.0-preview
```
