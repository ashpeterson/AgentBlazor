# AgentBlazor Architecture (Phase 0)

## Goal

Create a modular foundation for an agentic Blazor layer on top of MudBlazor with:
- A built-in first-party component-aware default agent
- Extensibility for user-registered custom agents
- A component capability catalog shared by agents and UI

## Current Project Layout

```text
src/
  AgentBlazor.Core
  AgentBlazor.Hosting
  AgentBlazor.Components
  AgentBlazor.DefaultAgent
  AgentBlazor.ProviderAdapters
  AgentBlazor.Licensing
demo/
  AgentBlazor.Demo
tests/
  AgentBlazor.Core.Tests
  AgentBlazor.Components.Tests
  AgentBlazor.IntegrationTests
```

## Core Design Decisions

1. `AgentBlazor.Core` owns contracts/options/registries and DI entry points.
2. Built-in default agent is represented as first-party registration data from startup.
3. Component capability catalog is explicit and configurable at startup.
4. Custom agents are additive and stored in `IAgentRegistry`.
5. Other projects are currently placeholders/extensions over core contracts.
6. UI strategy is MudBlazor-first: AgentBlazor adds agent capabilities and orchestration over MudBlazor instead of replacing MudBlazor's base component set.

## Startup Flow

1. App calls `AddMudServices(...)` and `AddAgentBlazor(...)`.
2. Unified registration configures provider, AgentBlazor runtime services, and AG-UI hosting services.
3. Options are configured (`AgentBlazorOptions`) with built-in default agent enabled.
4. Component catalog is built from shipped defaults plus user overrides.
5. Agent registry is built with built-in default agent plus custom agents.
6. Runtime services execute turns (`IAgentRuntime`) and component actions (`IComponentActionExecutor`).
7. Optional hosted endpoint is exposed via `MapAgentBlazorAgUiRun(...)` -> `MapAGUI(...)`.
8. UI components consume these services.

## Next Architecture Step

Advance from baseline Phase 5 to pilot hardening:
- telemetry sink exporter integrations (OpenTelemetry/App Insights/custom pipelines)
- richer run/thread observability views in demo and docs
- package/release hardening for private preview feedback loops

## Phase 2 Runtime (Framework-Native)

- `FrameworkBackedAgentRuntime` uses Microsoft Agent Framework `ChatClientAgent` for turn execution.
- Component capabilities are exposed as framework tools (`AIFunction`) and executed via tool invocation.
- MudBlazor capability taxonomy is defined in `AgentComponentV1CapabilityProfile` (`agentblazor.components.v1`) and applied to the default catalog.
- Typed Mud execution contracts are defined (`IDataGridActionExecutor`, `IDialogActionExecutor`, `IFormActionExecutor`, `INavigationActionExecutor`, `ITabsActionExecutor`) with default no-op implementations and replaceable DI registration.
- `IComponentActionExecutor` now dispatches known MudBlazor v1 capability actions to typed executors; unknown mappings fail safely with actionable errors.
- Agent policy now supports both component-level allow lists (`AllowedComponents`) and per-action allow lists (`AllowedActions`) applied before tool registration through shared evaluation (`ComponentActionPolicyEvaluation`) used by runtime and hosted AG-UI paths.
- Mud capability actions are tier-mapped (`Free`/`Paid`/`Premium`) through `AgentComponentTierBoundaries`; entitlement filtering is applied before framework tool registration in both runtime and hosted AG-UI paths.
- Runtime and hosted AG-UI paths emit run telemetry through `IAgentBlazorTelemetrySink` (`Started`/`Finished`, outcomes, execution counts, context flags).
- Policy-filtered blocked actions are observable through runtime response diagnostics and runtime/hosting logs when policy excludes requested actions.
- User tool registration (`WithToolsFromAssembly(...)`) now maps to invokable framework tools created with `AIFunctionFactory.Create(MethodInfo, ...)`.
- Approval-gated component actions are evaluated at tool execution time through shared approval policy (`ComponentActionApprovalPolicy`) using invocation context (`agentblazor_context` / AG-UI `ag_ui_context`).
- HTTP AG-UI hosting now uses Microsoft Agent Framework AG-UI ASP.NET Core hosting primitives (`AddAGUI`/`MapAGUI`) through `MapAgentBlazorAgUiRun(...)`.
- Integration coverage now includes Mud execution-matrix tests (policy + approvals + assembly-tool coexistence) and hosted `/agentblazor/agui/run` AG-UI endpoint approval tests.
- Demo host now includes MudBlazor registration (`AddMudServices`) and a AgentDataGrid scenario page (`/mud-grid-agent`) backed by a demo `IDataGridActionExecutor` state adapter.
- Demo host also includes a AgentDialog + AgentForm scenario page (`/mud-dialog-form-agent`) backed by demo `IDialogActionExecutor`/`IFormActionExecutor` state adapters.
- Demo host includes a mixed routing scenario page (`/mud-agent-routing`) that contrasts default-route and explicit custom-agent policy surfaces for deterministic route behavior.
- `NoOpComponentActionExecutor` remains as the default execution backend until concrete component dispatchers are wired.
- Provider registration now supplies framework `IChatClient` instances (OpenAI/Azure OpenAI) consumed directly by runtime.
- Custom keyword/rule planners, custom provider response adapters, and custom AG-UI runtime event-stream contracts have been removed.
- Dependency and SDK versions are centrally pinned (`Directory.Packages.props`, `global.json`) with lock-file restore strategy (`Directory.Build.props`) validated in CI.

## Phase 1 Onboarding Contract

- `AddAgentBlazor(...)` is the primary entry point.
- `AddMudServices(...)` is required for MudBlazor-first integration scenarios.
- Components are consumed via `@using AgentBlazor.Components`.
- MudBlazor components are consumed via `@using MudBlazor`.
- Static assets are exposed at:
  - `_content/AgentBlazor/AgentBlazor.min.css`
  - `_content/AgentBlazor/AgentBlazor.min.js`
- Provider components:
  - `AgentThemeProvider`
  - `AgentPopoverProvider`
  - `AgentDialogProvider`
  - `AgentSnackbarProvider`
- Quick bootstrap component: `AgentBlazorShell`.

## Reference Index

- See `docs/spec-references.md` for AG-UI and Microsoft Agent Framework source/spec links used by this repository.
