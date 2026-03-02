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
- generative UI spec + Razor renderer surface for Blazor-native dynamic workflows

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
- Capability matrix (shipped vs roadmap): `docs/agentblazor-capability-matrix.md`
- Generative UI contract v0: `docs/generative-ui-spec-v0.md`
- Product push roadmap: `docs/agentblazor-push-roadmap-q2-2026.md`
- Positioning + docs IA: `docs/agentblazor-positioning-and-docs-ia.md`

 Here is Claude's plan:                                                                                                                                                                                      
╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌
 AgentBlazor Complete Refactor Plan                                                                                                                                                                          

 Context

 AgentBlazor aims to be the CopilotKit equivalent for .NET/Blazor. CopilotKit succeeds because its developer surface is trivially simple (useCopilotAction() hook, one call). AgentBlazor has the right
 ideas but built the infrastructure layers first: the result is a system where adding agent control to a component requires GetCapability() + ExecuteActionAsync() + JSON schema strings + catalog
 registration — vs CopilotKit's single decorator.

 Additional blockers: the planner has a repair loop (second LLM call) because the first frequently fails; the IAgentComponentRegistry is a singleton causing cross-circuit leakage in Blazor Server; and
 ~2,500 lines of dead weight (keyword intent classifier, argument normalizer, feature gating, licensing) add complexity without value.

 Goal: Attribute-based registration ([AgentAction], [AgentReadable]), simplified planner (no repair loop), circuit-scoped registry, pruned dead code.

 ---
 Target Developer Experience

 // Any Blazor component
 public class SupplierGrid : AgentControllableComponentBase
 {
     [AgentReadable("Suppliers shown in grid")]
     public IEnumerable<Supplier> Suppliers => _suppliers;

     [AgentAction("Filter suppliers by risk level")]
     public async Task FilterByRisk(
         [AgentParam("Risk level", AllowedValues = "high,medium,low")] string level)
     {
         _filter = level;
         await LoadAsync();
         StateHasChanged();
     }
 }

 // Program.cs — done
 builder.Services.AddAgentBlazor(options =>
 {
     options.UseOpenAI(apiKey);
     options.AgentInstructions = "Help users manage supplier risk.";
 });

 Built-in wrappers (AgentDataGrid, AgentForm, etc.) use [AgentAction] internally — zero setup for standard components.

 ---
 Execution Order

 Phase 1 — New Attribute Files (zero deps, create first)

 Create src/AgentBlazor.Core/Attributes/:
 - AgentActionAttribute.cs — [AgentAction("description")] on methods; optional ActionId, RequiresApproval
 - AgentReadableAttribute.cs — [AgentReadable("description")] on properties; optional StateKey, MaxItems = 5
 - AgentParamAttribute.cs — [AgentParam("description")] on parameters; optional Required, AllowedValues

 Phase 2 — Discovery Engine (new file, zero deps)

 Create src/AgentBlazor.Core/Runtime/Discovery/AgentActionDiscovery.cs:
 - BuildCapability(IAgentControllable) — reflects [AgentAction] methods → ComponentCapability
 - BuildState(IAgentControllable) — reflects [AgentReadable] props → ComponentState
 - ExecuteActionAsync(IAgentControllable, AgentAction) — finds method by action id, maps args via [AgentParam] names, invokes via reflection with Convert.ChangeType coercion

 Phase 3 — Update AgentControllableComponentBase

 File: src/AgentBlazor.Components/Wrappers/AgentControllableComponentBase.cs

 - Change GetCapability(), GetCurrentState(), ExecuteActionAsync() from abstract → virtual with attribute-discovery defaults
 - Remove [Inject] IComponentActionArgumentResolver (deleted in Phase 8)
 - Remove NormalizeAction() helper (was calling the normalizer)
 - Make ComponentIdForRoute virtual (not abstract) returning ComponentType

 This is backwards-compatible: existing overrides still compile.

 Phase 4 — Rewrite Built-in Wrappers with [AgentAction]

 Replace GetCapability() + ExecuteActionAsync() switch blocks with [AgentAction]-decorated methods. Preserve all execution logic, just change the registration mechanism.

 AgentDataGrid.razor (hardest, ~1170 lines):
 - Add [AgentAction] to: Sort, Filter, ClearFilters, SelectRow, GoToPage, NavigateToRow
 - Keep GetCurrentState() override (grid has rich state: columns, rows, filters)
 - Add [AgentReadable] to CurrentViewRows, FocusedRow, TotalRowCount

 AgentDialog.razor: [AgentAction] on Open, Close, Confirm(RequiresApproval=true)

 AgentForm.razor: [AgentAction] on SetField, Validate, Reset, Submit(RequiresApproval=true)

 AgentTabs.razor: [AgentAction] on SwitchTab

 AgentNavMenu.razor: [AgentAction] on NavigateTo, NavigateExternal(RequiresApproval=true)

 Phase 5 — Circuit Scoping Fix

 Problem: IAgentComponentRegistry is Singleton — all Blazor Server circuits share one registry.

 New files:
 - src/AgentBlazor.Core/Runtime/Components/AgentComponentRegistryHub.cs (Singleton)
 ConcurrentDictionary<string, IAgentComponentRegistry> keyed by sessionId. Methods: Register(sessionId, registry), TryGet(sessionId, out registry), Remove(sessionId).
 - src/AgentBlazor.Core/Runtime/Components/CircuitAgentComponentRegistry.cs (Scoped)
 Wraps InMemoryAgentComponentRegistry. On construction, self-registers with hub using Guid.NewGuid(). Exposes SessionId property. On Dispose(), removes from hub.

 Add SessionId to IAgentComponentRegistry interface.

 DI change in AgentBlazorServiceCollectionExtensions:
 // Remove: services.TryAddSingleton<IAgentComponentRegistry, InMemoryAgentComponentRegistry>();
 services.TryAddSingleton<AgentComponentRegistryHub>();
 services.TryAddScoped<IAgentComponentRegistry, CircuitAgentComponentRegistry>();

 AgentChatSurface.razor: inject IAgentComponentRegistry, use registry.SessionId as the session ID (replaces local Guid.NewGuid()). This links each chat surface to its circuit's registry automatically.

 Phase 6 — New Planner (AgentPlanner.cs)

 Create src/AgentBlazor.Core/Runtime/Planning/AgentPlanner.cs. Delete StructuredActionPlanner.cs.

 Key changes from old planner:
 - Remove IAgentUiToolCatalog dependency
 - Remove repair loop (TryRepairGeneratedUiPlanAsync) entirely
 - Remove AttachFallbackGeneratedUiSummary
 - Merge # AVAILABLE COMPONENTS + # MOUNTED COMPONENTS into single # ACTIVE COMPONENTS section built from the scoped circuit registry
 - Add "message" field to LLM response schema (the natural language reply)

 New LLM response schema:
 {
   "message": "I'll filter to show high-risk suppliers.",
   "actions": [
     { "agentId": "supplier-grid", "action": "filter", "args": { "column": "RiskScore", "operator": "gte", "value": 80 } }
   ],
   "ui": [
     { "type": "summary.card", "title": "Risk Summary", "description": "3 suppliers above threshold." }
   ],
   "needsClarification": false,
   "clarificationQuestion": null
 }

 # ACTIVE COMPONENTS prompt section (built per-request from scoped registry):
 AgentId: supplier-grid (AgentDataGrid)
   Actions:
     - filter(column: string [required], operator: string [required, allowed: eq|neq|...], value: any)
     - sort(column: string [required], direction: string [required, allowed: asc|desc])
     - select_row(rowKey: string [required])
   State:
     rowCount: 42
     currentViewRows: [{"id":"1","risk":85},...]

 # OPTIONAL UI BLOCKS section (inline, not from catalog):
 - summary.card: { type, title, description, actions[] }
 - form.draft: { type, title, fields[{name,label,type,value}] }
 - table.view: { type, title, columns[], rows[] }
 - chart.view: { type, dataSource OR chartType+labels+series }

 The "ui" array is parsed and converted to AgentUiToolCall records by ParsePlanResponse() — DefaultAgentUiToolCatalog.BuildDocument() still converts these to AgentUiDocument for rendering.

 Phase 7 — Simplify AgentRuntime (was DeterministicAgentRuntime)

 Rename DeterministicAgentRuntime.cs → AgentRuntime.cs. Remove:
 - IIntentClassifier constructor param
 - IIntentResolver constructor param
 - IConversationManager constructor param (use IConversationStore directly)
 - IUserPreferenceService constructor param
 - IAgentBlazorEntitlementService constructor param
 - All feature-gate checks

 Add: resolve registry from AgentComponentRegistryHub by request.SessionId.

 Pass the resolved registry through to PlanExecutor.ExecuteAsync() as a parameter (avoid scoping fights — pass instance, don't inject).

 Phase 8 — Delete Dead Weight

 After phases 1–7 build cleanly, delete:

 Intent layer (entire Runtime/Intent/ directory):
 - IntentClassification.cs, IntentRule.cs, KeywordIntentClassifier.cs

 Interfaces for deleted types:
 - IIntentClassifier.cs, IIntentResolver.cs, IUserPreferenceService.cs
 - IConversationManager.cs, IPersistentMemoryProviders.cs, IComponentActionArgumentResolver.cs

 ConversationManager + IntentResolver:
 - Runtime/Agents/ConversationManager.cs
 - Runtime/Agents/IntentResolver.cs

 Feature gating:
 - Runtime/Conversation/FeatureGatedConversationStore.cs
 - Runtime/Preferences/ (entire directory)
 - Options/AgentBlazorPaidFeaturesOptions.cs, Options/IntentClassificationOptions.cs

 Argument normalization (entire pipeline replaced by reflection-based param mapping):
 - ComponentActionArgumentNormalizer.cs
 - ComponentActionArgumentResolver.cs
 - DeterministicEntityResolver.cs

 Misc:
 - Runtime/Internal/InMemoryPageStructureRegistry.cs
 - Runtime/Planning/StructuredActionPlanner.cs (replaced by AgentPlanner.cs)
 - Runtime/Planning/DeterministicAgentRuntime.cs (replaced by AgentRuntime.cs)
 - Components/AgentUiToolIds.cs (string constants — inline in planner now)

 Entire packages (remove .csproj references from solution):
 - src/AgentBlazor.Licensing/ — all 4 files
 - src/AgentBlazor.DefaultAgent/ — all 4 files (logic already lives in AgentBlazorRegistrationOptions)

 Phase 9 — Update Service Registration

 AgentBlazorServiceCollectionExtensions.cs: remove ~15 registrations for deleted types. Key changes:
 - IConversationStore → InMemoryConversationStore directly (remove FeatureGatedConversationStore)
 - IActionPlanner → AgentPlanner (was IStructuredActionPlanner → StructuredActionPlanner)
 - IAgentRuntime → AgentRuntime

 AgentBlazorRegistrationOptions.cs: remove paid-feature methods (EnablePaidPersistentMemory, RequirePaidPersistentProviders, UsePersistentConversationStore, UsePersistentUserPreferenceService).

 AgentBlazorOptions.cs: remove PaidFeatures property.

 Phase 10 — Simplify ComponentActionExecutor

 NoOpComponentActionExecutor.cs has a hard-coded switch on (componentId, actionId) pairs routing to specialized executors (IDataGridActionExecutor, IDialogActionExecutor, etc.). With attribute-based
 dispatch, this entire layer collapses.

 New approach: PlanExecutor looks up component by agentId from the passed-in registry, then calls component.ExecuteActionAsync(action). The component's own ExecuteActionAsync() (which in turn calls
 AgentActionDiscovery.ExecuteActionAsync() for attribute-driven components) does the work.

 Remove: IDataGridActionExecutor, IDialogActionExecutor, IFormActionExecutor, INavigationActionExecutor, ITabsActionExecutor, IChatWidgetActionExecutor, NoOp* implementations of each.

 Phase 11 — Update Demo App

 demo/AgentBlazor.Demo/Program.cs: shrink agent instructions from ~90 lines to ~10:
 options.AgentInstructions = """
     Help users manage supplier risk workflows.
     For charts prefer named data sources:
     - demo.suppliers.risk.by-region
     - demo.suppliers.risk.tier-distribution
     - demo.onboarding.volume.monthly
     - demo.mitigation.volume.monthly
     """;

 Demo pages: Suppliers.razor, Workflow.razor — no major changes needed. Components auto-register via the scoped registry.

 Add one example page showing the custom [AgentAction] pattern for developer reference.

 ---
 Files Summary

 ┌────────┬──────────────────────────────────────────────────────────────────────────────┐
 │ Action │                                     File                                     │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Attributes/AgentActionAttribute.cs                      │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Attributes/AgentReadableAttribute.cs                    │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Attributes/AgentParamAttribute.cs                       │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Runtime/Discovery/AgentActionDiscovery.cs               │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Runtime/Components/AgentComponentRegistryHub.cs         │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Runtime/Components/CircuitAgentComponentRegistry.cs     │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Runtime/Planning/AgentPlanner.cs                        │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ CREATE │ src/AgentBlazor.Core/Runtime/Planning/AgentRuntime.cs                        │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Components/Wrappers/AgentControllableComponentBase.cs        │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Components/Wrappers/AgentDataGrid.razor                      │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Components/Wrappers/AgentDialog.razor                        │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Components/Wrappers/AgentForm.razor                          │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Components/Wrappers/AgentTabs.razor                          │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Components/Wrappers/AgentNavMenu.razor                       │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Core/Services/AgentBlazorServiceCollectionExtensions.cs      │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs                    │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Core/Options/AgentBlazorOptions.cs                           │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ src/AgentBlazor.Core/Runtime/Planning/PlanExecutor.cs                        │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ MODIFY │ demo/AgentBlazor.Demo/Program.cs                                             │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Intent/ (entire dir)                            │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Preferences/ (entire dir)                       │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Agents/ConversationManager.cs                   │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Agents/IntentResolver.cs                        │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Conversation/FeatureGatedConversationStore.cs   │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Components/ComponentActionArgumentNormalizer.cs │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Components/ComponentActionArgumentResolver.cs   │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Components/DeterministicEntityResolver.cs       │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Internal/InMemoryPageStructureRegistry.cs       │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Planning/StructuredActionPlanner.cs             │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Runtime/Planning/DeterministicAgentRuntime.cs           │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Options/AgentBlazorPaidFeaturesOptions.cs               │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Options/IntentClassificationOptions.cs                  │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Core/Components/AgentUiToolIds.cs                            │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.Licensing/ (entire package)                                  │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ src/AgentBlazor.DefaultAgent/ (entire package)                               │
 ├────────┼──────────────────────────────────────────────────────────────────────────────┤
 │ DELETE │ All NoOp*ActionExecutor files + their interfaces                             │
 └────────┴──────────────────────────────────────────────────────────────────────────────┘

 ---
 Verification

 1. dotnet build passes after each phase before moving to next
 2. After Phase 3: existing wrapper tests in tests/AgentBlazor.Components.Tests/ pass unchanged
 3. After Phase 5: unit test — two CircuitAgentComponentRegistry instances do not share state; hub lookup by sessionId returns correct registry
 4. After Phase 6: mock IChatClient returns new-format JSON → AgentPlanner.PlanAsync() returns correct ActionPlan with no second LLM call
 5. After Phase 8: dotnet build with zero missing-type errors
 6. After Phase 11: demo app runs, "show high risk suppliers" → filter action executes on grid, "show risk summary" → generative UI card renders in chat