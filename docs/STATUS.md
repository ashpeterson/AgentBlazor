# AgentBlazor Development Status

Last updated: 2026-03-02

## Completed Recently

- Root README quickstart created with install/setup/provider guidance
- Provider-missing UX improved:
  - Startup console warning when no provider configured
  - User-facing chat error message with setup snippet
- Chat resiliency improved:
  - `ErrorBoundary` wrapper and `Try Again` recovery
  - 10-second "taking longer than expected" warning
  - Activity success/failure visuals (`✓` / `✗`)
- Form workflow improvements:
  - `AgentFormPageBase<TModel>` state now includes `fields`, `fieldValues`, `fieldMetadata`
  - Demo onboarding moved to `AgentFormPageBase<TModel>` pattern
- Model discovery/runtime updates:
  - Dynamic capability fallback via `GetCapability()` when attribute discovery is not enough
  - Improved component resolution path in no-op executor
- Phase 0 completed:
  - Dynamic input schema parser hardened for comma/parenthesis-heavy descriptions
  - Core provider-missing assertion updated
- Phase 1 completed (core surface):
  - Added `AgentSelect` (`open`, `close`, `set_value`, `clear`)
  - Added `AgentAutocomplete` (`set_query`, `select_option`, `clear`)
- Phase 2 completed (core surface):
  - Added `AgentDatePicker` (`set_date`, `clear`)
  - Added `AgentDateRangePicker` (`set_range`, `clear`)
  - Added runtime fallback matching for Agent-prefixed catalog IDs to component types
- Phase 3 completed (core surface):
  - Added `AgentTreeView` (`expand`, `collapse`, `select_node`)
  - Added `AgentStepper` (`go_to_step`, `next`, `previous`)
- Phase 4 completed (core surface):
  - Added `AgentCommandBar` (`invoke_command`, `list_commands`)
  - Added `AgentFileUpload` (`attach`, `remove`, `list_files`)
- Phase 5 completed (open-source ergonomics):
  - Added public runtime subscriber hooks via `IAgentRuntimeEventSubscriber`
    (`OnTurnStartedAsync`, `OnTurnFinishedAsync`, tool start/finish, error surfaced)
  - Added pluggable conversation store registration APIs:
    - `UseConversationStore<TStore>()`
    - `UseJsonFileConversationStore(path, configure?)`
  - Added file-backed `JsonFileConversationStore` for restart-safe history
  - Added core/integration tests for subscriber + persistence behavior

## Docs and Architecture Audit (This Review)

The `/docs` folder was re-reviewed against current code.

### Updated to match code

- `docs/architecture.md` rewritten to current architecture boundaries and runtime flow
- `docs/pricing-tiers.md` rewritten to current tier wiring and enforcement status
- `docs/STATUS.md` updated to reflect current project state

### Corrected stale assumptions

- Demo/Landing work is already implemented (`DemoLayout`, `DemoNavMenu`, landing section composition)
- Current registration path is `AddAgentBlazor(...)` + `MapAgentBlazorEndpoints()`
- Current runtime is `AgentRuntime` with Plan -> Validate -> Execute
- AG-UI hosting is mapped through `MapAgentBlazorAgUiRun(...)`

## Current Capability Snapshot

### Shipped and actively used

- Wrappers:
  - `AgentDataGrid`, `AgentDialog`, `AgentForm`, `AgentNavMenu`, `AgentTabs`
  - `AgentSelect`, `AgentAutocomplete`, `AgentDatePicker`, `AgentDateRangePicker`
  - `AgentTreeView`, `AgentStepper`
  - `AgentCommandBar`, `AgentFileUpload`
- Chat surfaces: `AgentChatSurface`, `AgentChatWidget`, streaming updates, approvals/clarifications
- Generative UI rendering components (card/form/table/chart)
- Service tools (`AddTool`) and MCP tool providers (`UseMcpServer`)
- Route scanning and intent-to-route matching (`InMemoryRouteRegistry`)
- Circuit-scoped component registry model
- Demo route `/demo/components` showcasing new wrapper actions and prompt examples

### Known technical gaps

- Tier boundaries are defined, but full action-level hard enforcement remains partial
- New wrappers are implemented/test-covered and have a consolidated demo route;
  richer domain-specific scenarios are still limited

## Next Plan

Detailed expansion plan is tracked in:

- `docs/component-expansion-plan.md`

Immediate implementation sequence:

1. Add deeper business workflow demos for tree/stepper/command/file actions
2. Add end-to-end file service integration samples (local policy vs remote handoff)
