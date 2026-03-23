# NuGet Prerelease Checklist

Last updated: 2026-03-20

Use this before publishing an `AgentBlazor` prerelease for real-project validation.

## Goal

Ship a package that:

- restores cleanly in a fresh Blazor app
- exposes the expected static assets and component assemblies
- does not overclaim full MudBlazor parity before richer real-project proof exists

## Minimum Bar

1. Run the focused test gates:
   - `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj -nologo /p:UseSharedCompilation=false`
   - `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj -nologo /p:UseSharedCompilation=false`
2. Run the public demo/browser gate:
   - `npm --prefix tests/e2e run test:e2e`
3. Pack the prerelease:
   - `dotnet pack src/AgentBlazor.Components/AgentBlazor.Components.csproj -nologo -c Release /p:UseSharedCompilation=false /p:PackageVersion=0.1.0-preview.N`
4. Run the local consumer smoke test:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test-local-package.ps1 -PackageVersion 0.1.0-preview.N`
   - add `-KeepScratch` if you want to inspect the generated consumer app after the build

## What The Smoke Test Proves

The smoke test script:

- creates a clean Blazor app under `.tmp/`
- installs the local `AgentBlazor` package from `src/AgentBlazor.Components/bin/Release`
- adds a real `AgentTabs` usage to the consumer app
- builds the app

This catches packaging regressions such as:

- unresolved internal package dependencies
- missing component assemblies in the `.nupkg`
- broken compile-time imports in a fresh consumer app

## Current Release Position

The current package should be described as:

- a parity-foundation preview
- suitable for real-project validation
- not yet a blanket claim of full complex-screen MudBlazor parity

Current caveats to keep explicit:

- `AgentDataGrid` still needs deeper public proof for richer server-backed and templated scenarios
- `AgentTreeView` still needs deeper hierarchy-heavy proof
- `AgentFileUpload` agent actions operate on file names and host workflow state; they do not synthesize real browser upload payloads
- the workflow-first demo story is now much stronger, but the package story should still be sold as a parity-foundation preview plus workflow-oriented app-layer integration proof, not as a finished all-scenarios platform

## Publish Notes

Before publishing:

- confirm the package contains `README.md`
- confirm the package contains the internal AgentBlazor assemblies under `lib/net10.0/`
- confirm docs and README describe the current parity scope honestly

Private preview reference:

- `docs/github-packages-private-preview.md`
