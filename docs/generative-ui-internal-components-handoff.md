# Generative UI Internal Components Handoff

## Objective
Replace the current demo-oriented generative UI rendering with reusable internal AgentBlazor components that work in any end-user Blazor app.

The target is chat-native generated UI:
- No fixed supplier grid on the Generative UI page.
- Generated components render inside the same chat timeline/window.
- Core stays domain-agnostic.
- Demo-specific behavior stays in demo project only.

Implementation order:
1. `Card` (MudCard-based internal component)
2. `Form` (MudBlazor input/form internal component)
3. `Chart` (MudChart-based internal component)

---

## Current State (Important Context)
- Generative UI page has already been refactored to chat-first layout (single chat pane).
- Core tool catalog is domain-agnostic and now supports generic tools (`summary.card`, `form.draft`, `action.confirmation`, `table.view`).
- Demo generated charts resolve from app-owned data sources through `DemoChartDataSources`.
- Demo and e2e now run through the real runtime/provider path (no deterministic e2e chat client swap).

Observed user feedback:
- They want richer generated components, specifically reusable internal components for Card/Form/Chart.
- They do not want hardcoded supplier-management assumptions in core.
- They want deterministic behavior and no redundant/legacy code paths left behind.

---

## Design Principles
- Domain-agnostic core: no supplier/risk/onboarding assumptions in `AgentBlazor.Core`.
- Reusable internal components: implemented in `AgentBlazor.Components`, usable by any consumer app.
- Deterministic action flow: generated action invocation remains structured and predictable.
- Chat-native UX: generated blocks and follow-up actions stay in the chat thread.
- Cleanup-first: remove obsolete inline rendering and redundant backward-compat branches once migrated.

---

## Target Architecture

### 1. Thin Orchestrator
`AgentGenerativeSurface` should become a thin orchestrator:
- Select block renderer by block kind.
- Pass shared context/session/action callbacks.
- Keep action forwarding pipeline centralized.

### 2. Internal Reusable Block Components
Create internal components under `src/AgentBlazor.Components/GenerativeUI`:
- `AgentGeneratedCard.razor` (MudCard-based)
- `AgentGeneratedForm.razor` (MudBlazor form controls)
- `AgentGeneratedChart.razor` (MudChart-based)

Optional support component:
- `AgentGeneratedTable.razor` (if table rendering is split out from surface)

### 3. Shared Action Pipeline
Do not duplicate action logic per block.
Keep one shared invocation path for:
- payload merging
- generated action forwarding to runtime
- busy/error state handling

---

## Schema / Contract Requirements

### Existing kinds
- `Card`
- `Form`
- `Table`

### New kind to add
- `Chart`

### Chart block contract (proposed)
Minimum fields:
- `id`
- `kind = Chart`
- `title` (optional but recommended)
- `description` (optional)
- `chartType` (`line`, `bar`, `pie`)
- `labels` (`string[]`)
- `series` (array of named numeric series)
- `actions` (optional)

Constraints:
- Validate required shape before rendering.
- Unknown/invalid chart payload should fail safe with validation error and no runtime crash.

### Tooling
Add a generic chart tool in core catalog (domain-neutral), e.g.:
- `chart.view` (or equivalent name aligned with existing naming style)

Tool should map deterministically to a `Chart` block document.

---

## Implementation Plan (Execution Order)

### Phase 1: Card Component
Deliverables:
- Create `AgentGeneratedCard` using MudCard + action buttons.
- Move card rendering out of inline render fragments.
- Route actions through existing shared invocation pipeline.
- Remove replaced inline card rendering code.

Acceptance:
- Existing card-based generated UI scenarios still pass.
- No duplicate card rendering paths remain.

### Phase 2: Form Component
Deliverables:
- Create `AgentGeneratedForm` using MudBlazor inputs.
- Render field types from metadata (`text`, `number`, etc. as supported).
- Keep deterministic payload forwarding for generated actions.
- Keep behavior chat-native (no forced external navigation).
- Remove replaced inline form rendering code.

Acceptance:
- Form actions produce expected payloads.
- Approval flow remains deterministic where applicable.
- No duplicate form rendering paths remain.

### Phase 3: Chart Component
Deliverables:
- Extend generated UI spec with `Chart` block kind.
- Add generic chart tool in core tool catalog.
- Create `AgentGeneratedChart` using MudChart.
- Support deterministic rendering for line/bar/pie.
- Integrate into surface orchestrator.

Acceptance:
- Prompt can generate chart block displayed inside chat.
- Chart follow-up actions (if provided) invoke shared action pipeline.
- Invalid chart payload handled safely.

### Phase 4: Surface Refactor and Consolidation
Deliverables:
- `AgentGenerativeSurface` acts only as orchestrator + shared action handling.
- All per-kind inline rendering removed.
- Shared action/runtime response plumbing remains centralized and tested.

Acceptance:
- No legacy render fragment branches for card/form/chart remain.
- Generated UI behavior unchanged functionally except improved componentization.

### Phase 5: Demo and Examples
Deliverables:
- Update demo deterministic provider/tool mappings to emit reusable generic blocks.
- Ensure demo prompts show in-chat component rendering (card/form/chart).
- Keep demo-specific data logic only in demo project.

Acceptance:
- Demo generative page showcases in-chat component generation and follow-up actions.
- No dependence on fixed page-level supplier grid for generated UI flow.

---

## Testing Requirements

### Unit tests
- Core schema validation for new `Chart` block kind.
- Tool catalog tests for `chart.view` (and existing tools unaffected).
- Component tests for card/form/chart parameter handling and rendering model.

### Integration tests
- Generated action invocation still works from each block type.
- Runtime forwarding behavior remains deterministic.

### E2E tests (Playwright)
Cover at least:
- Card flow in chat
- Form flow in chat (apply action + confirmation)
- Chart flow in chat (render + basic verification)

Current commands:
- `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj`
- `dotnet build demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj /p:UseAppHost=false`
- `npm run test:e2e` (from `tests/e2e`)

---

## Cleanup Rules (Must Follow)
- Remove obsolete inline rendering code once corresponding component is introduced.
- Do not keep parallel old/new rendering paths.
- Do not reintroduce demo domain specifics into core.
- Keep only one deterministic path for generated action forwarding.

---

## Suggested File Touchpoints

Core:
- `src/AgentBlazor.Core/Components/AgentGenerativeUiSpec.cs`
- `src/AgentBlazor.Core/Components/AgentUiToolCatalog.cs`

Components:
- `src/AgentBlazor.Components/GenerativeUI/AgentGenerativeSurface.razor`
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedCard.razor` (new)
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedForm.razor` (new)
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedChart.razor` (new)
- optional `AgentGeneratedTable.razor` (new/refactor)

Demo:
- `demo/AgentBlazor.Demo/Components/Pages/Demo/GenerativeUi.razor`
- `demo/AgentBlazor.Demo/Services/DemoChartDataSources.cs`

Tests:
- `tests/AgentBlazor.Core.Tests/AgentUiToolCatalogTests.cs`
- `tests/e2e/specs/generative-ui.spec.cjs`

---

## Definition of Done
- Card/Form/Chart are internal reusable components, not inline render fragments.
- Generated components render in the same chat window.
- Chart is first-class in generated UI spec and tooling.
- Core remains domain-agnostic.
- Demo-specific examples remain in demo only.
- Tests pass and old redundant code is removed.
