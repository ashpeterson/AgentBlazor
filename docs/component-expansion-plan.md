# Non-Paid Feature Expansion Plan

Last updated: 2026-03-02

## Goal

Increase open-source/non-paid product value by expanding component coverage and extension ergonomics while preserving current architecture boundaries.

## Architecture Guardrails

All new work should follow:

- `AgentBlazor.Core`: contracts, capability profile, planner/runtime, policy, telemetry
- `AgentBlazor.Components`: wrapper UI components and chat UX
- `AgentBlazor.Hosting`: registration options and AG-UI host mapping
- `AgentBlazor.ProviderAdapters`: LLM provider integration only
- `AgentBlazor.Licensing`: tier primitives and entitlement contracts

Do not move runtime orchestration into Components.
Do not put provider-specific code in Core or Components.

## Review Findings That Drive This Plan

Current wrapper surface in code now includes:

- `AgentDataGrid`
- `AgentDialog`
- `AgentForm`
- `AgentNavMenu`
- `AgentTabs`
- `AgentSelect`
- `AgentAutocomplete`
- `AgentDatePicker`
- `AgentDateRangePicker`
- `AgentTreeView`
- `AgentStepper`
- `AgentCommandBar`
- `AgentFileUpload`

Current non-paid gaps are no longer wrapper availability; they are depth gaps:

- richer domain-specific demo scenarios using the new wrappers
- end-to-end file integration samples (local policy mode vs remote handoff mode)

## Phase 0 (Stability, Completed)

### P0.1 Dynamic Form Schema Parsing Hardening

Problem:

- `ParseInputSchemaToParameters` splits by comma and can mis-parse descriptions with commas.

Work:

- Replace naive split parser with a tokenizer that respects description boundaries
- Add unit tests for descriptions containing commas and parenthesized examples

Status:

- Completed in code and tests.

### P0.2 Test Suite Alignment

Problem:

- One core test still expects old provider-missing message text.

Work:

- Update assertion to current message contract

Status:

- Completed (`AgentBlazor.Core.Tests` green).

## Phase 1 (Component Breadth: Form Inputs, Completed - Core Wiring)

### P1.1 `AgentSelect`

Actions:

- `open`
- `close`
- `set_value`
- `clear`

State:

- current value
- allowed options
- disabled/read-only flags

### P1.2 `AgentAutocomplete`

Actions:

- `set_query`
- `select_option`
- `clear`

State:

- query text
- selected value
- options snapshot (if available)

Status for Phase 1:

- Added to `AgentComponentCapabilityProfile`
- Added to `AgentComponentTierBoundaries` with Free defaults
- Wrapper execution tests added
- Added demo route + prompts in `/demo/components`
- Remaining: richer workflow-specific prompt packs

## Phase 2 (Component Breadth: Date and Time, Completed - Core Wiring)

### P2.1 `AgentDatePicker`

Actions:

- `set_date`
- `clear`

State:

- selected date
- min/max constraints

### P2.2 `AgentDateRangePicker`

Actions:

- `set_range`
- `clear`

State:

- start/end
- min/max constraints

Status for Phase 2:

- Core wrappers + capabilities + tier map added
- Wrapper execution tests added
- Added demo route + prompts in `/demo/components`
- Remaining: deeper culture/timezone-focused scenario docs

## Phase 3 (Component Breadth: Navigation and Workflow, Completed - Core Wiring)

### P3.1 `AgentTreeView`

Actions:

- `expand`
- `collapse`
- `select_node`

### P3.2 `AgentStepper`

Actions:

- `go_to_step`
- `next`
- `previous`

Status for Phase 3:

- Core wrappers + capabilities + tier map added
- Wrapper execution tests added
- Added demo route + prompts in `/demo/components`
- Remaining: richer tree/step workflow scenarios tied to business models

## Phase 4 (Component Breadth: Commands and Files, Completed - Core Wiring)

### P4.1 `AgentCommandBar`

Actions:

- `invoke_command`
- `list_commands`

### P4.2 `AgentFileUpload`

Actions:

- `attach`
- `remove`
- `list_files`

Status for Phase 4:

- Core wrappers + capabilities + tier map added
- Wrapper execution tests added
- Added demo route + prompts in `/demo/components`
- Added explicit local/remote file-upload policy examples in demo docs/page content
- Remaining: end-to-end integration samples with real file services

## Phase 5 (Open-Source Ergonomics Parity, Completed - Core Wiring)

### P5.1 Event Subscription API

Add lightweight runtime subscriber hooks for:

- turn started/finished
- tool execution started/finished
- error surfaced

### P5.2 Persistent Conversation Store Option

Add pluggable store options beyond in-memory for non-paid users.

Acceptance for Phase 5:

- Public API docs with one in-memory and one persistent sample
- No changes to core planning semantics

Status for Phase 5:

- Added public `IAgentRuntimeEventSubscriber` contract with runtime hooks for:
  - turn started/finished
  - tool execution started/finished
  - surfaced runtime errors
- Added builder APIs:
  - `AddRuntimeEventSubscriber<TSubscriber>()`
  - `UseConversationStore<TStore>()`
  - `UseJsonFileConversationStore(path, configure?)`
- Added file-backed `JsonFileConversationStore` for restart-safe conversation history
- Added core + integration test coverage for event subscriber and persistent store behavior

## Delivery Status

Completed sequence:

1. Phase 0
2. Phase 1
3. Phase 2
4. Phase 3
5. Phase 4
6. Phase 5

Next sequence (post-phase polish):

1. Deepen workflow scenarios for tree/stepper/command/file interactions
2. Add real file service integration examples (local-reference policy and remote-handoff policy)

## Definition of Done for Each New Component

- Wrapper implemented in `AgentBlazor.Components/Wrappers`
- Capability + schema added in `AgentComponentCapabilityProfile`
- Tier mapping added in `AgentComponentTierBoundaries`
- Planner prompt visibility verified (mounted component + action metadata)
- Runtime execution path tested (success and failure)
- Demo route + prompts added
- Documentation updated (`architecture.md`, capability/tier docs, quickstart snippets)
