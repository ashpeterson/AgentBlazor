# CLI v1 Real-App Validation - 2026-05-27

Purpose: evidence for the CLI v1 scope gate requiring real Blazor applications to produce useful `agentblazor analyze` reports without hallucinated method references.

Environment:

- Source-validation branch: `feature/cli-v1-analyze-spine`
- Packaged-tool validation branch: `cli-v1-packaged-validation`
- Command shape: `dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- analyze <solution> --host <host> --output <report>`
- Packaged command shape: `agentblazor analyze <solution> --host <host> --output <report>`
- LLM provider: real OpenAI API key from environment
- Model: environment default, currently `gpt-4o-mini` when unset
- Temporary clone root: `/tmp/agentblazor-cli-v1-realapps`
- Packaged tool: locally packed `AgentBlazor.Cli.0.2.1.nupkg`, installed via `dotnet tool install AgentBlazor.Cli --tool-path /tmp/agentblazor-cli-v1-tool-smoke/tool --configfile /tmp/agentblazor-cli-v1-tool-smoke/NuGet.config --version 0.2.1`
- Packaged tool output root: `/tmp/agentblazor-cli-v1-tool-smoke`

## Packaged Tool Quality Gates

- Install gate: local `AgentBlazor.Cli.0.2.1.nupkg` installs as a `dotnet tool` into an isolated tool path and reports `0.2.1` from `agentblazor --version`.
- No-provider gate: with OpenAI and Azure OpenAI environment variables removed, `agentblazor analyze` exits cleanly with `No OpenAI API key configured for agentblazor analyze. Set OPENAI_API_KEY or OpenAI__ApiKey, and optionally set AGENTBLAZOR_ANALYZE_MODEL.`
- Static-only gate: `agentblazor analyze ... --static-only` writes a report with routes, capabilities, services, static workflow candidates, install readiness, and recommended next steps without requiring an LLM provider.

## Applications Tested

### damienbod/BlazorSecurityNet10

- Repo: `https://github.com/damienbod/BlazorSecurityNet10`
- Solution: `BlazorApp.sln`
- Host: `BlazorApp`
- Result: completed with real OpenAI call
- Summary: 4 routes, 0 developer-facing services, 0 discovered actions
- LLM result: 0 accepted, 3 rejected
- Packaged tool result: 4 routes, 0 developer-facing services, 0 discovered actions, 0 accepted, 3 rejected
- Outcome: pass. The app is sparse and the LLM suggested non-existent services, but all hallucinated method references were rejected.

### neozhu/CleanArchitectureWithBlazorServer

- Repo: `https://github.com/neozhu/CleanArchitectureWithBlazorServer`
- Solution: `CleanArchitecture.Blazor.slnx`
- Host: `Server.UI`
- Result: completed with real OpenAI call
- Summary after filtering: 31 routes, 10 developer-facing services, 18 discovered actions
- LLM result: 5 accepted, 0 rejected
- Packaged tool result: 31 routes, 10 developer-facing services, 18 discovered actions, 5 accepted, 0 rejected
- Fixes driven by this app:
  - Filter UI/helper infrastructure services from reports and LLM prompts.
  - Remove recommendations for filtered actions.
  - Disambiguate duplicate static headings such as `Export`.
  - Align terminal summary counts with filtered report counts.

### fullstackhero/blazor-starter-kit

- Repo: `https://github.com/fullstackhero/blazor-starter-kit`
- Solution: `BlazorHero.CleanArchitecture.sln`
- Host: `Client`
- Result: completed with real OpenAI call
- Summary: 21 routes, 16 developer-facing services, 59 discovered actions
- LLM result: 5 accepted, 0 rejected
- Packaged tool result: 21 routes, 15 developer-facing services, 57 discovered actions, 5 accepted, 0 rejected
- Outcome: pass with caveat. The app targets `net6.0`, so install readiness correctly flags unsupported target framework. Workflow suggestions reference real manager methods.

### enkodellc/blazorboilerplate

- Repo: `https://github.com/enkodellc/blazorboilerplate`
- Solution: `src/BlazorBoilerplate.sln`
- Host: `BlazorBoilerplate.Server`
- Result: completed with real OpenAI call
- Summary after filtering: 1 route, 5 developer-facing services, 36 discovered actions
- LLM result after parser fix: 5 accepted, 0 rejected
- Packaged tool result: 1 route, 5 developer-facing services, 36 discovered actions, 5 accepted, 0 rejected
- Fixes driven by this app:
  - Normalize LLM method references that include parameter lists.
  - Clarify prompt instructions so JSON method fields use method names only.
  - Filter controllers, factories, providers, DbContexts, and UI notifiers from the developer-facing service list.

### davidfowl/TodoApp

- Repo: `https://github.com/davidfowl/TodoApp`
- Solution: `TodoApp.sln`
- Host: `Todo.Web.Server`
- Result: completed with real OpenAI call after temporary clone-only `global.json` roll-forward changed from `latestFeature` to `latestMajor` because this environment only has .NET 10 SDK installed.
- Summary: 1 route, 4 developer-facing services, 9 discovered actions
- LLM result: 4 accepted, 0 rejected
- Packaged tool result: 1 route, 4 developer-facing services, 9 discovered actions, 4 accepted, 0 rejected
- Fixes driven by this app:
  - Report SDK-resolution failures as SDK setup problems rather than opaque unexpected exceptions.
  - Resolve target frameworks inherited from `Directory.Build.props`.

## Current Evidence

- Automated analysis tests pass: `160`
- CLI build passes: `dotnet build src/AgentBlazor.Cli/AgentBlazor.Cli.csproj`
- Local packaged tool install passes from `AgentBlazor.Cli.0.2.1.nupkg`.
- Packaged no-provider and `--static-only` paths pass.
- Real OpenAI-backed analysis completed on 5 real GitHub Blazor applications.
- Packaged `dotnet tool` smoke completed on the same 5 real GitHub Blazor applications.
- Hallucinated methods were rejected on sparse apps.
- Accepted workflow suggestions on larger apps reference methods present in the static model.

## Remaining Before v1 Complete

- Publish or otherwise cut the CLI package from a green commit.
- Run final NuGet-installed tool smoke after publish.
- Decide whether the report should hide rejected suggestions by default or keep them as validation evidence.
