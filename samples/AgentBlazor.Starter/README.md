# AgentBlazor Starter

Last updated: 2026-04-15

This is the current golden-path starter for AgentBlazor.

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

For a package-first app, the current preview entry path is:

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/ashpeterson/index.json" `
  --name github-agentblazor `
  --username YOUR_GITHUB_USERNAME `
  --password YOUR_GITHUB_PAT `
  --store-password-in-clear-text

dotnet new blazor
dotnet add package AgentBlazor --version 0.1.0-preview.10 --source github-agentblazor
```

Then copy the shape from:

- [Program.cs](Program.cs)
- [OpsReviewCapabilities.cs](Workflows/OpsReviewCapabilities.cs)
- [OpsReviewService.cs](Services/OpsReviewService.cs)
- [OpsReview.razor](Components/Pages/OpsReview.razor)

The local source-project mode exists only so this repo can build and validate the sample before packages are published to a public feed.

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

1. [OpsReviewCapabilities.cs](Workflows/OpsReviewCapabilities.cs)
2. [OpsReviewService.cs](Services/OpsReviewService.cs)
3. [OpsReview.razor](Components/Pages/OpsReview.razor)
4. the route prefix and agent description in [Program.cs](Program.cs)

## What It Proves

This starter is meant to prove the base product:

- route-scoped workflow registration with `AddWorkflow<T>()`
- structured capability results
- approval-gated workflow mutation
- persisted conversation and shared state
- in-app agent UX inside a live Blazor route

## Maintainer Note

Inside this repo, the starter defaults to local source-project references so the sample can build before packages are published.

To validate the package-first path from inside the repo:

```powershell
dotnet run --project samples/AgentBlazor.Starter/AgentBlazor.Starter.csproj -p:UseLocalAgentBlazorSource=false -p:AgentBlazorPackageVersion=0.1.0-preview.10
```
