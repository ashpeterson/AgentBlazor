# CLI v1 Real-App Validation - 2026-05-27

Purpose: evidence for the CLI v1 scope gate requiring real Blazor applications to produce useful `agentblazor analyze` reports without hallucinated method references.

Status: v1 validation complete. `agentblazor analyze` shipped as the CLI v1 line in `AgentBlazor.Cli` `0.2.5` on May 29 2026.

Environment:

- Source-validation branch: `feature/cli-v1-analyze-spine`
- Packaged-tool validation branch: `cli-v1-packaged-validation`
- Command shape: `dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- analyze <solution> --host <host> --output <report>`
- Packaged command shape: `agentblazor analyze <solution> --host <host> --output <report>`
- LLM provider: real OpenAI API key from environment
- Model: environment default, currently `gpt-4o-mini` when unset
- Temporary clone root: `/tmp/agentblazor-cli-v1-realapps`
- Initial packaged tool: locally packed `AgentBlazor.Cli.0.2.2.nupkg`, installed via `dotnet tool install AgentBlazor.Cli --tool-path /tmp/agentblazor-cli-v1-tool-smoke/tool --configfile /tmp/agentblazor-cli-v1-tool-smoke/NuGet.config --version 0.2.2`
- Final shipped packaged tool: NuGet.org `AgentBlazor.Cli` `0.2.5`
- Initial packaged tool output root: `/tmp/agentblazor-cli-v1-tool-smoke`
- Expanded real-app clone root: `/tmp/agentblazor-cli-v1-more-realapps`
- Final packaged tool output root after RCL route and semantic-validation fixes: `/tmp/agentblazor-cli-v1-tool-smoke-final2`
- Final NuGet-installed tool output root after publish: `/tmp/agentblazor-cli-v1-nuget-smoke-022`

## Packaged Tool Quality Gates

- Install gate: NuGet.org `AgentBlazor.Cli` `0.2.5` installs as a `dotnet tool` into an isolated tool path and reports `0.2.5` from `agentblazor --version`.
- No-provider non-interactive gate: with OpenAI and Azure OpenAI environment variables removed, `agentblazor analyze --non-interactive` exits cleanly with `No OpenAI API key configured for agentblazor analyze. Set OPENAI_API_KEY or OpenAI__ApiKey, and optionally set AGENTBLAZOR_ANALYZE_MODEL.`
- Static-only gate: `agentblazor analyze ... --static-only` writes a report with routes, capabilities, services, static workflow candidates, install readiness, and recommended next steps without requiring an LLM provider.
- Windows fallback gate: `AgentBlazor.Cli` `0.2.5` falls back to static source-file analysis when Roslyn `MSBuildWorkspace` fails with known MSBuild/Visual Studio assembly conflicts such as `Microsoft.Build.Shared.XMakeElements`.
- Report-quality gate: helper/object-method noise is filtered from developer-facing sections, the summary uses AgentBlazor action adoption, and mutating workflow suggestions include explicit `RequiresApproval = true` guidance.

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

## Expanded Real-App Matrix

### dotnet-architecture/eShopOnBlazor

- Repo: `https://github.com/dotnet-architecture/eShopOnBlazor`
- Solution: `eShopOnBlazor.sln`
- Host: `eShopOnBlazor`
- Packaged tool result: 5 routes, 2 developer-facing services, 7 discovered actions, 4 accepted, 0 rejected
- Outcome: pass. Standard Blazor Web App on `net8.0`; workflow suggestions reference real catalog service methods.

### immense/Remotely

- Repo: `https://github.com/immense/Remotely`
- Solution: `Remotely.sln`
- Host: `Server`
- Packaged tool result after stricter validation: 45 routes, 18 developer-facing services, 113 discovered actions, 5 accepted, 0 rejected
- Outcome: pass. Large production app with many routes and services; useful as a stress test for route volume and workflow candidate filtering.

### CuriousDrive/BlazingChat

- Repo: `https://github.com/CuriousDrive/BlazingChat`
- Solution: `src/BlazingChat.sln`
- Host: `BlazingChat.Server`
- Initial packaged result before RCL route fix: 0 routes, 2 developer-facing services, 2 discovered actions, 2 accepted, 0 rejected
- Final packaged result after RCL route and semantic-validation fixes: 10 routes, 1 developer-facing service, 2 discovered actions, 2 accepted, 2 rejected
- Outcome: pass with caveat. The app targets `net6.0` and uses legacy Blazor Server hosting, so readiness correctly flags unsupported target framework and legacy host shape.
- Fixes driven by this app:
  - Include routed Razor components from referenced Razor Class Library projects.
  - Exclude lifecycle/noise methods such as `Dispose` and `OnPropertyChanged` from developer-facing service reports and LLM prompts.
  - Validate LLM workflow suggestions against scored candidate actions, not every public method.
  - Reject suggestions whose referenced method terms do not align with the workflow description.

### Yu-Core/SwashbucklerDiary

- Repo: `https://github.com/Yu-Core/SwashbucklerDiary`
- Solution: `SwashbucklerDiary.Server.slnx`
- Host: `SwashbucklerDiary.Server`
- Initial packaged result before RCL route fix: 2 routes, 21 developer-facing services, 79 discovered actions, 5 accepted, 0 rejected
- Final packaged result after RCL route and semantic-validation fixes: 38 routes, 21 developer-facing services, 79 discovered actions, 3 accepted, 2 rejected
- Outcome: pass. This validates route discovery across a server host plus referenced RCL projects in a modern `net10.0` app.

### microsoft/FhirBlaze

- Repo: `https://github.com/microsoft/FhirBlaze`
- Solution: `FhirBlaze.sln`
- Host: `FhirBlaze`
- Initial packaged result before RCL route fix: 4 routes, 3 developer-facing services, 24 discovered actions, 5 accepted, 0 rejected
- Final packaged result after RCL route and semantic-validation fixes: 13 routes, 3 developer-facing services, 24 discovered actions, 3 accepted, 2 rejected
- Outcome: pass with caveat. Standalone WebAssembly host is reported as unsupported for install readiness, but analysis still discovers module routes and grounded workflow candidates.

### stavroskasidis/BlazorWithIdentity

- Repo: `https://github.com/stavroskasidis/BlazorWithIdentity`
- Solution: `template/BlazorWithIdentity.sln`
- Host: `BlazorWithIdentity.Client`
- Result: completed after temporary clone-only `global.json` roll-forward was added because the repo pins .NET SDK `7.0.100` and this environment has .NET 10 SDK installed.
- Packaged tool result: 5 routes, 1 developer-facing service, 4 discovered actions, 4 accepted, 0 rejected
- Outcome: pass with caveat. Initial run against `BlazorWithIdentity.Server` correctly reported the available Blazor host as `BlazorWithIdentity.Client`.

## Current Evidence

- Automated analysis tests pass: `163`
- CLI build passes: `dotnet build src/AgentBlazor.Cli/AgentBlazor.Cli.csproj`
- NuGet.org published package is available from `https://api.nuget.org/v3-flatcontainer/agentblazor.cli/0.2.5/agentblazor.cli.0.2.5.nupkg`.
- NuGet.org package install passes into an isolated tool path using a NuGet-only config, and `agentblazor --version` reports `0.2.5`.
- Packaged no-provider and `--static-only` paths pass.
- Real OpenAI-backed analysis completed on 11 real GitHub Blazor applications.
- Packaged `dotnet tool` smoke completed on 11 real GitHub Blazor applications.
- Final NuGet-installed `dotnet tool` smoke completed on 5 real GitHub Blazor applications with the real OpenAI provider.
- Final `0.2.5` eShopOnBlazor smoke completed with 5 routes, 1 developer-facing service after helper filtering, 7 discovered candidate actions, and mutating LLM suggestions that include approval guidance.
- Hallucinated methods were rejected on sparse apps.
- Accepted workflow suggestions on larger apps reference methods present in the static model.
- Referenced RCL routes are discovered for hosted/componentized apps.
- Semantically misaligned LLM suggestions are rejected even when they reference real methods.

## Final 0.2.5 Smoke

Environment:

- Tool version: `0.2.5`
- Command shape: `agentblazor analyze <solution> --host <host> --output <report>`
- Fallback diagnostic shape: `AGENTBLAZOR_STATIC_WORKSPACE=1 agentblazor analyze <solution> --host <host> --output <report>`
- LLM provider: real OpenAI API key from environment

Results:

| App | Host | Routes | Services | Actions | Suggestions | Outcome |
| --- | --- | ---: | ---: | ---: | --- | --- |
| dotnet-architecture/eShopOnBlazor | `eShopOnBlazor` | 5 | 1 | 7 discovered | 3 accepted, 0 rejected | Pass; helper noise filtered, candidate methods grounded in `CatalogService`, mutating suggestions include approval guidance. |

Additional command-path gates:

- Clean NuGet install from `https://api.nuget.org/v3/index.json` reports `agentblazor --version` as `0.2.5`.
- Forced static workspace fallback on eShopOnBlazor reports 5 routes, 1 developer-facing service, and 7 discovered candidate actions.
- Full LLM run on eShopOnBlazor reports validated workflow suggestions without hallucinated method references.

## Final NuGet-Installed Smoke

This section records the earlier `0.2.2` validation matrix. It remains useful as breadth evidence, but the shipped v1 package line is `0.2.5`.

Environment:

- Tool install root: `/tmp/agentblazor-cli-v1-nuget-smoke-022/tool`
- Report root: `/tmp/agentblazor-cli-v1-nuget-smoke-022/reports`
- Install command used only `https://api.nuget.org/v3/index.json` via an isolated `NuGet.config`
- Tool version: `0.2.2`
- LLM provider: real OpenAI API key from environment

Results:

| App | Host | Routes | Services | Actions | Readiness | Suggestions |
| --- | --- | ---: | ---: | ---: | --- | --- |
| CuriousDrive/BlazingChat | `BlazingChat.Server` | 10 | 1 | 2 discovered | 0 passed, 4 warnings, 6 missing | 2 accepted, 3 rejected |
| Yu-Core/SwashbucklerDiary | `SwashbucklerDiary.Server` | 38 | 21 | 79 discovered | 2 passed, 3 warnings, 5 missing | 1 accepted, 4 rejected |
| microsoft/FhirBlaze | `FhirBlaze` | 13 | 3 | 24 discovered | 0 passed, 4 warnings, 6 missing | 2 accepted, 3 rejected |
| immense/Remotely | `Server` | 45 | 18 | 113 discovered | 2 passed, 3 warnings, 5 missing | 1 accepted, 4 rejected |
| neozhu/CleanArchitectureWithBlazorServer | `Server.UI` | 31 | 21 | 19 discovered | 4 passed, 2 warnings, 4 missing | 2 accepted, 3 rejected |

Synthetic target refresh using the NuGet-installed tool:

| Target | Routes | Services | Actions | Readiness | Suggestions | Outcome |
| --- | ---: | ---: | ---: | --- | --- | --- |
| `simple-blazor-app` | 2 | 2 | 3 discovered | 2 passed, 3 warnings, 5 missing | 3 accepted, 0 rejected | Pass; useful new workflow suggestions were generated from services. |
| `realistic-blazor-app` | 3 | 3 | 4 confirmed, 6 discovered | 10 passed, 0 warnings, 0 missing | 0 accepted, 4 rejected | Pass; suggestions targeted methods that already have confirmed AgentBlazor actions, so duplicate workflow suggestions were correctly rejected. |
| `hosted-wasm-app` | 1 | 1 | 2 confirmed, 2 discovered | 4 passed, 5 warnings, 1 missing | 0 accepted, 3 rejected | Pass; suggestions targeted methods that already have confirmed AgentBlazor actions, so duplicate workflow suggestions were correctly rejected. |

Additional command-path gates:

- No-provider non-interactive path exits cleanly with: `No OpenAI API key configured for agentblazor analyze. Set OPENAI_API_KEY or OpenAI__ApiKey, and optionally set AGENTBLAZOR_ANALYZE_MODEL.`
- No-provider interactive path prompts for an OpenAI API key, masks the input, and uses it for the current run without writing it to disk.
- `--static-only` works without an LLM provider and writes an analysis report.
- Rejected suggestions remain visible in the report for now as validation evidence. Hiding them by default is a future UX decision, not a v1 ship blocker.
