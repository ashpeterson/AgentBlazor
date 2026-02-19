# MudBlazor Pivot Implementation Checklist

Last updated: 2026-02-18  
Source of truth: `plan.md` (MudBlazor-first pivot)

## Scope

Implement AgentBlazor as an agent orchestration/control layer on top of MudBlazor, aligned with:
- AG-UI protocol: https://docs.ag-ui.com/
- AG-UI source: `C:\Git\repos\ag-ui`
- Microsoft Agent Framework docs: https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp
- Microsoft Agent Framework source: `C:\Git\Grouptree\agent-framework`
- MudBlazor source: `C:\Git\repos\MudBlazor`
- MudBlazor license (MIT): `C:\Git\repos\MudBlazor\LICENSE`

## Milestones

1. M1: MudBlazor onboarding + capability profile contract
2. M2: Runtime execution adapters for MudBlazor capabilities
3. M3: Demo scenarios + policy-safe execution
4. M4: AG-UI/HITL hardening + commercial readiness gates

## Backlog (Issue-by-Issue)

### Epic A: Platform and Contract Alignment

- [x] `AB-MUD-001` Define MudBlazor v1 capability taxonomy in `AgentBlazor.Core`
Acceptance criteria: Capability IDs/namespaces are fixed for v1 (data-grid, dialog, form, nav), actions include input schema + approval flags, docs include versioning rules.
Depends on: none.

- [x] `AB-MUD-002` Add capability profile presets (e.g., `MudBlazorV1Minimal`, `MudBlazorV1Full`)
Acceptance criteria: Consumers can apply presets via registration API; presets are test-covered and additive with custom overrides.
Depends on: `AB-MUD-001`.

- [x] `AB-MUD-003` Update public onboarding API/docs to standard MudBlazor+AgentBlazor setup
Acceptance criteria: Quickstart shows `AddMudServices` + `AddAgentBlazorServices`, `_Imports` includes `MudBlazor`, demo setup matches docs exactly.
Depends on: `AB-MUD-001`.

### Epic B: Runtime and Execution Adapters

- [x] `AB-MUD-010` Introduce explicit Mud action executor contracts (`IDataGridActionExecutor`, `IDialogActionExecutor`, `IFormActionExecutor`, `INavigationActionExecutor`)
Acceptance criteria: Interfaces exist with stable contracts, default no-op implementations are provided, DI registration supports replacement.
Depends on: `AB-MUD-001`.

- [x] `AB-MUD-011` Implement runtime dispatch from capability action -> typed Mud executor
Acceptance criteria: `IComponentActionExecutor` routes known Mud capability actions to typed executors; unknown actions fail safely with actionable errors.
Depends on: `AB-MUD-010`.

- [x] `AB-MUD-012` Preserve policy enforcement (`AllowedComponents` + `AllowedActions`) on all Mud actions
Acceptance criteria: Disallowed Mud actions are filtered before tool registration; disallowed invocations do not execute and are observable in results/logs.
Depends on: `AB-MUD-011`.

- [x] `AB-MUD-013` Align approval gating for sensitive Mud actions with framework approval flow
Acceptance criteria: `RequiresApproval` actions require context approval; behavior is consistent in runtime and `/agentblazor/agui/run` hosted path.
Depends on: `AB-MUD-012`.

### Epic C: Demo and UX Validation

- [x] `AB-MUD-020` Build demo page: agent-driven AgentDataGrid flow
Acceptance criteria: Prompt can trigger grid filter/sort/navigation actions through framework tools; visible UX confirmation in demo.
Depends on: `AB-MUD-011`.

- [x] `AB-MUD-021` Build demo page: agent-driven AgentDialog + AgentForm flow
Acceptance criteria: Prompt can open/close dialog, set form values, validate/submit via policy-controlled actions.
Depends on: `AB-MUD-011`, `AB-MUD-013`.

- [x] `AB-MUD-022` Build demo page: mixed default-agent + custom-agent routing over Mud capabilities
Acceptance criteria: Deterministic routing behavior demonstrated; docs show how to constrain custom agents with component/action policy.
Depends on: `AB-MUD-012`, `AB-MUD-020`, `AB-MUD-021`.

### Epic D: Testing, AG-UI, and Commercial Gates

- [x] `AB-MUD-030` Add integration tests for Mud capability execution matrix
Acceptance criteria: Tests cover allowed/disallowed actions, approval-required actions, and assembly tools coexisting with Mud tools.
Depends on: `AB-MUD-011`, `AB-MUD-012`, `AB-MUD-013`.

- [x] `AB-MUD-031` Add AG-UI endpoint tests for Mud action runs (`/agentblazor/agui/run`)
Acceptance criteria: Endpoint tests validate run execution with Mud actions and approval behavior using framework AG-UI stream.
Depends on: `AB-MUD-013`, `AB-MUD-030`.

- [x] `AB-MUD-032` Add compatibility matrix and version pinning strategy
Acceptance criteria: Matrix documents tested versions of .NET, MudBlazor, Agent Framework packages; CI validates pinned versions.
Depends on: `AB-MUD-003`.

- [x] `AB-MUD-033` Define tier packaging boundaries for Mud integration features
Acceptance criteria: Free/Paid/Premium feature map identifies which Mud integration capabilities are gated; entitlement checks mapped.
Depends on: `AB-MUD-022`, `AB-MUD-032`.

## Critical Path

1. `AB-MUD-001` -> `AB-MUD-010` -> `AB-MUD-011` -> `AB-MUD-012` -> `AB-MUD-013`
2. `AB-MUD-013` -> `AB-MUD-031`
3. `AB-MUD-011` -> `AB-MUD-020` and `AB-MUD-021` -> `AB-MUD-022`
4. `AB-MUD-022` + `AB-MUD-032` -> `AB-MUD-033`

## Recent Completions

- 2026-02-18: `AB-MUD-001` completed.
  - Added MudBlazor v1 taxonomy/profile: `src/AgentBlazor.Core/Components/AgentComponentV1CapabilityProfile.cs`
  - Wired profile into default catalog: `src/AgentBlazor.Core/Components/DefaultShippedComponents.cs`
  - Added core tests for schemas/approval flags: `tests/AgentBlazor.Core.Tests/ServiceRegistrationTests.cs`
  - Added versioning rules doc: `docs/mudblazor-capability-taxonomy.md`

- 2026-02-18: `AB-MUD-010` completed.
  - Added explicit Mud executor contracts and request models: `src/AgentBlazor.Core/Runtime/AgentActionExecutors.cs`
  - Added default no-op implementations for each contract in the same file
  - Registered replaceable DI defaults behind unified `AddAgentBlazor(...)`
  - Added tests proving default registration + replacement behavior: `tests/AgentBlazor.Core.Tests/ServiceRegistrationTests.cs`

- 2026-02-18: `AB-MUD-011` completed.
  - Updated `IComponentActionExecutor` default implementation to dispatch known MudBlazor v1 actions to typed executors
  - Unknown action mappings now fail safely with actionable error messages
  - Added tests validating dispatch path and unknown-action safe failure

- 2026-02-18: `AB-MUD-012` completed.
  - Added shared policy evaluation (`ComponentActionPolicyEvaluation`) consumed by runtime and hosted AG-UI factory
  - Kept policy filtering (`AllowedComponents` + `AllowedActions`) before framework tool registration on both paths
  - Added policy-filter diagnostics in runtime responses/logs and hosted-agent logs/instructions
  - Added tests for blocked-action observability and non-execution behavior

- 2026-02-18: `AB-MUD-013` completed.
  - Consolidated approval checks into shared runtime policy helper (`ComponentActionApprovalPolicy`) used by both runtime and hosted AG-UI factory
  - Enforced `RequiresApproval` consistently for Mud actions (`AgentForm.submit`) across runtime and `/agentblazor/agui/run`
  - Added integration tests for hosted AG-UI endpoint approval behavior using framework `MapAGUI(...)` request shape and SSE stream validation
  - Added runtime integration tests for Mud approval-required action behavior with and without approval context

- 2026-02-18: `AB-MUD-030` completed.
  - Extended integration matrix with Mud approval-required execution tests (`AgentForm.submit`) under approved/non-approved context
  - Added mixed-tool integration test proving assembly tools and Mud tools coexist in one framework run
  - Confirmed policy allowed/disallowed matrix remains enforced under Mud capability set

- 2026-02-18: `AB-MUD-031` completed.
  - Added `/agentblazor/agui/run` hosted endpoint integration tests covering Mud approval-required behavior
  - Verified framework AG-UI stream response (`text/event-stream`) includes lifecycle/tool-result events for Mud actions
  - Verified hosted path executes blocked/approved behavior consistently with runtime approval policy helper

- 2026-02-18: `AB-MUD-020` completed.
  - Added demo AgentDataGrid page with visible state chips, focused row highlight, and executor event feed (`/mud-grid-agent`)
  - Wired demo `IDataGridActionExecutor` implementation to mutate AgentDataGrid state for `filter`, `sort`, `navigate_to_row`, and `set_page`
  - Registered MudBlazor in demo (`MudBlazor` package, `AddMudServices`, Mud providers/assets) and aligned supplier-risk agent policy to AgentDataGrid actions

- 2026-02-18: `AB-MUD-021` completed.
  - Added demo AgentDialog + AgentForm page (`/mud-dialog-form-agent`) with visible dialog/form execution state and event feed
  - Wired demo `IDialogActionExecutor` and `IFormActionExecutor` implementations to apply `open`/`close` and `set_field`/`validate`/`submit` actions
  - Added `supplier-onboarding-agent` with policy-constrained AgentDialog/AgentForm actions and retained approval-gated submit behavior

- 2026-02-18: `AB-MUD-022` completed.
  - Added mixed-routing demo page (`/mud-agent-routing`) showing default vs custom route targets and their allowed Mud action surfaces
  - Added runtime route execution panel that runs the same prompt against explicit route targets and displays planned/executed actions
  - Added integration test coverage for deterministic default/custom route policy behavior on Mud actions
  - Updated quickstart/docs to show policy-constrained custom agent routing over Mud capabilities

- 2026-02-18: `AB-MUD-032` completed.
  - Added compatibility matrix documenting tested .NET, MudBlazor, and Microsoft Agent Framework versions (`docs/compatibility-matrix.md`)
  - Added central package version pinning with `Directory.Packages.props` and repo-level lock-file strategy (`Directory.Build.props`)
  - Added SDK pinning via `global.json`
  - Added CI workflow enforcing central pinning + locked restore (`.github/workflows/ci.yml`)

- 2026-02-18: `AB-MUD-033` completed.
  - Added Mud integration tier map and packaging boundaries doc (`docs/pricing-tiers.md`)
  - Added code-level Mud action tier boundary source (`src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs`)
  - Applied entitlement filtering before framework tool registration in runtime and hosted AG-UI paths
  - Added runtime + hosted integration coverage for tier-blocked Mud premium action execution

- 2026-02-18: Wrapper/runtime context increment completed.
  - Added component registry/runtime contracts and default in-memory registry (`src/AgentBlazor.Core/Runtime/*`)
  - Registered `IAgentComponentRegistry` in service setup (`src/AgentBlazor.Core/Services/AgentBlazorServiceCollectionExtensions.cs`)
  - Added initial Mud wrapper components that publish capability/state and register with registry (`src/AgentBlazor.Components/Wrappers/*`)
  - Updated runtime and hosted AG-UI factory to inject registered wrapper snapshots into framework instruction context
  - Updated demo pages to consume wrappers (`demo/AgentBlazor.Demo/Components/Pages/AgentDataGridAgentDemo.razor`, `demo/AgentBlazor.Demo/Components/Pages/AgentDialogFormAgentDemo.razor`)
  - Added/updated core + integration tests for registry wiring and instruction snapshot inclusion

- 2026-02-18: `AB-MUD-002` completed.
  - Added preset model and API (`AgentCapabilityPreset`, `AgentCapabilityPresets`, `UseAgentCapabilityPreset(...)`)
  - Added `V1Minimal` and `V1Full` preset support for component catalog setup
  - Added core test coverage for preset behavior and additive custom catalog overrides

- 2026-02-18: `AB-MUD-003` completed.
  - Updated onboarding quickstart to include MudBlazor + AgentBlazor setup and preset usage
  - Verified demo wiring remains aligned with documented `AddMudServices`, `_Imports`, providers/assets, and runtime registration flow

- 2026-02-18: Wrapper execution + hosted snapshot parity increment completed.
  - Wired `AgentDataGrid` actions (`filter`, `sort`, `set_page`, `navigate_to_row`) to real wrapper state/callback execution paths
  - Deferred individual input wrappers in favor of `AgentForm` field-level actions (`set_field`, `reset`, `validate`, `submit`)
  - Added wrapper action execution tests in `tests/AgentBlazor.Components.Tests/WrapperActionExecutionTests.cs`
  - Replaced static hosted AG-UI endpoint agent instance with a factory-backed `AIAgent` wrapper so component snapshot instructions refresh at run time
  - Added hosted AG-UI integration coverage for registered component snapshot instruction inclusion with approval-context execution preserved

- 2026-02-18: Demo UX modernization increment completed.
  - Replaced legacy Bootstrap-style demo shell with modern MudBlazor-first layout/navigation styling
  - Removed Bootstrap CSS include from demo app and consolidated visual language via `wwwroot/app.css` design tokens
  - Updated home/status/utility pages to consistent MudBlazor surfaces and typography
  - Kept all existing runtime/agent behaviors intact while improving visual hierarchy and responsiveness

- 2026-02-18: Demo theme selector increment completed.
  - Added runtime theme switching in demo layout with two curated light themes (`Aurora Blue`, `Coastline Mint`)
  - Bound theme selection to `MudThemeProvider` and CSS token classes for cohesive shell/navigation/page styling
  - Kept feature behavior unchanged while adding visual customization control for demo walkthroughs

## Definition of Completion for Pivot

- MudBlazor-first onboarding is stable and documented.
- Built-in agent can control selected MudBlazor components without custom agent code.
- Policy + approval are enforced uniformly in runtime and hosted AG-UI paths.
- Integration tests cover Mud action matrix and AG-UI run path.
- Commercial tier boundaries are explicitly mapped for Mud integration features.
