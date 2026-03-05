# AgentBlazor Architecture (Current State)

Last updated: 2026-03-05

## Purpose

Describe the current production architecture in this repository as implemented in code.

## Solution Structure

```text
src/
  AgentBlazor.Components
  AgentBlazor.Core
  AgentBlazor.Hosting
  AgentBlazor.ProviderAdapters
  AgentBlazor.DefaultAgent
  AgentBlazor.Licensing

demo/
  AgentBlazor.Demo

tests/
  AgentBlazor.Core.Tests
  AgentBlazor.Components.Tests
  AgentBlazor.IntegrationTests
```

## Layer Responsibilities

### AgentBlazor.Core

Owns runtime behavior and core contracts:

- Agent planning and execution pipeline (`AgentPlanner` + `PlanValidator` + `PlanExecutor` + `AgentRuntime`)
- Component capability contracts and policy helpers
- Circuit-aware component registry interfaces
- Route registry and route intent matching
- Runtime middleware contract/pipeline
- Prompt tracing and telemetry contracts
- Service tool contracts and MCP abstraction

### AgentBlazor.Hosting

Owns host integration and endpoint exposure:

- Unified registration options (`AgentBlazorRegistrationOptions`)
- `AddAgentBlazor(...)` orchestration of provider + hosting + core services
- AG-UI endpoint mapping (`MapAgentBlazorAgUiRun`, `MapAgentBlazorEndpoints`)
- Hosted AG-UI adapter (`DeterministicAgUiHostedAgent`)

### AgentBlazor.Components

Owns Blazor UI surface and wrappers:

- Chat components (`AgentChatSurface`, `AgentChatWidget`, etc.)
- Tool render registration components (`AgentToolRender`, `AgentActionRender`)
- Generative UI rendering components
- Inspector component
- Agent wrappers (`AgentDataGrid`, `AgentDialog`, `AgentForm`, `AgentNavMenu`, `AgentTabs`, `AgentSelect`, `AgentAutocomplete`, `AgentDatePicker`, `AgentDateRangePicker`, `AgentTreeView`, `AgentStepper`, `AgentCommandBar`, `AgentFileUpload`)
- `AgentFormPageBase<TModel>` for auto-generated form fill actions

### AgentBlazor.ProviderAdapters

Owns provider registration to `IChatClient`:

- OpenAI
- Azure OpenAI
- Ollama

### AgentBlazor.DefaultAgent

Provides default descriptor plumbing for the default component-aware agent metadata.

### AgentBlazor.Licensing

Owns tier primitives and entitlement service contract (`Free`, `Paid`, `Premium`).

## Dependency Direction

Current compile-time references:

- `AgentBlazor.Core` -> `AgentBlazor.Licensing`
- `AgentBlazor.Hosting` -> `AgentBlazor.Core`, `AgentBlazor.ProviderAdapters`, `AgentBlazor.DefaultAgent`, `AgentBlazor.Licensing`
- `AgentBlazor.Components` -> `AgentBlazor.Core`, `AgentBlazor.Hosting`

This keeps runtime orchestration in Core while Hosting wires external boundaries and Components renders UX.

## Registration and Startup Flow

Typical app startup:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI("...", "gpt-4o-mini");
    // optional: options.UseProLicense("AB-PRO-...");
    // optional: options.AddTool(...);
    // optional: options.UseMcpServer(...);
    // optional: options.UseMiddleware(...);
    // optional: runtime event subscriber + persistent store shown below
});

app.MapAgentBlazorEndpoints();
```

What `AddAgentBlazor(...)` does:

1. Applies provider registration from `AgentBlazorRegistrationOptions`
2. Registers AG-UI hosting services (`AddAgentBlazorHosting`)
3. Registers core runtime services (`AddAgentBlazorServices`)
4. Applies options and builder customizations

## Runtime Execution Pipeline

`AgentRuntime` executes turns using Plan -> Validate -> Execute:

1. Build request context (session, mounted components, allowed components/actions, routes, tools, history)
2. Plan with `AgentPlanner`
3. Normalize/resolution pass (component/action targeting)
4. Apply policy filtering (for example non-explicit form submit suppression)
5. Validate plan with `PlanValidator`
6. Execute via `PlanExecutor`
7. Persist conversation turn and emit telemetry/stream events

`AgentRuntime` implements both:

- `IAgentRuntime` (single response)
- `IAgentRuntimeStreaming` (run stream + reconnect + stop)

## Shared State Model

Shared state is now a first-class runtime contract:

- Store contract: `IAgentSharedStateStore`
- Default provider: `InMemoryAgentSharedStateStore` (registered by default, no extra infra required)
- Optional persisted provider: `JsonFileAgentSharedStateStore` via `UseJsonFileSharedStateStore(...)`
- Merge policy: `SharedStateOptions.MergeMode`
  - `LastWriteWins` (default)
  - `RejectStaleWrites`
- Keying model: `agentName -> sessionId(thread) -> runId`
- Correlation: hosted AG-UI adapter records `messageId -> runId` mappings in the shared state store

Per turn, runtime behavior is:

1. Build/refresh canonical shared state from mounted component state + route context.
2. Persist snapshot to shared-state store for the active run.
3. Pass shared state into planner request context (`ActionPlanRequest.SharedState`).
4. Emit streaming state events:
   - `StateSnapshot` at turn start
   - `StateDelta` (+ refreshed `StateSnapshot`) after execution when state changes

Runtime also supports direct UI-context state sync for non-wrapper state:

- `agentblazor.shared_state_snapshot` (JSON object of `string -> string`)
- `agentblazor.shared_state_delta` (JSON object of `string -> string|null`)

This provides CopilotKit-style state synchronization semantics without requiring user-managed services.

## Component Capability Model

Canonical shipped component profile is in `AgentComponentCapabilityProfile`:

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

Runtime action discovery is hybrid:

- First uses `[AgentAction]` discovery
- Falls back to `GetCapability()` for dynamic components (notably `AgentFormPageBase<TModel>`)

Convention-mode discovery now reduces annotation boilerplate for attribute-driven components:

- `[AgentAction]`, `[AgentReadable]`, and `[AgentParam]` descriptions are optional
- Action descriptions default to humanized method names when omitted
- Non-nullable parameters are inferred as required (unless overridden by `[AgentParam]`)
- Enum parameters infer allowed values automatically when not specified
- `AgentControllableComponentBase` infers `ComponentType` and default `AgentId` (with optional `[AgentComponent(...)]` overrides)

`AgentFormPageBase<TModel>` now exposes partial-update aliases for dynamic form control:

- `fill_<form>`
- `set_<form>`
- `update_<form>`
- `set_field`

This is intentionally distinct from the generic `AgentForm` wrapper behavior.
`AgentForm` remains intentionally limited to reliable wrapper actions (`validate`, `reset`, `submit`) for mounted form instances.

## Circuit and Session Model

Blazor component registry is circuit-scoped:

- `AgentComponentRegistryHub` is singleton and indexes registries by session id
- `IAgentComponentRegistry` is scoped (`CircuitAgentComponentRegistry`)
- Chat surfaces pass effective session id into runtime requests

This avoids cross-circuit leakage and supports reconnect/replay flows.

Execution fallback in `NoOpComponentActionExecutor` also resolves Agent-prefixed catalog IDs
(for example `AgentSelect`) to wrapper component types (for example `Select`) when dispatching to mounted components.

## Multi-Agent V1 Model

Multi-agent selection is now first-class in runtime and chat context:

- Runtime context keys:
  - `agentblazor.agent_name`
  - `agentblazor.agent_lock`
  - `agentblazor.agent_handoff_from`
  - `agentblazor.agent_handoff_to`
  - `agentblazor.agent_handoff_at`
  - `agentblazor.current_route`
- Resolution precedence:
  1. explicit request agent
  2. locked/context agent
  3. route-scoped agent resolution
  4. configured default agent fallback (unless lock mode is active)
- Route-scoped resolution sources:
  - route metadata (`RouteDefinition.Metadata`)
  - agent registration metadata route patterns (`route_prefixes`)
- Conversation isolation:
  - explicit-target runs are stored under `AgentConversationScope` session keys when multiple agents are registered
  - avoids cross-agent conversation leakage while keeping default single-agent behavior intact
- Chat handoff commands:
  - `/agent <name>`
  - `/handoff <name>`
  - `/agents`
  - `/handoff-history [N]`
  - `/handoff-policy`
  - `/approve-handoff`
  - `/cancel-handoff`
  - `switch agent to <name>`
  - in route-lock mode, chat can auto-navigate to the target agent's configured route prefix
  - optional `RequireHandoffApproval` adds an explicit pending-handoff confirmation step before transfer
  - optional `HandoffApprovalPolicy` provides pair-scoped approval requirements while preserving global default behavior
  - optional `HandoffPolicy` allows route/app-defined transfer constraints:
    - explicit allow target names
    - wildcard `*` allow
    - deny tokens (`!<agent>`, `!*`)
  - optional loop guards:
    - `MaxHandoffsPerSession`
    - `MaxHandoffsPerPair`
    - `MaxHandoffsPerWindow`
    - `HandoffWindowMinutes`
    - `MaxPairHandoffsPerWindow`
    - `BlockImmediateReturnHandoff`

## Inspector Console V1 Model

Runtime inspector recording is now integrated into `AgentRuntime` turn flow:

- a run record is written for all terminal outcomes:
  - success
  - clarification/approval/validation exits
  - provider-missing/no-agent exits
  - canceled and exception paths
- recorded event timeline includes:
  - planning start/finish + planned actions
  - approval and validation events
  - execution summary events
  - shared state snapshots and deltas
  - terminal run events
- inspector panel (`AgentInspectorPanel`) now renders:
  - run summary chips (planned count, approvals, state deltas, duration)
  - category-styled event timeline
  - formatted JSON payloads for plan/state-rich events
  - event timeline filters (text search, event-kind filter, handoff-only toggle)
  - AG-UI stream diagnostics controls (`Stream only` toggle + stream-event summary count)
  - event phase diagnostics (phase filter + grouped-by-phase view)
  - payload key lens chips for JSON event details (top-level key scan)
  - optional payload key=value preview lens for quick top-level value inspection
  - optional nested payload key-path lens (`$.path`) for deeper event payload tracing
  - run correlation filters (agent filter + handoff-runs-only) with handoff summaries in run list
  - inferred handoff chain IDs with run-list chain filtering for cross-agent sequence tracing
  - run-list handoff-pair filtering (`from -> to`) for path-specific multi-agent diagnostics
  - state-diff view that parses `StateDelta` payloads into added/updated/removed key changes with filter controls

This gives in-app observability of runtime decisions without requiring log scraping.

## Validation and Policy Guardrails

Recent runtime hardening adds three important protections:

- `PlanValidator` validates actions against mounted live component action sets when a matching component is mounted on the route.
  This prevents executing catalog-defined actions that are not actually exposed on the live component instance.
- `AgentRuntime` suppresses `AgentForm.submit` unless submit intent is explicit (`submit`, `save`, `confirm`, `finalize`, `send`).
  This prevents non-submit edit prompts from being diverted into approval loops.
- `AgentRuntime` can auto-recover direct single-field form edits when a planner asks for unnecessary clarification:
  explicit `set/change/update ... to ...` prompts are deterministically converted to mounted-form `set_field` actions
  when field metadata is available.
- `AgentRuntime` enforces action-level tier/policy gates deterministically:
  - pre-plan policy filtering returns a deterministic blocked response when no actions are allowed
  - validation returns explicit tier diagnostics when a planned action requires a higher entitlement tier
  - blocked reasons flow through both standard runtime and AG-UI stream responses

## Runtime Event Subscription Hooks

Open-source/non-paid runtime hooks are available via `IAgentRuntimeEventSubscriber`.

- `OnTurnStartedAsync`
- `OnTurnFinishedAsync`
- `OnToolExecutionStartedAsync`
- `OnToolExecutionFinishedAsync`
- `OnErrorAsync`

Registration:

```csharp
builder.Services
    .AddAgentBlazorServices()
    .AddRuntimeEventSubscriber<MyRuntimeEventSubscriber>();
```

Demo usage:

- the Dojo demo registers `DojoRuntimeEventSubscriber` to persist executed `dojo-*` tool outcomes into SQLite run notes, keeping the run-note panel aligned with real runtime activity.
- the Components demo file pipeline persists file state, workflow events, and adapter-style job records (`remote_handoff`, `token_validation`) via `DemoFileWorkflowService`.
- `DemoFileWorkflowService` now delegates remote handoff/validation to `IDemoRemoteStorageAdapter`:
  - `InMemoryRemoteStorageAdapter` default (no extra infra)
  - optional HTTP adapter mode via `DemoRemoteStorage` settings
  - HTTP mode supports API key auth, optional bearer auth, and configurable handoff/validate paths
  - retry handling for transient adapter failures is applied in the workflow service
  - HTTP adapter request/response contract behavior is covered in integration tests (headers, paths, nested errors, transient mapping)

These hooks are observer-only and do not alter plan/validation/execution semantics.

## Routing Model

`IRouteRegistry` is implemented by `InMemoryRouteRegistry`.

- Scans `AgentBlazorOptions.AssembliesToScan` for `[Route]`
- Supports exact, alias, and fuzzy intent resolution
- Exposed to planner as available routes per turn

## Tools and MCP

Two extension paths are available:

- In-process service tools (`AddTool(...)`) via `IAgentServiceToolRegistry`
- MCP-backed tools (`UseMcpServer(...)`) via `IMcpToolProvider`

Both feed planner/runtime as available tools for the current turn.

## Middleware

Turn middleware is supported via delegate registration:

- `UseMiddleware(Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>)`
- Executed by `AgentMiddlewarePipeline`

Note: `UseMiddleware<TMiddleware>()` currently registers the type but throws at runtime if used directly; delegate middleware is the stable path today.

## AG-UI Hosting

AG-UI hosting uses `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`:

- Services: `AddAGUI()`
- Endpoint: `MapAGUI(...)` through `MapAgentBlazorAgUiRun(...)`
- Adapter: `DeterministicAgUiHostedAgent` bridges AG-UI run payloads to `AgentRuntime`

Default endpoint pattern is `/agentblazor/agui/run`.

## Conversation Store Options

Default registration uses in-memory conversation history:

```csharp
builder.Services.AddAgentBlazorServices();
```

Persistent, restart-safe JSON store is available in Core:

```csharp
builder.Services
    .AddAgentBlazorServices()
    .UseJsonFileConversationStore("App_Data/agentblazor-conversations.json", options =>
    {
        options.MaxTurnsPerSession = 100;
        options.SessionTimeout = TimeSpan.FromDays(7);
    });
```

Custom stores can also be plugged in:

```csharp
builder.Services
    .AddAgentBlazorServices()
    .UseConversationStore<MyConversationStore>();
```

## Compatibility and Migration Notes

Current compatibility guarantees for consumer apps:

- default local usage remains zero-infra:
  - in-memory conversation + shared-state providers are still the defaults
  - optional persistence/remote adapters are opt-in via explicit configuration
- additive API evolution:
  - newer options and parameters are added with safe defaults (existing app startup continues to work)
  - `AgentToolRender` is additive and does not replace/remove `AgentActionRender`
- runtime behavior guardrails are backwards-safe:
  - submit suppression affects only non-explicit submit intent
  - explicit submit/save/confirm/send flows remain unchanged

Migration guidance when adopting newer parity features:

- move custom tool render registrations to `AgentToolRender` over time for clearer `ToolId` semantics
- if enabling HTTP remote adapter mode, keep defaults first and then opt in incrementally:
  - `HttpApiKey` and/or `HttpBearerToken`
  - `HttpHandoffPath` and `HttpValidatePath` if your adapter paths differ
- for multi-agent apps, enable route lock and handoff approval in stages:
  1. `LockAgentToCurrentRoute`
  2. `RequireHandoffApproval`
  3. `HandoffPolicy` and loop guards

## Paid Feature Wiring

By default (`Free`), no-op/limited paid services are registered:

- `IActionHistoryStore` -> `NullActionHistoryStore`
- `IAdaptiveSuggestionService` -> `StaticSuggestionService`
- `IProactiveInsightService` -> `NullProactiveInsightService`
- `IAgentInspectorStore` -> `NullAgentInspectorStore`

`UseProLicense("AB-PRO-..." | "AB-ENT-...")` switches these to paid implementations and sets `AgentBlazorOptions.LicensedTier`.

## Generative UI Surface

Generative UI rendering is implemented in `AgentBlazor.Components/GenerativeUI`:

- `AgentGenerativeSurface`
- `AgentGeneratedCard`
- `AgentGeneratedForm`
- `AgentGeneratedTable`
- `AgentGeneratedChart`

This is integrated into chat surfaces with `EnableGeneratedUi` context signaling.

## Clean Architecture Notes

The current architecture is cleanest when treated as:

- Core = deterministic runtime and contracts
- Hosting = transport and registration boundary
- Components = UI boundary
- ProviderAdapters/Licensing = infrastructure adapters

For new work, keep this direction:

- Avoid moving planning/execution logic into Components or Hosting
- Keep provider-specific behavior in ProviderAdapters
- Keep endpoint/protocol adaptation in Hosting
- Keep domain policies and capability logic in Core

## Current Strategic Gaps

The architecture is stable for current wrapper automation, but product-level parity work is still required for:

- advanced multi-agent handoff/orchestration semantics beyond route lock
- inspector V2 depth (filtering/correlation across multi-agent runs, richer diff tooling)
- deeper production connector contract coverage layered behind the current persistence-backed Dojo + file workflow baseline

Tracking for these is maintained in:

- `docs/STATUS.md`
- `docs/component-expansion-plan.md`
