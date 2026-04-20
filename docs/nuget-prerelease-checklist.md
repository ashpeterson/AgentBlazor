# NuGet Prerelease Checklist

Last updated: 2026-04-20

Use this before publishing `AgentBlazor`, `AgentBlazor.Client`, and `AgentBlazor.Cli` prerelease packages for real-project validation.

## Goal

Ship a package that:

- restores cleanly in a fresh Blazor app
- installs the matching `agentblazor` CLI tool from the same package version
- exposes the expected static assets and component assemblies
- does not overclaim full MudBlazor parity before richer real-project proof exists

## Minimum Bar

1. Run the focused test gates:
   - `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj -nologo /p:UseSharedCompilation=false`
   - `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj -nologo /p:UseSharedCompilation=false`
2. Run the public demo/browser gate:
   - `npm --prefix tests/e2e run test:e2e`
3. Run the hosted WebAssembly remote-chat browser gate:
   - `npm --prefix tests/e2e run test:hosted-wasm-remote-chat`
   - this creates a fresh server+client Blazor Web App, installs packed local `AgentBlazor` and `AgentBlazor.Client`, maps `MapAgentBlazorRemoteChat()`, submits prompts through remote widget/surface/panel/bar, and verifies widget minimize/reopen behavior
4. Pack the prerelease:
   - `dotnet pack src/AgentBlazor.Components/AgentBlazor.Components.csproj -nologo -c Release /p:UseSharedCompilation=false /p:PackageVersion=0.1.0-preview.N`
   - `dotnet pack src/AgentBlazor.Client/AgentBlazor.Client.csproj -nologo -c Release /p:UseSharedCompilation=false /p:PackageVersion=0.1.0-preview.N`
   - `dotnet pack src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -nologo -c Release /p:UseSharedCompilation=false /p:PackageVersion=0.1.0-preview.N`
5. Run the local consumer smoke test:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test-local-package.ps1 -Pack -PackageVersion 0.1.0-preview.N`
   - add `-OpenAIApiKey $env:OPENAI_API_KEY` to include a live AG-UI workflow run
   - add `-KeepScratch` if you want to inspect the generated consumer app after the build
6. After the package is published, repeat the clean-app install using the published feed:
   - install `AgentBlazor` from the feed
   - install `AgentBlazor.Client` from the feed for hosted WebAssembly client validation
   - install `AgentBlazor.Cli` from the same feed
   - run `agentblazor --version`, `agentblazor scaffold --diff`, `agentblazor scaffold --approve`, `dotnet restore`, `dotnet build`, `agentblazor doctor`, and `agentblazor validate`

## What The Smoke Test Proves

The smoke test script:

- creates a clean Blazor app under `.tmp/`
- creates an isolated local NuGet feed for the full AgentBlazor package set
- installs the local `AgentBlazor` package in a fresh consumer app
- writes the same host wiring the public quickstart now requires
- installs the local `AgentBlazor.Cli` tool and generates `.agentblazor/AGENT.md`
- starts the app and verifies the home route loads
- optionally runs a live AG-UI semantic workflow turn when an OpenAI key is supplied

This catches packaging regressions such as:

- unresolved internal package dependencies
- missing component assemblies in the `.nupkg`
- broken compile-time imports in a fresh consumer app
- missing host-shell assets or endpoint wiring in the documented setup
- CLI packaging regressions that would break `agentblazor init`
- CLI/runtime package-version drift that would scaffold a different `AgentBlazor` version than the installed CLI package

## Current Release Position

Current validated private-preview package:

- `0.1.0-preview.9`
- GitHub Packages workflow run `24680658866`
- source commit `faaeb6842ca90f4bd4cdeea070b1a28e30886463`
- published-feed clean-app validation passed, including CLI install, scaffold, restore/build, `doctor`, `validate`, and runtime static-asset smoke
- published-feed real-app validation passed against `damienbod/BlazorSecurityNet10`, including CSP nonce preservation for scaffolded assets
- published-feed all-surface browser validation passed against `damienbod/BlazorSecurityNet10`, covering `AgentChatWidget`, `AgentChatSurface`, `AgentChatPanel`, and `AgentChatBar` with deterministic provider responses and production-style prompts; report `tests/e2e/artifacts/external-chat-widget/2026-04-20T17-33-11-449Z/report.md`
- published-feed hosted WebAssembly browser validation passed in a generated server+client app using `AgentBlazor.Client`; report `tests/e2e/artifacts/hosted-wasm-remote-chat/2026-04-20T17-31-59-002Z/report.md`

The generated scaffold workflow has been confirmed to compile in a clean consumer app without manual references to bundled internal assemblies. The scaffolded `AppCapabilities.cs` file imports `AgentBlazor.App`, so each future release still needs published-feed validation that the restored `AgentBlazor` package exposes `AgentBlazor.Core.dll` as a compile asset.

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
- confirm `AgentBlazor.Cli` installs from the same package version and reports that version with `agentblazor --version`
- confirm docs and README describe the current parity scope honestly

Private preview reference:

- `docs/github-packages-private-preview.md`
