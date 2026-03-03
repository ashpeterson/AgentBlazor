# Product Expansion Plan

Last updated: 2026-03-03

## Goal

Move AgentBlazor from wrapper-complete demo framework to product-ready agentic platform parity on core workflows:

- shared state
- multi-agent orchestration
- runtime inspection/debugging
- production-grade demo flows

while preserving the current architecture boundaries and keeping the default user setup lightweight.

## Architecture Guardrails

All work must respect:

- `AgentBlazor.Core`: runtime contracts, planning/validation/execution, policy, state/event contracts
- `AgentBlazor.Components`: UI wrappers, chat surfaces, inspector UI
- `AgentBlazor.Hosting`: endpoint and host integration boundaries
- `AgentBlazor.ProviderAdapters`: LLM integrations only
- `AgentBlazor.Licensing`: tier primitives and entitlement wiring

Non-negotiables:

- no provider-specific behavior in Core/Components
- no runtime orchestration moved into Components
- no mandatory extra infrastructure for baseline usage

## Current Baseline

### Completed Foundation

- Deterministic runtime pipeline (`Plan -> Validate -> Execute`) is operational.
- Wrapper breadth for core UI controls is complete.
- Runtime subscriber and conversation persistence hooks are shipped.
- Demo route map is broad and docs-first.

### Completed Reliability Refinement (2026-03-03)

- `AgentFormPageBase<TModel>` partial-update action aliases added:
  - `fill_<form>`
  - `set_<form>`
  - `update_<form>`
  - `set_field`
- Planner prompt guardrails now explicitly discourage submit unless user intent is explicit.
- Runtime submit filtering enforces that policy even when model output is overly aggressive.
- `PlanValidator` now validates against mounted live component actions, not only static catalog actions.
- Dojo/demo readability and prompt guidance refined for first-run success.

## Gap Analysis (What Is Missing)

1. Shared state API and synchronization semantics are not first-class.
2. Multi-agent runtime mode and per-agent tooling/thread semantics are not first-class.
3. Embedded runtime inspector is not yet a complete product console.
4. Demo production depth is still limited by in-memory/service-seeded patterns.
5. Tier enforcement is not yet complete at fine-grained action level.

## Roadmap Phases

### Phase A: Shared State API (Priority 1)

### Objective

Deliver a first-class agent<->UI shared state contract with no extra setup burden.

### Scope

- Core contracts:
  - shared state snapshot
  - state delta events
  - conflict/merge semantics for concurrent edits
- Runtime integration:
  - state in turn context
  - state update stream events
- Storage:
  - in-memory default provider
  - optional persistent provider (JSON/file-backed first)
- Developer API:
  - simple registration in `AddAgentBlazor(...)`
  - no mandatory external service dependency

### Acceptance Criteria

- Works out of the box with in-memory defaults.
- State round-trips agent->UI->agent in a single session.
- Persistent mode survives restart when enabled.
- Integration tests cover concurrent state updates and reconnect behavior.

### Phase B: Multi-Agent Runtime Mode (Priority 2)

### Objective

Support multiple named agents with route/agent lock and per-agent tool scoping.

### Scope

- Multiple agent registration and discovery in runtime metadata.
- Agent selection strategy:
  - explicit lock mode
  - optional router mode
- Per-agent tool visibility and execution boundaries.
- Session/thread separation and state isolation by agent.
- Demo UX:
  - visible active agent
  - controlled handoff flow between agents

### Acceptance Criteria

- Multiple agents can execute independently in one app instance.
- Tools can be scoped to specific agents.
- Route transitions do not leak state between agents unless explicitly configured.
- End-to-end tests validate lock mode and handoff behavior.

### Phase C: Embedded Inspector Console (Priority 3)

### Objective

Ship a product-grade in-app inspector for runtime transparency and debugging.

### Scope

- Event timeline:
  - turn started/finished
  - planning output summary
  - validation failures
  - action execution results
  - approval blocks
- State inspection:
  - mounted component snapshots
  - per-turn diffs where available
- Prompt trace visibility:
  - system prompt and model response references when enabled
- Developer ergonomics:
  - enable/disable toggle
  - no impact on default end-user UX if disabled

### Acceptance Criteria

- All core runtime stages are visible in inspector.
- Approval and validation failure causes are inspectable without logs.
- Inspector can be enabled in demo and local apps with one option.

### Phase D: Production-Grade Demo Workflows (Priority 4)

### Objective

Convert Dojo and scenario demos from seed-centric behavior to realistic workflow implementation.

### Scope

- Persisted session/workspace model for Dojo.
- Deterministic action/run note history replay.
- Realistic file pipeline sample:
  - local-reference mode
  - remote handoff mode
- Replace fragile demo-hardcoded assumptions with service-backed workflows.

### Acceptance Criteria

- Dojo state persists between reloads in configured mode.
- Run notes align with executed actions.
- File flow samples demonstrate real integration boundary patterns.

### Phase E: Tier and Policy Hardening (Priority 5)

### Objective

Complete action-level enforcement and predictable entitlement behavior.

### Scope

- Fine-grained action gating by entitlement tier.
- Consistent error messaging for gated actions.
- Runtime diagnostics surface showing why action was blocked.
- Coverage tests for free/paid/premium transitions.

### Acceptance Criteria

- No paid-only action executes in free tier.
- All blocked actions return deterministic, user-readable reasons.
- Tier behavior is fully covered in automated tests.

## Cross-Cutting Requirements

For every phase:

- No new mandatory external infrastructure for baseline local usage.
- Maintain backwards-compatible defaults where possible.
- Add docs and examples at the same time as implementation.
- Add unit + integration tests for each new behavior.

## Definition of Done (Per Deliverable)

- Implementation is complete in correct layer(s).
- Public API surface is documented with at least one minimal and one realistic sample.
- Validation includes:
  - Core tests
  - Integration tests
  - Demo build
- Status and architecture docs updated to reflect shipped behavior.

## Recommended Delivery Sequence

1. Phase A (Shared State)
2. Phase B (Multi-Agent)
3. Phase C (Inspector)
4. Phase D (Production Demo Flows)
5. Phase E (Tier Hardening)

## Risk Register

- Shared state merge semantics can become brittle without strict event contracts.
- Multi-agent routing can introduce hidden cross-agent leakage if session boundaries are weak.
- Inspector scope creep can delay platform features if not phased.
- Demo hardening can overfit to recipe/supplier examples unless abstractions are generalized.
