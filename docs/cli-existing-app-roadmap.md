# CLI Existing-App Roadmap

Last updated: 2026-04-09

## Goal

Make the AgentBlazor CLI the primary path for installing AgentBlazor into existing Blazor apps.

This should optimize for the real adoption path:

- analyze an existing app safely
- report what is missing
- apply baseline runtime wiring with explicit approval
- layer on optional features later

## Current State

The CLI is no longer just an analyzer. It now supports the standard installer path for existing Blazor apps:

1. `agentblazor init`
2. `agentblazor scaffold`
3. `agentblazor scaffold --approve`
4. `agentblazor doctor`
5. `agentblazor validate`

What is implemented now:

- `init`
  - analyzes the app
  - generates `.agentblazor/AGENT.md`
  - generates `.agentblazor/state.json`
  - shows an installer-style setup summary with the exact next commands to run
- `doctor`
  - inspects an app for baseline AgentBlazor readiness
  - reports pass/warn/missing status for the baseline install surface
  - reports when the host shape is outside the current standard scaffold path
- `validate`
  - runs the baseline readiness checks
  - validates `.agentblazor/scaffold-manifest.json` when present
  - confirms scaffold-tracked files still exist
- `scaffold`
  - preview-first by default
  - supports `--diff`
  - supports `--approve`
  - supports `--provider openai|azure-openai|ollama`
  - keeps advanced and legacy Blazor hosts in review-first mode with safe additions plus manual-review items
  - infers companion hosted WebAssembly client projects from project references so server-plus-client scaffold can target the correct UI files
  - can now preview/apply the standard hosted WebAssembly server `Program.cs` path plus standard client `_Imports.razor`, shell, layout, and page edits
  - still stops early only when the CLI cannot classify the host into a supported Blazor scaffold path
  - writes `.agentblazor/scaffold-manifest.json` on apply
  - supports local-source evaluation via `--use-local-source`
  - auto-detects the local AgentBlazor repo when run from this repository

What is validated now:

- CLI analysis tests are green: `131/131`
- CLI integration tests are green: `9/9`
- `init --help` and `scaffold --help` are correct
- fresh standard Blazor app smoke test under `/Users/...` succeeds through:
  - `init`
  - `scaffold --approve`
  - `dotnet build`
  - `doctor`
- hosted WebAssembly server+client scaffold is now verified through preview/apply/validate

What is not done yet:

- safe auto-patching for nonstandard hosts such as Oqtane
- interactive provider selection and config generation
- `diff` as a standalone command
- additive commands such as `add workflow`, `add memory-source`, and `add mcp-server`

## Handoff Summary

If someone else picks this up, the key point is:

- standard Blazor app support exists now
- the next work is about broadening host coverage and removing remaining human decisions

Primary implementation files:

- [CommandPathResolver.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli/CommandPathResolver.cs)
- [InitCommand.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli/Commands/InitCommand.cs)
- [DoctorCommand.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli/Commands/DoctorCommand.cs)
- [ScaffoldCommand.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli/Commands/ScaffoldCommand.cs)
- [ValidateCommand.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli/Commands/ValidateCommand.cs)
- [Program.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli/Program.cs)
- [InstallReadinessAnalyzer.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/InstallReadinessAnalyzer.cs)
- [InstallValidationAnalyzer.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/InstallValidationAnalyzer.cs)
- [ExistingAppScaffoldPlanner.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/ExistingAppScaffoldPlanner.cs)
- [ExistingAppScaffoldApplier.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/ExistingAppScaffoldApplier.cs)
- [InstallReadinessReport.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/InstallReadinessReport.cs)
- [InstallValidationReport.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/InstallValidationReport.cs)
- [ScaffoldPlan.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/ScaffoldPlan.cs)
- [ScaffoldPreviewResult.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/ScaffoldPreviewResult.cs)
- [ScaffoldApplyResult.cs](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/ScaffoldApplyResult.cs)

Primary tests:

- [InstallReadinessAnalyzerTests.cs](/home/ashdev/workspace/AgentBlazor/tests/AgentBlazor.Cli.Analysis.Tests/InstallReadinessAnalyzerTests.cs)
- [InstallValidationAnalyzerTests.cs](/home/ashdev/workspace/AgentBlazor/tests/AgentBlazor.Cli.Analysis.Tests/InstallValidationAnalyzerTests.cs)
- [ExistingAppScaffoldPlannerTests.cs](/home/ashdev/workspace/AgentBlazor/tests/AgentBlazor.Cli.Analysis.Tests/ExistingAppScaffoldPlannerTests.cs)

## Product Position

The CLI should evolve from:

- analyzer and `AGENT.md` generator

Into:

- existing-app installer
- validation tool
- incremental feature generator

The greenfield template path is still useful, but it is secondary.

## Command Model

### Phase 1

- `agentblazor init`
  - analyze routes, services, and agent-exposable surfaces
  - generate `.agentblazor/AGENT.md`
  - generate `.agentblazor/state.json`
  - show the exact next installer commands
- `agentblazor doctor`
  - inspect an existing app for baseline AgentBlazor readiness
  - report missing package, service wiring, endpoint mapping, workflow registration, shell assets, Mud providers, and chat surface coverage

### Phase 2

- `agentblazor scaffold`
  - preview the minimum runtime wiring by default
  - support `--diff`
  - support `--approve`
  - write `.agentblazor/scaffold-manifest.json`
  - support local-source installs for evaluation before package adoption

### Phase 3

- `agentblazor add workflow`
- `agentblazor add chat-widget`
- `agentblazor add chat-surface`
- `agentblazor add memory-source`
- `agentblazor add mcp-server`

### Phase 4

- `agentblazor validate`
  - verify that the installed app is still correctly wired
- `agentblazor diff`
  - show proposed edits before mutation commands run

## Why This Shape

The installer should follow existing-app generator patterns rather than only starter-template patterns.

The strongest CLI precedents are:

- Angular schematics for safe project transforms
- Nx generators for workspace-aware configuration changes
- Rails generators for layered additions
- Prisma for lifecycle-oriented commands such as `init`, `generate`, `migrate`, and `validate`

## Baseline Wiring Scope

The first installable baseline should handle:

- `AgentBlazor` package reference or local source project references
- `builder.Services.AddMudServices()`
- `builder.Services.AddAgentBlazor(...)`
- `ConfigureBuilder(... AddWorkflow<T> ...)`
- `app.MapAgentBlazorEndpoints()`
- host shell assets in `App.razor`
- Mud providers in layout
- one chat surface
- one starter capability/workflow class

Current limitation:

- scaffold can now write provider-specific registration, but environment-specific secrets and config values are still a human step
- advanced-host support now includes a working hosted WebAssembly server+client path, but more exotic nonstandard hosts still fall back to review-first/manual work

## Safety Rules

All mutation commands must be:

- idempotent
- explicit about what they will change
- able to run in preview mode by default
- able to target a specific host project in multi-project repos
- conservative with nonstandard apps

For complex hosts such as Oqtane:

- default to detect + report first
- only patch with explicit approval

## Non-Negotiable Flags

Installer-oriented commands should support:

- `--diff`
- `--approve`
- `--host`
- `--non-interactive`

Current behavior:

- `scaffold` is non-mutating even without `--dry-run`
- `--dry-run` is still accepted for explicit preview intent
- `--use-local-source` exists for local evaluation before a published package is available

## Editing Strategy

Use the right mechanism for the right file type:

- C# host files:
  - prefer Roslyn-based edits for `Program.cs` and capability classes
- Razor and markup files:
  - prefer targeted text transforms with strong anchors
- project files:
  - prefer XML-aware edits where practical

Avoid brittle blind string replacement for critical runtime setup.

## Phased Delivery

### Phase 1: Readiness Detection

Deliver:

- `doctor` command
- readiness report model
- checks for:
  - package reference
  - `AddAgentBlazor(...)`
  - `AddWorkflow<T>()`
  - `MapAgentBlazorEndpoints()`
  - chat component presence
  - Mud service/provider presence
  - host shell asset presence

Definition of done:

- a developer can point the CLI at an existing Blazor app and get a useful gap report without mutating code

Status:

- complete for baseline readiness checks

### Phase 2: Baseline Scaffold

Deliver:

- `scaffold` command
- preview and approval workflow
- starter workflow/capability generator
- starter chat widget insertion

Definition of done:

- a standard Blazor app can be converted to an AgentBlazor-ready app in one guided command

Status:

- substantially complete for standard Blazor hosts
- preview, diff, approve, manifest, and local-source evaluation are implemented
- remaining work is broadening host support and tightening provider/config guidance

### Phase 3: Incremental Add-ons

Deliver:

- feature add commands
- memory source installer path
- MCP server registration path

Definition of done:

- advanced features can be layered on without rerunning the full installer

Status:

- not started

## Acceptance Criteria

The CLI is on the right path when:

- it helps more with existing apps than with fresh samples
- it reports gaps before making edits
- it can patch standard Blazor apps with minimal manual work
- it does not silently damage complex hosts
- advanced features fit naturally into the command model

## Verified Commands

These are the commands that are known-good on the current branch:

```bash
dotnet test tests/AgentBlazor.Cli.Analysis.Tests/AgentBlazor.Cli.Analysis.Tests.csproj -nologo
dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- init --help
dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- scaffold --help
```

Fresh app flow that was verified under `/Users/...`:

```bash
dotnet new blazor -n InitSmoke
dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- init /full/path/InitSmoke.csproj --description "Init smoke app" --non-interactive
dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- scaffold /full/path/InitSmoke.csproj --approve
dotnet build /full/path/InitSmoke.csproj -nologo
dotnet run --project src/AgentBlazor.Cli/AgentBlazor.Cli.csproj -- doctor /full/path/InitSmoke.csproj
```

## Known Constraints

- Standard-host-first: `scaffold --approve` is intentionally conservative and is not yet safe for arbitrary complex hosts.
- Provider config still needs a human: scaffold can insert provider registration, but secrets and environment-specific values are not generated.
- Local-source evaluation is path-sensitive: verified under normal `/Users/...` paths. macOS `/tmp` symlink paths can distort `ProjectReference` resolution and are not the right evaluation path.
- `doctor` currently treats local `ProjectReference`s as equivalent to package references, which is correct for local evaluation but should stay explicit in docs.

## Immediate Next Work

The next contributor should work in this order:

1. Extend advanced-host support for solutions like Oqtane:
   - detect host shape
   - add safer patching where host structure is predictable
   - keep preview and diff strong
2. Extend provider setup beyond registration guidance:
   - environment variable and appsettings templates
   - clearer host-specific configuration guidance
3. Start additive commands:
   - `add workflow`
   - `add chat-widget`
   - `add memory-source`
   - `add mcp-server`
4. Add `diff` as a standalone command.

## Worktree Notes

At the time of this update there are unrelated dirty lockfile changes in the repository that are not part of the CLI handoff:

- [packages.lock.json](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/demo/AgentBlazor.Demo/packages.lock.json)
- [packages.lock.json](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Hosting/packages.lock.json)
- [packages.lock.json](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/tests/AgentBlazor.IntegrationTests/packages.lock.json)

Do not assume those files are part of the CLI changes.
