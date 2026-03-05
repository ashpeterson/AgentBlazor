# AgentBlazor Development Status

Last updated: 2026-03-05

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
  - Compatibility + migration guidance documented for parity features (shared state, tool renders, multi-agent, remote adapter mode)
- Demo shell has been simplified around the current product story:
  - primary nav is now:
    - `/demo/dojo`
    - `/demo/components`
    - `/demo/components/attribute-based`
  - component explorer exposes:
    - `AgentDataGrid`
    - `AgentForm`
    - `AgentDialog`
    - `AgentTabs`
    - `AgentSelect`
    - `AgentAutocomplete`
    - `AgentDatePicker`
    - `AgentDateRangePicker`
    - `AgentTreeView`
    - `AgentStepper`
    - `AgentCommandBar`
    - `AgentFileUpload`
  - legacy supplier/workflow/docs routes now redirect into the new demo structure instead of remaining first-class navigation targets
- Provider-missing and chat resiliency UX landed:
  - startup warning + user-facing guidance
  - `ErrorBoundary` recovery
  - long-running warning and activity state indicators
- Shared state foundation landed (CopilotKit-style runtime parity baseline):
  - first-class store contract: `IAgentSharedStateStore`
  - default in-memory provider: `InMemoryAgentSharedStateStore` (no extra user infra required)
  - optional persisted provider: `UseJsonFileSharedStateStore(...)`
  - run/thread/agent keyed state snapshots and deltas
  - merge semantics via `SharedStateOptions.MergeMode` (`LastWriteWins`, `RejectStaleWrites`)
  - planner receives canonical `SharedState` context each turn
  - runtime emits `StateSnapshot` + `StateDelta` stream events
  - runtime accepts UI-provided shared-state context payloads:
    - `agentblazor.shared_state_snapshot`
    - `agentblazor.shared_state_delta`
  - hosted AG-UI adapter forwards shared-state payloads and tracks message->run correlation
  - shared-state stress coverage now includes deterministic concurrency and reconnect/reload stale-write tests
- Tool-render lifecycle parity improved:
  - custom tool/action render UI now supports `InProgress`, `Executing`, `Complete`, and `Failed`
  - lifecycle fragments now render during streaming activity (not only final result messages)
  - `AgentToolRender` added as a friendly alias over `AgentActionRender` (`ToolId` or `ComponentId` + `ActionId`)
- Multi-Agent V1 shipped:
  - runtime agent lock context keys:
    - `agentblazor.agent_name`
    - `agentblazor.agent_lock`
    - `agentblazor.current_route`
  - route-scoped agent resolution from:
    - route metadata (`RouteDefinition.Metadata`)
    - agent metadata route prefixes (`route_prefixes`)
  - explicit-target conversation isolation:
    - per-agent conversation session keying via `AgentConversationScope`
    - enabled by default when multiple agents are registered
  - chat surface/host integration:
    - `AgentChatSurface` supports route lock and locked-agent UX
    - AG-UI hosted adapter now uses shared runtime context key constants
  - demo now registers multiple route-scoped specialist agents and runs route-locked assistant mode in layout
- Multi-Agent V2 (initial handoff) shipped:
  - chat supports explicit handoff commands:
    - `/agent <name>`
    - `/handoff <name>`
    - `switch agent to <name>`
    - `/agents` (list available agents)
    - `/handoff-history [N]` (recent transfer diagnostics in chat)
  - route-locked mode can auto-navigate to target agent route prefix on handoff
  - optional explicit handoff approval protocol:
    - pending handoff request UI in chat
    - `/approve-handoff`
    - `/cancel-handoff`
    - `RequireHandoffApproval` parameter on chat surface/panel/widget
    - scoped approval policy support:
      - `HandoffApprovalPolicy` parameter for pair-specific approval requirements
      - can override global approval defaults to require approval only on sensitive agent transitions
  - optional handoff transfer-policy constraints:
    - `HandoffPolicy` parameter on chat surface/panel/widget
    - policy can allow/deny `from-agent -> to-agent` transfers with user-visible diagnostics
    - wildcard/deny target semantics supported in rules:
      - `*` allow any target
      - `!<agent>` deny specific target
      - `!*` deny all targets
    - loop-guard parameters on chat surface/panel/widget:
      - `MaxHandoffsPerSession`
      - `MaxHandoffsPerPair`
      - `MaxHandoffsPerWindow`
      - `HandoffWindowMinutes`
      - `MaxPairHandoffsPerWindow`
      - `BlockImmediateReturnHandoff`
  - in-chat handoff diagnostics command:
    - `/handoff-policy` (active rules + limits + current window counters)
  - runtime inspector now records handoff context (`AgentHandoff`) when forwarded in turn context
- Tier/policy hardening shipped:
  - runtime now enforces agent policy + tier gates before planning and during validation
  - blocked actions return deterministic diagnostics with current tier context
  - AG-UI and standard runtime paths both emit blocked-action outcomes consistently
  - coverage includes free/paid/premium transitions in core + integration suites
- Embedded inspector console V1 shipped:
  - runtime now records per-turn stage timeline events across all outcomes:
    - planning start/finish
    - planned actions
    - approval/validation events
    - execution summary
    - shared-state snapshots and deltas
    - terminal outcomes (finished/error/canceled)
  - inspector panel now shows:
    - run summary chips (planned count, approvals, state deltas, duration)
    - category-styled event timeline
    - pretty-printed JSON payload details for plan/state event records
- Embedded inspector console V2 (initial filtering) shipped:
  - event text search
  - event-kind filter
  - handoff-only filter toggle for multi-agent debugging
  - visible/total event count for active filters
  - runs-tab correlation controls:
    - agent filter
    - handoff-runs-only filter
    - per-run handoff count/summary display
  - state-diff tab:
    - parsed `StateDelta` key-level added/updated/removed entries
    - key/value/change-type filtering with visible/total counts
  - AG-UI stream introspection controls:
    - stream-event summary count in run chips
    - `Stream only` event filter toggle in events view
  - phase drill-down controls:
    - phase filter (`Planning`, `Validation`, `Execution`, `State`, `Handoff`, `Stream`, `Run`)
    - grouped-by-phase event rendering
    - JSON payload key lens chips for faster payload scanning
    - optional top-level JSON key=value preview chips for faster payload triage
    - optional nested payload path lens (`$.path.to.value`) for deeper AG-UI/state payload drill-down
  - cross-run handoff chain correlation:
    - inferred handoff chain IDs across recent runs
    - run-list chain filter for multi-agent debugging
    - run-list handoff-pair filter (`from -> to`) for path-specific triage
    - selected-run chain badge in event summary
- Dojo demo workflow backend hardening shipped:
  - `DojoWorkspaceService` now uses SQLite-backed session persistence
  - Dojo workspace state keyed by circuit session id
  - persisted recipe/ingredients/steps/run-notes with auto-bootstrap defaults
  - run notes now ingest runtime-executed Dojo action outcomes via `IAgentRuntimeEventSubscriber`
  - schema bootstrap is automatic (no manual migration steps for local demo use)
- Dojo experience state and route-level demo controls shipped:
  - dojo-local state model tracks:
    - selected integration
    - selected demo example
    - selected `Preview` / `Code` / `Docs` mode
  - assistant-callable dojo route actions now exist for:
    - changing dojo example
    - switching dojo view mode
    - switching integration label
  - dojo examples are wired as concrete agent surfaces instead of static screenshots:
    - `agentic-chat`
    - `backend-tool-rendering`
    - `human-loop`
    - `agentic-generative-ui`
    - `tool-based-generative-ui`
    - `shared-state`
    - `predictive-state`
- Dojo parity restructuring is underway:
  - dojo assistant is now embedded inside the dojo page instead of using the app-level global assistant pane
  - dojo has a dedicated internal rail for examples plus `Preview` / `Code` / `Docs` toggles
  - current implementation direction is explicitly aligned to CopilotKit-style dojo flows rather than the older supplier dashboard demo
- Components page file workflow hardening shipped:
  - `/demo/components` `AgentFileUpload` flow is SQLite-backed per session
  - attached file list + upload mode (`Local`/`Remote`) persist across refresh/reconnect
  - attach/remove/mode + command-bar actions now record persisted audit events for debugging/demo realism
  - remote adapter-style workflow operations shipped:
    - `sync_remote_handoff` creates persisted handoff jobs and completion events
    - `validate_remote_tokens` creates persisted token-validation jobs and verification/missing events
    - recent workflow jobs are surfaced in the Components UI to mirror runtime workflow traces
  - remote storage adapter boundary is now service-backed (not hardcoded in workflow service):
    - default `InMemoryRemoteStorageAdapter` requires no extra user infra
    - optional `HttpRemoteStorageAdapter` mode (`DemoRemoteStorage.Adapter=Http`) supports external provider handoff/validation
    - HTTP adapter supports API key auth, optional bearer auth, and configurable handoff/validate endpoint paths
    - transient retry policy (`MaxAttempts`, `RetryDelayMilliseconds`) with deterministic failure events
    - per-attempt retry events are now persisted (`RemoteHandoffRetry`, `RemoteTokenValidationRetry`) for debug traceability
    - HTTP adapter contract tests now validate request payloads/headers, endpoint-path overrides, nested error parsing, and transient-error mapping in integration tests

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
- Clarification recovery hardening:
  - runtime now auto-recovers direct single-field form edits from clarification loops when user intent is explicit (for example `set recipe title to test`)
  - mounted form field metadata (`fields`) is used to resolve the best `set_field` target deterministically
- Demo refinement pass:
  - Dojo workspace prompt examples simplified for first-run success.
  - Layout density reduced and readability increased in demo shell and docs-style sections.
  - Assistant surface copy/placeholders aligned to actual Dojo workflow commands.
- Test coverage added for all above behavior:
  - component tests for `AgentFormPageBase` partial updates
  - integration tests for submit filtering and mounted-action validation
  - integration regression test for clarification-to-`set_field` auto-recovery

## Outstanding (Known Gaps)

### Product/Platform Gaps

- Dojo parity is not finished:
  - example shells are functional, but the visual fidelity and interaction polish still fall short of the CopilotKit dojo reference
  - several dojo examples still need tighter artifact/chat composition so they feel like a product showcase rather than internal demo panels
- Multi-agent orchestration depth is still evolving:
  - pair-scoped policies and approval rules are shipped, but broader multi-step orchestration presets/workflow templates are not yet productized.
- Demo breadth still exceeds demo polish in some areas:
  - the core product story is now dojo + components explorer, but several older flows still exist as compatibility redirects and need continued cleanup/consistency work
- Component-product depth is still uneven:
  - wrapper breadth is strong, but some components need richer end-to-end scenarios so the demo proves production usage rather than isolated control calls

### Runtime/Policy Gaps

- Capability consistency still needs ongoing alignment:
  - static catalog vs mounted live behavior must stay in sync as wrappers evolve.
- Domain scenario depth remains thin for:
  - tree/stepper orchestration
  - command bar workflows
  - richer production connector package depth (advanced auth schemes and broader external-adapter scenario coverage)

## What Needs Doing Next (Product Priority)

1. Finish CopilotKit-style Dojo parity:
   - tighten layout, spacing, and embedded chat/artifact fidelity for all dojo examples
   - ensure every dojo example feels like a clean agent workflow showcase, not a dashboard fragment
2. Expand deep end-to-end component scenarios:
   - especially `AgentTreeView`, `AgentStepper`, `AgentCommandBar`, and `AgentFileUpload`
   - keep the component explorer aligned to a MudBlazor-style docs experience
3. Embedded runtime inspector panel:
   - continue iterative UX depth for cross-run and cross-agent correlation
4. Package broader production connector/auth examples for external adapter integrations.

## Immediate Execution Plan

1. Finish the dojo refinement pass and lock the new demo shell structure before adding more scenarios.
2. Build production-style walkthroughs for tree/stepper/command/file wrappers with persisted state and approvals.
3. Expand inspector correlation beyond current run-chain and pair filters into reusable troubleshooting presets.
4. Publish advanced connector/auth examples (beyond API key/bearer) on top of the HTTP adapter boundary.

## Verification Snapshot

Latest local validation after the 2026-03-05 dojo restructuring pass:

- `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj` passed
- `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj` passed
- `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj` passed
- `dotnet build demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj` passed

## Reference Docs

- Architecture: `docs/architecture.md`
- Expansion roadmap: `docs/component-expansion-plan.md`
- Tier model: `docs/pricing-tiers.md`
