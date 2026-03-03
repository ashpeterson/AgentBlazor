# AgentBlazor Architecture (Current State)

Last updated: 2026-03-03

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

## Validation and Policy Guardrails

Recent runtime hardening adds two important protections:

- `PlanValidator` validates actions against mounted live component action sets when a matching component is mounted on the route.
  This prevents executing catalog-defined actions that are not actually exposed on the live component instance.
- `AgentRuntime` suppresses `AgentForm.submit` unless submit intent is explicit (`submit`, `save`, `confirm`, `finalize`, `send`).
  This prevents non-submit edit prompts from being diverted into approval loops.

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

- first-class shared state contracts and sync semantics
- first-class multi-agent runtime modes and agent-specific tooling boundaries
- complete embedded inspector experience for planning/validation/execution transparency
- production-grade persistence-backed demo workflows

Tracking for these is maintained in:

- `docs/STATUS.md`
- `docs/component-expansion-plan.md`
