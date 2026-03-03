# AgentBlazor Development Status

Last updated: 2026-03-03

## Done (Implemented)

- Core wrapper surface is shipped and exercised in demo routes:
  - `AgentDataGrid`, `AgentDialog`, `AgentForm`, `AgentNavMenu`, `AgentTabs`
  - `AgentSelect`, `AgentAutocomplete`, `AgentDatePicker`, `AgentDateRangePicker`
  - `AgentTreeView`, `AgentStepper`, `AgentCommandBar`, `AgentFileUpload`
- Runtime architecture is in place with deterministic `Plan -> Validate -> Execute` flow.
- Open-source ergonomics completed:
  - Runtime event subscriber API (`IAgentRuntimeEventSubscriber`)
  - Pluggable conversation store APIs (`UseConversationStore<TStore>()`, `UseJsonFileConversationStore(...)`)
  - Restart-safe JSON conversation persistence
- Route-aware docs-first demo structure is in place:
  - `/demo/dojo`, `/demo/components`, `/demo/workflow`, `/demo/suppliers`, `/demo/onboarding`, `/demo/generative-ui`, `/demo/status`, `/demo/docs`
- Provider-missing and chat resiliency UX landed:
  - startup warning + user-facing guidance
  - `ErrorBoundary` recovery
  - long-running warning and activity state indicators

### Recently Completed (2026-03-03)

- Form automation reliability improvements:
  - `AgentFormPageBase<TModel>` now exposes partial-update aliases:
    - `fill_<form>`
    - `set_<form>`
    - `update_<form>`
    - `set_field`
  - Field name matching now handles human phrasing better for single-field edits.
- Planner guardrails improved:
  - prompt instructions explicitly discourage `submit` unless user intent is submit/save/confirm/send.
  - partial form edits are explicitly allowed without asking for all fields.
- Runtime safety policy strengthened:
  - `AgentForm.submit` steps are filtered unless submit-intent is explicit.
- Validation hardening:
  - `PlanValidator` now checks action availability on mounted live components, not only static catalog entries.
  - prevents execution of actions that exist in catalog but are not exposed on current route/component instance.
- Demo refinement pass:
  - Dojo workspace prompt examples simplified for first-run success.
  - Layout density reduced and readability increased in demo shell and docs-style sections.
  - Assistant surface copy/placeholders aligned to actual Dojo workflow commands.
- Test coverage added for all above behavior:
  - component tests for `AgentFormPageBase` partial updates
  - integration tests for submit filtering and mounted-action validation

## Outstanding (Known Gaps)

### Product/Platform Gaps

- Shared state model is still missing as a first-class API:
  - no canonical agent<->UI synchronized state contract yet (CopilotKit-style coagent/shared-state parity gap).
- Multi-agent runtime mode is missing:
  - no first-class router/agent-lock mode and no per-agent session UX in the demo.
- Embedded runtime inspector is partial:
  - inspector data exists, but no full production-grade event timeline + state diff + approvals console UX.
- Demo production depth is limited:
  - Dojo remains service-seeded/in-memory and not yet a full persistence-backed workflow implementation.

### Runtime/Policy Gaps

- Tier boundaries exist but full action-level hard enforcement remains partial.
- Capability consistency still needs ongoing alignment:
  - static catalog vs mounted live behavior must stay in sync as wrappers evolve.
- Domain scenario depth remains thin for:
  - tree/stepper orchestration
  - command bar workflows
  - file upload integrated with real storage/processing backends

## What Needs Doing Next (Product Priority)

1. Shared state API + sync semantics:
   - no extra user infrastructure required by default
   - in-memory default state store
   - optional persisted provider for production
2. Multi-agent runtime mode with route/agent lock and per-agent tool scoping.
3. Embedded runtime inspector panel:
   - AG-UI/runtime event timeline
   - planned actions + approvals view
   - component state snapshots and diffs
4. Production-grade demo backend flows:
   - replace Dojo seed-only state with persistence-backed session workflows
   - keep local default setup simple (no forced external services)
5. Complete action-level tier enforcement and diagnostic messaging.
6. Expand deep end-to-end business scenarios for tree/stepper/command/file wrappers.
7. Stabilize developer-facing contracts with explicit compatibility guarantees and migration notes.

## Immediate Execution Plan

1. Deliver Shared State foundation:
   - core state contract
   - runtime state update events
   - in-memory + JSON persistence implementation
   - demo page proving UI + agent bidirectional updates
2. Deliver Multi-Agent V1:
   - register multiple named agents
   - route/agent lock handling
   - per-agent tool visibility and thread/session isolation
   - demo route showing agent handoff patterns
3. Deliver Inspector V1:
   - integrated panel in demo layout
   - per-turn timeline and action details
   - approvals and validation failure visibility
4. Deliver Dojo backend hardening:
   - persistent workspace sessions
   - deterministic replay of run notes/actions
   - replace seed-only assumptions

## Verification Snapshot

Latest local validation after the 2026-03-03 reliability + docs pass:

- `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj` passed
- `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj` passed
- `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj` passed
- `dotnet build demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj` passed

## Reference Docs

- Architecture: `docs/architecture.md`
- Expansion roadmap: `docs/component-expansion-plan.md`
- Tier model: `docs/pricing-tiers.md`
