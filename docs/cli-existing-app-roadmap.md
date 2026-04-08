# CLI Existing-App Roadmap

Last updated: 2026-04-08

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

What is implemented now:

- `init`
  - analyzes the app
  - generates `.agentblazor/AGENT.md`
  - generates `.agentblazor/state.json`
  - shows an installer-style setup summary with the exact next commands to run
- `doctor`
  - inspects an app for baseline AgentBlazor readiness
  - reports pass/warn/missing status for the baseline install surface
- `scaffold`
  - preview-first by default
  - supports `--diff`
  - supports `--approve`
  - writes `.agentblazor/scaffold-manifest.json` on apply
  - supports local-source evaluation via `--use-local-source`
  - auto-detects the local AgentBlazor repo when run from this repository

What is validated now:

- CLI analysis tests are green: `106/106`
- `init --help` and `scaffold --help` are correct
- fresh standard Blazor app smoke test under `/Users/...` succeeds through:
  - `init`
  - `scaffold --approve`
  - `dotnet build`
  - `doctor`

What is not done yet:

- safe patching for nonstandard hosts such as Oqtane
- provider selection as a guided CLI decision
- `validate`
- `diff` as a standalone command
- additive commands such as `add workflow`, `add memory-source`, and `add mcp-server`

## Handoff Summary

If someone else picks this up, the key point is:

- standard Blazor app support exists now
- the next work is about broadening host coverage and removing remaining human decisions

Primary implementation files:

- [CommandPathResolver.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli/CommandPathResolver.cs)
- [InitCommand.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli/Commands/InitCommand.cs)
- [DoctorCommand.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli/Commands/DoctorCommand.cs)
- [ScaffoldCommand.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli/Commands/ScaffoldCommand.cs)
- [Program.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli/Program.cs)
- [InstallReadinessAnalyzer.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/InstallReadinessAnalyzer.cs)
- [ExistingAppScaffoldPlanner.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/ExistingAppScaffoldPlanner.cs)
- [ExistingAppScaffoldApplier.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/ExistingAppScaffoldApplier.cs)
- [InstallReadinessReport.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/InstallReadinessReport.cs)
- [ScaffoldPlan.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/ScaffoldPlan.cs)
- [ScaffoldPreviewResult.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/ScaffoldPreviewResult.cs)
- [ScaffoldApplyResult.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Cli.Analysis/Models/ScaffoldApplyResult.cs)

Primary tests:

- [InstallReadinessAnalyzerTests.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/tests/AgentBlazor.Cli.Analysis.Tests/InstallReadinessAnalyzerTests.cs)
- [ExistingAppScaffoldPlannerTests.cs](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/tests/AgentBlazor.Cli.Analysis.Tests/ExistingAppScaffoldPlannerTests.cs)

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

- provider selection is still left as a human follow-up in `Program.cs`

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
- remaining work is broadening host support and handling provider selection

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
- Provider decision missing: scaffold inserts the AgentBlazor startup block, but still leaves provider selection as a human step.
- Local-source evaluation is path-sensitive: verified under normal `/Users/...` paths. macOS `/tmp` symlink paths can distort `ProjectReference` resolution and are not the right evaluation path.
- `doctor` currently treats local `ProjectReference`s as equivalent to package references, which is correct for local evaluation but should stay explicit in docs.

## Immediate Next Work

The next contributor should work in this order:

1. Add provider guidance or provider selection to the scaffold flow.
2. Improve nonstandard-host detection so scaffold can stop earlier and explain why.
3. Add an explicit advanced-host path for solutions like Oqtane:
   - detect host shape
   - downgrade risky edits to review-only items
   - keep preview and diff strong
4. Add `validate` as a higher-level post-install verification command.
5. Start additive commands:
   - `add workflow`
   - `add chat-widget`
   - `add memory-source`
   - `add mcp-server`

## Worktree Notes

At the time of this update there are unrelated dirty lockfile changes in the repository that are not part of the CLI handoff:

- [packages.lock.json](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/demo/AgentBlazor.Demo/packages.lock.json)
- [packages.lock.json](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/src/AgentBlazor.Hosting/packages.lock.json)
- [packages.lock.json](/Users/ashleypetetson/Documents/GitHub/AgentBlazor/tests/AgentBlazor.IntegrationTests/packages.lock.json)

Do not assume those files are part of the CLI changes.
