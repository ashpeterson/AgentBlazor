# CLI v1 Real-App Validation - 2026-05-27

Purpose: evidence for the CLI v1 scope gate requiring real Blazor applications to produce useful `agentblazor analyze` reports without hallucinated method references.

Environment:

- Branch: `feature/cli-v1-analyze-spine`
- Command shape: `dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- analyze <solution> --host <host> --output <report>`
- LLM provider: real OpenAI API key from environment
- Model: environment default, currently `gpt-4o-mini` when unset
- Temporary clone root: `/tmp/agentblazor-cli-v1-realapps`

## Applications Tested

### damienbod/BlazorSecurityNet10

- Repo: `https://github.com/damienbod/BlazorSecurityNet10`
- Solution: `BlazorApp.sln`
- Host: `BlazorApp`
- Result: completed with real OpenAI call
- Summary: 4 routes, 0 developer-facing services, 0 discovered actions
- LLM result: 0 accepted, 3 rejected
- Outcome: pass. The app is sparse and the LLM suggested non-existent services, but all hallucinated method references were rejected.

### neozhu/CleanArchitectureWithBlazorServer

- Repo: `https://github.com/neozhu/CleanArchitectureWithBlazorServer`
- Solution: `CleanArchitecture.Blazor.slnx`
- Host: `Server.UI`
- Result: completed with real OpenAI call
- Summary after filtering: 31 routes, 10 developer-facing services, 18 discovered actions
- LLM result: 5 accepted, 0 rejected
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
- Outcome: pass with caveat. The app targets `net6.0`, so install readiness correctly flags unsupported target framework. Workflow suggestions reference real manager methods.

### enkodellc/blazorboilerplate

- Repo: `https://github.com/enkodellc/blazorboilerplate`
- Solution: `src/BlazorBoilerplate.sln`
- Host: `BlazorBoilerplate.Server`
- Result: completed with real OpenAI call
- Summary after filtering: 1 route, 5 developer-facing services, 36 discovered actions
- LLM result after parser fix: 5 accepted, 0 rejected
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
- Fixes driven by this app:
  - Report SDK-resolution failures as SDK setup problems rather than opaque unexpected exceptions.
  - Resolve target frameworks inherited from `Directory.Build.props`.

## Current Evidence

- Automated analysis tests pass: `160`
- CLI build passes: `dotnet build src/AgentBlazor.Cli/AgentBlazor.Cli.csproj`
- Real OpenAI-backed analysis completed on 5 real GitHub Blazor applications.
- Hallucinated methods were rejected on sparse apps.
- Accepted workflow suggestions on larger apps reference methods present in the static model.

## Remaining Before v1 Complete

- PR CI must pass.
- Merge PR #64 after review.
- Run final published-tool smoke once the CLI package is cut.
- Decide whether the report should hide rejected suggestions by default or keep them as validation evidence.
