# MudBlazor Compatibility Roadmap

Last updated: 2026-03-20

## Goal

Make every shipped `Agent*` component a drop-in replacement for the corresponding `Mud*` component without sacrificing MudBlazor features.

That means:

- users can replace `Mud*` with `Agent*` in complex real-world screens
- existing MudBlazor parameters, callbacks, templates, `@bind` patterns, and `@ref` workflows still work
- agent capabilities are layered on top instead of narrowing the native component contract

## Current State

The roadmap is now partly executed, not just planned.

Completed or materially completed:

- shared agent runtime support was extracted so high-surface components no longer depend only on `AgentControllableComponentBase`
- the current Mud-backed `Agent*` set has been moved onto native-first implementations
- the components explorer exposes focused compatibility proof for every shipped Mud-backed `Agent*` component
- rendered parity tests exist in `AgentBlazor.Components.Tests`
- Playwright covers the proof routes in the public demo app
- the component explorer now sits behind the workflow hub as a supporting reference surface instead of the primary product-story entry point

Still outstanding:

- richer `MudDataGrid` proof around server-backed and heavily templated usage
- deeper hierarchy proof for `AgentTreeView`
- more composed-screen parity variants beyond the current workflow screen
- final product positioning for `AgentCommandBar` as the intentional non-Mud outlier

## Product Standard

For high-surface components, "drop-in replacement" means:

- preserve all public MudBlazor parameters that matter to real app usage
- preserve named child-content slots
- preserve event callbacks
- preserve public instance methods reachable through `@ref`
- preserve MudBlazor lifecycle and validation rules
- add `AgentId`, readable state, and agent actions without changing normal component behavior

If a complex MudBlazor example cannot be ported with only minimal rename changes, parity is not complete.

## Why This Is Needed

Current AgentBlazor wrappers prove the agent model, but some of them do not preserve the full MudBlazor surface.

That is a problem for adoption because public MudBlazor usage heavily relies on:

- server-backed data loading
- custom filter templates
- toolbar / no-records / pager slots
- rich event callbacks
- complex binding patterns
- `@ref` methods such as reload and focus workflows

The compatibility work is therefore not optional polish. It is the adoption path.

## Architecture Direction

The current base-class model is too restrictive for high-surface MudBlazor components.

Target architecture:

1. extract agent registration, action execution, state publication, and deferred-intent handling out of `AgentControllableComponentBase`
2. let high-surface `Agent*` components inherit the matching MudBlazor component directly where feasible
3. have those components implement `IAgentControllable` through shared helper services instead of through a single base class
4. keep explicit agent metadata and readable-state modeling as a layer on top of native MudBlazor behavior

This keeps MudBlazor compatibility first and agent behavior additive.

## Component Scope

The current shipped compatibility target set is:

- `AgentDataGrid -> MudDataGrid`
- `AgentDialog -> MudDialog`
- `AgentForm -> MudForm`
- `AgentNavMenu -> MudNavMenu`
- `AgentTabs -> MudTabs`
- `AgentSelect -> MudSelect`
- `AgentAutocomplete -> MudAutocomplete`
- `AgentDatePicker -> MudDatePicker`
- `AgentDateRangePicker -> MudDateRangePicker`
- `AgentTreeView -> MudTreeView`
- `AgentStepper -> MudStepper`
- `AgentFileUpload -> MudFileUpload`

The current special-case component is:

- `AgentCommandBar`

`AgentCommandBar` does not map cleanly to one MudBlazor primitive, so it should preserve its custom behavior while following the same compatibility principles for parameters, templates, and events where applicable.

## Delivery Phases

### Phase 1. Compatibility Foundation

Objective:

- make the architecture capable of true MudBlazor parity

Work:

- extract reusable agent behavior from `AgentControllableComponentBase`
- define a reusable helper/service layer for:
  - component registration
  - action discovery and execution
  - readable state publication
  - deferred navigation-intent application
- document parity rules for every shipped component
- create test fixtures that compare `Mud*` usage to `Agent*` usage

Definition of done:

- at least one high-surface component can inherit MudBlazor directly and still participate fully in the agent runtime

Status:

- completed

### Phase 2. Data Entry And Grid Parity

Objective:

- solve the highest-risk adoption blockers first

Priority components:

- `AgentDataGrid`
- `AgentForm`
- `AgentSelect`
- `AgentAutocomplete`
- `AgentDatePicker`
- `AgentDateRangePicker`

Work:

- preserve server-data workflows
- preserve custom filter templates and slot content
- preserve callbacks and `@ref` methods
- preserve Mud parameter rules and mutual-exclusion rules
- add agent-readable state that reflects native component state instead of replacing it

Definition of done:

- complex MudBlazor examples with server data, templating, and validation port with minimal rename changes

Status:

- materially completed for the current component set
- still needs stronger proof depth for richer `MudDataGrid` scenarios

### Phase 3. Navigation And Workflow Parity

Objective:

- preserve layout and multi-step application patterns

Priority components:

- `AgentDialog`
- `AgentTabs`
- `AgentNavMenu`
- `AgentStepper`
- `AgentTreeView`
- `AgentFileUpload`

Work:

- preserve nested child content
- preserve visibility / selection / expansion callbacks
- preserve dialog and navigation lifecycles
- preserve uploader and stepper behaviors in workflow-heavy pages

Definition of done:

- workflow-oriented pages keep Mud behavior while gaining agent control

Status:

- materially completed for the current component set
- still needs deeper hierarchy and additional composed workflow proof

### Phase 4. Explorer And Adoption Proof

Objective:

- make compatibility visible and easy to trust

Work:

- add focused compatibility demos inside the components explorer
- add migration guidance from `Mud*` to `Agent*`
- include realistic samples, not only simplified component cards
- add prompt-backed browser tests against those examples

Definition of done:

- docs and demo app prove parity with realistic screens instead of isolated happy paths

Status:

- underway and already visible in the repo
- focused component proof in the explorer is in place
- one broader composed workflow proof page is in place
- still not finished for the richest scenarios

## Per-Component Acceptance Criteria

### AgentDataGrid

Must preserve:

- `Items`, `ServerData`, and virtualized server-data modes
- column templates and filter templates
- pager, toolbar, no-records, loading, and grouping content
- sorting, filtering, editing, grouping, selection, row click, and row context menu
- `ReloadServerData()` and other practical `@ref` workflows

Agent layer must add:

- deterministic sort/filter/page/select actions
- readable state for current page, active filters, sort state, focused row, and visible row summaries
- planner-friendly aliases for complex columns when needed

### AgentForm

Must preserve:

- validation lifecycle
- child content composition
- nested Mud input usage
- `@ref` access and validity state patterns

Agent layer must add:

- field set
- validate
- reset
- submit

### AgentDialog

Must preserve:

- title, body, and action content
- visibility binding
- modal lifecycle behavior

Agent layer must add:

- deterministic open/close/confirm actions

### AgentSelect And AgentAutocomplete

Must preserve:

- generic typing
- complex model binding
- search and item templates
- clear/open/close/change behavior

Agent layer must add:

- value selection and query-setting actions

### AgentTabs, AgentStepper, AgentNavMenu, AgentTreeView

Must preserve:

- nested content
- selection state
- active item / step / tab / node transitions
- event callbacks and route-friendly integration

Agent layer must add:

- deterministic navigation actions over the existing UI state

### AgentDatePicker And AgentDateRangePicker

Must preserve:

- native MudBlazor binding and formatting behavior
- validation and clear/reset semantics

Agent layer must add:

- deterministic date and range actions

### AgentFileUpload

Must preserve:

- file selection behavior
- validation and accepted-file configuration
- upload workflow integration

Agent layer must add:

- attach, remove, and list-file actions over the current host-owned workflow

## Test Strategy

Each parity component needs four layers of validation:

1. contract tests
   - verify parameters, callbacks, and methods remain available
2. behavior tests
   - verify native MudBlazor scenarios behave the same after switching to `Agent*`
3. agent-runtime tests
   - verify planner, validator, and executor interactions still work
4. browser tests
   - verify realistic public examples, including server-backed and templated scenarios

Parity should be tested against realistic examples, not only synthetic unit cases.

## Demo Requirements

The demo app should include:

- focused `Agent*` examples that prove MudBlazor-compatible behavior on complex screens
- server-backed examples for `AgentDataGrid`
- form-heavy examples with validation
- workflow examples for dialogs, steppers, tabs, uploads, and tree views

This should become the public proof that AgentBlazor adds agent behavior without taking MudBlazor away.

Current status:

- the demo now meets the baseline proof requirement for the current shipped Mud-backed set
- the remaining need is depth, not initial coverage
- this is strong enough for a NuGet prerelease focused on real-project validation, but not yet for broad claims of full complex-screen parity

## Non-Negotiables

Do:

- optimize for compatibility first on high-surface components
- preserve native MudBlazor behavior even when the agent layer is richer
- treat parity gaps as product issues

Do not:

- hand-curate a narrow subset of MudBlazor features for complex components
- force users to simplify their existing MudBlazor screens to adopt AgentBlazor
- claim drop-in support unless complex scenarios are validated

## Success Criteria

AgentBlazor succeeds at compatibility when:

- a team can replace `Mud*` with `Agent*` across complex pages with minimal rename changes
- their existing MudBlazor features still work
- the agent runtime can understand and act on those components deterministically
- the repo demonstrates and tests those scenarios publicly
