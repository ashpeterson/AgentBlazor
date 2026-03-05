# Product Expansion Plan

Last updated: 2026-03-05

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
- Demo platform has been re-centered around the current product story:
  - `/demo/dojo` as the primary agentic workflow showcase
  - `/demo/components` as the component explorer
  - `/demo/components/attribute-based` as the convention-first developer story
  - older supplier/workflow pages now act as compatibility redirects, not the primary product narrative

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
- Clarification-loop recovery now auto-converts explicit single-field edit prompts into mounted-form `set_field` actions.

### Completed Shared State Foundation (2026-03-04)

- Core shared-state contracts shipped:
  - `IAgentSharedStateStore`
  - `AgentSharedStateSnapshot`
  - `AgentSharedStateDelta`
- Default in-memory provider shipped (`InMemoryAgentSharedStateStore`).
- Runtime now:
  - seeds shared state from mounted components + route context
  - passes shared state into planner context
  - emits `StateSnapshot` and `StateDelta` runtime stream events
- Hosted AG-UI bridge now forwards shared-state payloads and records message->run correlation.

### Completed Demo Repositioning (2026-03-05)

- Main demo navigation now aligns to the intended public story:
  - Dojo
  - Components
  - Attribute-based example
- Dojo now has route-local experience state for:
  - current example
  - current integration label
  - current `Preview` / `Code` / `Docs` mode
- Dojo assistant interaction is now embedded inside the dojo page itself instead of relying on the app-level assistant pane.
- Dojo examples are now wired as live agent workflows rather than seed-only cards:
  - agentic chat
  - backend tool rendering
  - human in the loop
  - agentic generative UI
  - tool-based generative UI
  - shared state
  - predictive state updates

## Gap Analysis (What Is Missing)

1. Shared state persistence and conflict semantics are not yet production-complete.
2. Multi-agent advanced handoff/orchestration UX is not yet product-complete.
3. Embedded runtime inspector needs deeper product-console depth beyond V1 timeline coverage.
4. Demo production depth still needs broader non-Dojo persistence and workflow hardening.
5. Dojo parity is still visually and experientially incomplete:
   - structure is now pointed in the right direction
   - fidelity against the CopilotKit dojo reference still needs another refinement pass
6. Component explorer breadth is good, but scenario depth is still uneven for several wrappers.

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

### Phase A Status (2026-03-05)

- Completed:
  - in-memory default provider
  - optional JSON file persistence provider (`UseJsonFileSharedStateStore`)
  - merge semantics via `SharedStateOptions.MergeMode`
  - runtime snapshot/delta events
  - planner shared-state context injection
  - runtime UI-context shared-state injection (`agentblazor.shared_state_snapshot` / `agentblazor.shared_state_delta`)
  - AG-UI payload forwarding + message/run mapping
  - broader shared-state coverage for concurrency + reconnect semantics (in-memory and JSON stores)

### Completed Tool Render Lifecycle Refinement (2026-03-04)

- `AgentActionRender` now supports a full lifecycle fragment set:
  - `InProgress`
  - `Executing`
  - `Complete`
  - `Failed`
- chat streaming surfaces now render lifecycle fragments during tool-call progression.
- `AgentToolRender` alias added for friendlier `ToolId` (`Component.Action`) registrations.

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

### Phase B Status (2026-03-04)

- Completed (V1):
  - runtime agent lock keys:
    - `agentblazor.agent_name`
    - `agentblazor.agent_lock`
    - `agentblazor.current_route`
  - route-scoped agent resolution via:
    - route metadata agent keys
    - agent registration metadata (`route_prefixes`)
  - explicit-target per-agent conversation session scoping via `AgentConversationScope`
  - chat-surface route lock UX (`LockAgentToCurrentRoute`, locked selector mode)
  - demo multi-agent specialist registrations + route-locked assistant panel
  - integration tests for:
    - route lock resolution
    - invalid lock handling
    - explicit-target conversation scoping
- Completed (V2-Initial):
  - explicit chat handoff commands:
    - `/agent <name>`
    - `/handoff <name>`
    - `switch agent to <name>`
    - `/agents` for discovery
    - `/handoff-history [N]` for recent transfer diagnostics
  - route-lock-aware handoff flow with optional auto-navigation to target route prefix
  - optional handoff approval protocol:
    - pending handoff request state in chat
    - `/approve-handoff`
    - `/cancel-handoff`
    - `RequireHandoffApproval` switch on chat surface/panel/widget
    - `HandoffApprovalPolicy` for pair-scoped approval requirements
  - optional transfer-policy constraints:
    - `HandoffPolicy` switch on chat surface/panel/widget
    - blocks disallowed `from-agent -> to-agent` handoffs with explicit diagnostics
    - wildcard/deny rule semantics for broader orchestration modeling:
      - `*` allow any target
      - `!<agent>` deny specific target
      - `!*` deny all targets
    - loop guards:
      - `MaxHandoffsPerSession`
      - `MaxHandoffsPerPair`
      - `MaxHandoffsPerWindow`
      - `HandoffWindowMinutes`
      - `MaxPairHandoffsPerWindow`
      - `BlockImmediateReturnHandoff`
  - handoff context keys forwarded into runtime and surfaced in inspector timeline
  - in-chat orchestration diagnostics:
    - `/handoff-history [N]`
    - `/handoff-policy`
- Outstanding (V2):
  - richer transfer-policy constraints for complex multi-step cross-agent orchestration
  - richer cross-agent timeline/diagnostics in inspector
  - broader production examples of complex transfer policies in real workflows

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

### Phase C Status (2026-03-05)

- Completed (V1):
  - runtime now records per-turn timeline events for:
    - planning start/finish
    - planned actions
    - approvals/validation outcomes
    - execution outcomes
    - shared-state snapshots/deltas
    - run terminal states (finished/error/canceled)
  - inspector panel now surfaces:
    - run summary chips
    - event-kind timeline styling
    - pretty-printed JSON event payloads
- Completed (V2-initial):
  - event timeline text search
  - event-kind filter
  - handoff-only event filter for multi-agent diagnostics
  - visible/total event count in event view
  - run-level correlation controls:
    - runs filtered by agent
    - handoff-runs-only filter
    - per-run handoff count and summary
  - state-diff inspection tab:
    - parsed `StateDelta` key changes
    - added/updated/removed classification
    - state key/value/change-type filters
  - AG-UI stream introspection baseline:
    - stream-event run summary count
    - events-tab `Stream only` filter
    - phase filter + grouped-by-phase rendering
    - JSON payload top-level key lens chips in event items
    - optional JSON top-level key=value preview lens in event items
    - optional nested JSON path/value lens for deeper payload drill-down
  - handoff correlation depth:
    - inferred run-chain IDs across recent handoff-linked runs
    - run-list chain filtering and chain badge visibility
    - run-list handoff-pair filtering (`from -> to`)
- Outstanding (V2):
  - richer correlation across multi-agent handoffs
  - richer payload diff/correlation UX beyond event-local nested key lenses

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

### Phase D Status (2026-03-05)

- Completed:
  - Dojo workspace is now SQLite-backed with per-session persistence
  - recipe/ingredient/step/run-note state is stored and reloaded from DB
  - runtime-executed Dojo actions are now written into run notes via runtime subscriber hooks
  - schema bootstrap is automatic for local runs (no extra user infra required)
  - `/demo/components` file workflow is now persistence-backed:
    - per-session attached-file state
    - persisted local/remote mode state
    - persisted file + command workflow event history
    - persisted adapter-style workflow jobs:
      - remote handoff jobs (`remote_handoff`)
      - token validation jobs (`token_validation`)
    - command flows exposed for workflow simulation:
      - `sync_remote_handoff`
      - `validate_remote_tokens`
      - `create_audit_bundle`
    - pluggable remote storage adapter boundary:
      - default in-memory adapter path (no extra user setup)
      - optional HTTP adapter path (`DemoRemoteStorage.Adapter=Http`) for external-provider handoff/validation
      - optional HTTP auth/path controls (`HttpApiKey`, `HttpBearerToken`, `HttpHandoffPath`, `HttpValidatePath`)
      - retry semantics (`MaxAttempts`, `RetryDelayMilliseconds`) applied to transient adapter failures
      - integration contract tests for HTTP handoff/validate request+response behavior, endpoint-path overrides,
        nested error-message extraction, and transient failure classification
- Outstanding:
  - expand provider-specific HTTP contract coverage further (timeouts/network interruption simulations and advanced auth schemes such as OAuth/mTLS)
  - expand persistence-backed flows beyond Dojo + file baseline to richer external adapter scenarios

### Phase F: Dojo Product-Parity Pass (Priority 1 for demo UX)

### Objective

Make the dojo feel like a refined product showcase, not a collection of functional panels.

### Scope

- Match the CopilotKit dojo interaction model more closely:
  - left example rail inside the dojo page
  - `Preview` / `Code` / `Docs` mode switching in-page
  - embedded chat/artifact layouts per example
- Tighten visual fidelity:
  - cleaner spacing
  - lighter artifact canvases
  - better chat/artifact composition
  - less dashboard chrome
- Ensure each dojo example demonstrates a distinct capability clearly and predictably.

### Acceptance Criteria

- Each dojo example has a stable, distinct layout and a believable prompt-to-artifact workflow.
- The dojo can serve as the primary public product demo without relying on the old supplier-style narrative.
- The dojo experience feels coherent across all examples rather than like separate prototype panels.

### Phase F Status (2026-03-05)

- Completed:
  - dojo is now the primary demo entry point
  - internal dojo rail and mode toggles are wired
  - example selection and view changes are controllable from both UI and agent actions
  - embedded assistant surfaces replaced the old global dojo-side assistant pattern
- Outstanding:
  - final layout refinement against the CopilotKit dojo reference
  - tighter artifact sizing and chat composition in several examples
  - final cleanup of stale visual chrome inherited from earlier dashboard-oriented demo shells

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

### Phase E Status (2026-03-05)

- Completed:
  - runtime policy filtering now applies both agent policy and entitlement tier constraints before planning
  - validation path now returns deterministic tier diagnostics for blocked actions
  - AG-UI hosted runtime returns consistent blocked outcomes and user-visible diagnostics for tier-gated actions
  - free/paid/premium coverage added/updated across core + integration tests
- Outstanding:
  - keep coverage aligned as new component actions are introduced

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

1. Phase F (Dojo Product-Parity Pass)
2. Phase A (Shared State)
3. Phase B (Multi-Agent V1) - completed
4. Phase C (Inspector)
5. Phase D (Production Demo Flows)
6. Phase E (Tier Hardening) - completed
7. Phase B (Multi-Agent V2 handoff depth)

## Risk Register

- Shared state merge semantics can become brittle without strict event contracts.
- Multi-agent routing can introduce hidden cross-agent leakage if session boundaries are weak.
- Inspector scope creep can delay platform features if not phased.
- Demo hardening can overfit to recipe/supplier examples unless abstractions are generalized.
