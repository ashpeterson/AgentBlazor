# AgentBlazor Runtime Realignment Plan

Last updated: 2026-03-19

## Goal

Reposition AgentBlazor as the Blazor application layer that sits on top of:

- an external agent runtime/orchestration framework
- MudBlazor as the native UI primitive layer

AgentBlazor should not compete as a general-purpose agent framework.
It should own:

- live Blazor UI context
- app capability binding
- deterministic UI execution
- approval UX
- inspector UX
- proactive suggestion UX

## Alignment Against The Master Plan

The expanded master plan from 2026-03-18 is directionally aligned with this document.
The biggest change is not strategy; it is precision around what AgentBlazor must own so it does not collapse into a thin adapter.

### Already Aligned

- AgentBlazor is being repositioned as the Blazor-native app layer rather than the primary agent runtime.
- live UI context remains the moat.
- Mud-backed wrappers remain deterministic execution adapters rather than the product headline.
- chat, approval, inspector, and explanation UX remain first-class framework concerns.
- the runtime adapter seam is the right migration path.

### Needs Stronger Definition

- the semantic capability model needs to become a first-class app authoring surface instead of a doc-only concept.
- execution semantics need to be explicit: `ExecutionPlan`, `ExecutionStep`, `ExecutionContext`, and execution event contracts.
- approval and policy must become a real contract set rather than planner-era behavior carried forward indirectly.
- context freshness and versioning need to become explicit so plans can be invalidated safely.
- the docs need to keep defending against "thin adapter syndrome" by naming the systems AgentBlazor must own:
  - live UI context
  - deterministic execution
  - approval/policy
  - inspector/debugging UX
  - capability authoring

### Updated Reading Of The Plan

The master plan is now the more complete statement of intent.
This document should stay as the working implementation map for that strategy:

- what is already built
- what is partially built
- what the next code slices are

## Product Definition

The product is no longer:

- "natural-language control for components"

The product becomes:

- "make a Blazor app agent-capable with live UI context, semantic app actions, approvals, and native MudBlazor execution"

The critical rule is:

- basic UI primitives stay available
- semantic workflows become the primary value

## What AgentBlazor Should Keep Owning

### 1. UI Context and Session Binding

AgentBlazor already has the right foundations here and should keep them:

- circuit/session-scoped component registry
- mounted component discovery
- route-aware context
- readable component state
- live Blazor execution against the current page

Current examples:

- `IAgentComponentRegistry`
- `[AgentAction]`
- `[AgentReadable]`
- `AgentActionDiscovery`

### 2. MudBlazor Execution Adapters

AgentBlazor should keep the MudBlazor-facing execution layer:

- `AgentDataGrid`
- `AgentDialog`
- `AgentForm`
- `AgentTabs`
- `AgentSelect`
- `AgentAutocomplete`
- `AgentDatePicker`
- `AgentDateRangePicker`
- `AgentTreeView`
- `AgentStepper`
- `AgentFileUpload`

These are execution adapters and capability surfaces, not the product headline.

### 3. Blazor-Native Agent UX

AgentBlazor should keep all host-app UX:

- `AgentChatWidget`
- `AgentChatSurface`
- `AgentChatPanel`
- `AgentChatBar`
- inspector/devtools
- approval prompts
- plan previews
- inline explanations
- proactive suggestion surfaces

### 4. Capability Authoring Model

AgentBlazor should keep the host-app authoring story:

- attributes for component actions/state
- app-defined semantic capability registration
- approval metadata
- argument schemas
- availability checks
- action result shaping

This is where reusable value lives across apps.

## What AgentBlazor Should Stop Owning

AgentBlazor should stop acting like the system-of-record runtime for:

- multi-agent orchestration
- graph workflow execution
- long-running run lifecycle
- generic tool runtime
- general provider abstraction strategy
- protocol-first AG-UI runtime hosting
- framework-level memory/middleware orchestration

Those responsibilities should move behind the external runtime integration layer.

## New Package Direction

Do not add external-vendor names to package names.
Keep package names vendor-neutral and product-owned.

Target module shape:

### `AgentBlazor.App`

Owns host-app capability modeling:

- semantic action registration
- capability discovery
- approval metadata
- action schemas
- action results
- read models and context descriptors

This becomes the stable app integration package.

### `AgentBlazor.UiContext`

Owns live Blazor context:

- component registry
- route/session context
- mounted component state capture
- page-scoped execution targeting
- navigation intent carry-over

This is the bridge between the browser circuit and the agent runtime.

### `AgentBlazor.Mud`

Owns MudBlazor-specific execution adapters:

- `AgentDataGrid`
- `AgentForm`
- `AgentDialog`
- other Mud-backed `Agent*` wrappers

This package should stay aggressively compatible with current MudBlazor.

### `AgentBlazor.Host`

Owns ASP.NET/Blazor app startup:

- service registration
- endpoint mapping
- runtime adapter registration
- auth/session hooks
- inspector and dev UI wiring

This replaces the idea that Hosting also owns the full runtime.

### `AgentBlazor.Chat`

Owns user-facing conversation and agent UX:

- widget
- panel
- bar
- approvals
- plan preview
- explanation surfaces
- proactive suggestion entry points

This should consume a runtime adapter rather than own planning itself.

### `AgentBlazor.RuntimeAdapter`

Owns the bridge into the external runtime:

- agent creation
- tool/capability projection
- run execution
- streaming conversion
- thread/session correlation
- middleware wiring
- telemetry bridging

This is the key integration package.
It should be neutral in name even though it wraps external open-source packages.

### `AgentBlazor.Insights`

Owns proactive behavior and prediction:

- suggestion engines
- action history
- pattern learning
- read-only recommendations
- later automation proposals

This is where "predict what the user wants" should live.

## Mapping from Current Packages

### Keep, but narrow responsibility

- `AgentBlazor.Components`
  - split into `AgentBlazor.Chat` and `AgentBlazor.Mud`
- `AgentBlazor.Hosting`
  - narrow into `AgentBlazor.Host`
- `AgentBlazor.Licensing`
  - keep if the commercial model remains

### Shrink or replace

- `AgentBlazor.Core`
  - keep only app abstractions, UI context, execution contracts, approvals, and policy contracts
  - remove ownership of bespoke planning/orchestration over time

### Deprecate as primary architecture

- `AgentBlazor.DefaultAgent`
  - deprecate the current built-in "framework-owned default runtime"
- `AgentBlazor.ProviderAdapters`
  - de-emphasize as a core package
  - provider integration should mostly happen through the external runtime unless a host app explicitly needs direct chat plumbing

## Cleanup Plan

This repo does not appear to have tracked `bin/`, `obj/`, or `.tmp/` build artifacts in git.
The cleanup problem is architectural overlap and legacy runtime ownership, not generated-file pollution.

### Keep as First-Class

- `Runtime/Interfaces/IAgentComponentRegistry`
- `Runtime/Interfaces/IAgentNavigationIntentService`
- `Runtime/Discovery/AgentActionDiscovery`
- `Runtime/Components/*`
- Mud-backed `Agent*` wrappers
- chat surfaces and inspector UX
- approval/event contracts that are UI-facing rather than planner-facing

These are still aligned with the new product shape.

### Refactor Behind the Runtime Adapter

- `Runtime/Interfaces/IAgentRuntime`
- `Runtime/Interfaces/IAgentRuntimeStreaming`
- `Runtime/Interfaces/IAgentRuntimeEventSubscriber`
- `Runtime/Middleware/*`
- `Runtime/Conversation/*`
- `Runtime/State/*`
- `Runtime/Tools/*`
- `Runtime/Routing/*`
- `Hosting/DeterministicAgUiHostedAgent`
- provider registration entry points in `AgentBlazorRegistrationOptions`

These should not disappear immediately.
They should become adapter-facing seams so existing chat surfaces and host apps continue to work while the runtime is swapped underneath.

### Mark as Legacy Immediately

- `Runtime/Planning/AgentPlanner`
- `Runtime/Planning/AgentRuntime`
- `Runtime/Planning/PlanExecutor`
- `Runtime/Planning/PlanValidator`
- `Runtime/Planning/IActionPlanner`
- `Runtime/Planning/IPlanExecutor`
- `Runtime/Planning/IPlanValidator`
- `AgentBlazor.DefaultAgent`

These are the clearest expression of the old architecture.
They should be marked legacy first, then reduced behind compatibility shims, then removed once the adapter path is complete.

### De-Emphasize Hard

- direct provider-first product positioning
- component-primitive demo narratives
- docs that present AgentBlazor as the full agent runtime

### Remove Once Replacement Lands

- direct registrations of `AgentRuntime` as the default runtime implementation
- `DeterministicAgUiHostedAgent` as the primary AG-UI bridge
- planner-specific tests that are only proving bespoke orchestration behavior rather than app capability projection or UI execution
- `AgentBlazor.DefaultAgent` package and its descriptor plumbing

## Cleanup Order

Do the cleanup in this order to avoid breaking the package surface all at once.

### 1. Freeze Legacy Runtime Surface

- stop adding features to `Runtime/Planning/*`
- mark planner/runtime services as legacy in docs and comments
- stop expanding demo coverage for planner-specific behavior

### 2. Introduce Replacement Seams

- add a runtime adapter abstraction
- make chat surfaces depend on the adapter
- make hosting depend on the adapter
- keep `IAgentRuntime` only as a temporary compatibility surface if needed

### 3. Move Product Features Off the Old Runtime

- move approvals, plan preview, and chat UX to adapter-facing contracts
- move semantic capability projection to the new adapter path
- move AG-UI bridging to the adapter path

### 4. Delete Redundant Runtime Pieces

- remove `AgentBlazor.DefaultAgent`
- remove bespoke planner registrations from default service wiring
- remove planner-specific abstractions once no public or internal consumer depends on them

### 5. Simplify Package Story

- reduce `ProviderAdapters` to optional compatibility integrations
- narrow `Hosting` to app startup and endpoint composition
- narrow `Core` to context, capability, execution, approval, and policy contracts

## New Execution Model

The new runtime stack should be:

1. user prompt or proactive suggestion trigger
2. host app context snapshot captured from `AgentBlazor.UiContext`
3. semantic capabilities and UI actions projected as tools
4. external runtime plans and orchestrates
5. AgentBlazor executes approved app actions and UI actions
6. AgentBlazor updates live UI state and emits explanation/inspector events

This means:

- external runtime owns the agent brain
- AgentBlazor owns app context and app execution

The master plan sharpens this further:

- AgentBlazor must explicitly own normalized execution semantics rather than leaving them implicit inside planner-era flows.
- execution needs stable framework contracts for:
  - `ExecutionPlan`
  - `ExecutionStep`
  - `ExecutionContext`
  - execution event streams
- execution must remain deterministic, inspectable, and approval-aware even when reasoning is delegated to the external runtime.

## Capability Model

AgentBlazor needs a first-class semantic capability layer.

Example direction:

```csharp
[AgentCapability]
public sealed class SupplierCapabilities
{
    [AgentAction("Show suppliers likely to fail compliance review", RequiresApproval = false)]
    public Task<CapabilityResult> ShowAtRiskSuppliersAsync(int days = 30) { ... }

    [AgentAction("Prepare remediation tasks for selected suppliers", RequiresApproval = true)]
    public Task<CapabilityResult> PrepareRemediationAsync(Guid[] supplierIds) { ... }
}
```

Rules:

- semantic app actions come first
- raw UI actions are fallback primitives
- read-only actions should be easy to expose
- mutating actions should default toward approval

The master plan also raises the quality bar here:

- capability metadata must become richer than `RequiresApproval` alone.
- capability discovery must support:
  - stable identifiers
  - category
  - route and state relevance
  - availability checks
  - result shaping
  - policy tags
- capability results should become structured rather than plain text only.
- capability composition should exist, but should not turn into a new hidden internal planner.

## UX Model

The chat experience should move from "prompt to click for me" toward:

- plan preview
- explanation
- approval
- suggestion
- workflow compression

Recommended behavior:

- simple UI actions stay silent and fast when directly clicked by the user
- complex prompts produce an explicit plan summary
- mutating workflows require confirmation
- read-only insights can run immediately

## Proactive Layer

Prediction should not mean "autonomous clicking".

The first viable proactive layer is:

- observe repeated user behavior
- observe page state anomalies
- suggest next actions
- let the user accept, reject, or inspect the plan

Examples:

- "You usually export after filtering this report."
- "These suppliers likely need review because certification expiry and audit status both changed."
- "Do you want me to prepare the remediation batch?"

## What to Deprecate First

These should stop being primary demo stories:

- "filter the grid by X"
- "sort by Y"
- "go to page 2"
- "click the dialog confirm button"

Do not remove the capabilities.
Just stop presenting them as the product value.

## First Real Workflow

The first serious workflow should prove all three layers:

- semantic app capability
- cross-component UI execution
- explanation/approval UX

Recommended shape:

1. identify at-risk suppliers based on multiple signals
2. explain why they were selected
3. highlight and filter the relevant grid
4. open the remediation dialog
5. draft the next actions
6. require approval before mutation/submission

This is a better proof than any single-component prompt demo.

## Migration Plan

## Current Status

### Completed

- `IAgentRuntimeAdapter` exists and chat/hosting surfaces now depend on it rather than directly on the legacy runtime.
- `ChatClientRuntimeAdapter` exists as a real external-runtime-backed bridge over `ChatClientAgent` and `IChatClient`.
- the external runtime adapter is now the default registration path whenever an `IChatClient` is present.
- mounted UI actions, generated UI tools, service tools, and MCP tools can already be projected into the adapter path as runtime tools.
- AG-UI hosting now runs through the adapter path.
- shared runtime shaping/persistence logic has been extracted from the legacy runtime into helper modules such as:
  - `RuntimeTurnPreflight`
  - `RuntimeCapabilityPolicy`
  - `RuntimeEarlyExitResponses`
  - `RuntimePlanResponses`
  - `RuntimePlanApprovals`
  - `RuntimePlanExecution`
  - `RuntimeGeneratedUi`
  - `RuntimePersistenceRecords`
  - `RuntimeConversationHistory`
  - `RuntimeTurnResponses`
- `AgentRuntime` has been materially reduced and now uses scoped phase contexts instead of one large monolithic turn method.
- persisted conversation turns now carry `ExecutionPlan`, and both chat surfaces hydrate history from the normalized execution model before falling back to planner-era action/result lists.
- persisted conversation turns now canonicalize plan-backed adapter responses, dropping redundant planner-era action/result payloads when a normalized `ExecutionPlan` exists.
- approval surfaces now render policy/risk intent in the chat UI rather than only raw `component.action` identifiers.
- adapter-backed turns now stop surfacing legacy `PlannedActions` once a normalized `ExecutionPlan` is available.
- semantic capability turns now feed the action-history/suggestion pipeline directly from normalized execution steps rather than only from legacy component execution results.
- explicit registered agents now win implicit resolution over the built-in default agent unless legacy fallback is enabled explicitly.
- the normal `AddAgentBlazorServices()` path no longer auto-registers the built-in default agent; legacy fallback now has to be opted into explicitly, while the older hosting registration path still enables it for compatibility.
- legacy runtime-oriented test suites now opt into default-agent fallback explicitly instead of silently depending on it through the plain service-registration path.
- the unified `AddAgentBlazor()` path no longer silently re-enables the built-in default agent; legacy hosting compatibility now has to be requested explicitly or triggered by the obsolete default-agent fields themselves.
- chat surfaces now share a normalized execution-step narrative formatter so blocked, approval-required, failed, warning, next-step, and output details stay consistent.
- approval prompts in the chat surface now reuse normalized execution-plan step labels and result narrative instead of rendering only raw pending-approval identifiers.
- inspector run records now carry normalized `ExecutionPlan` data, and the inspector now reuses the same plan-summary, step-label, and approval-summary narrative model as chat instead of relying only on raw event counters.
- adapter-backed inspector runs and trace reporting now treat normalized execution steps as the canonical devtools shape, only falling back to legacy action/result payloads when no `ExecutionPlan` exists.
- `AgentTurnResponse` and persisted `ConversationTurn` records now expose normalized-plan state explicitly and treat planner-era action/result lists as legacy compatibility payloads rather than the primary public execution model.
- component-mocking/reporting test helpers now prefer normalized `ExecutionPlan` data and only fall back to legacy action/result payloads when the legacy runtime path returns no plan.
- `AgentRuntimeIntegrationTests` now treat normalized execution-plan data as primary and explicitly opt into legacy default-agent fallback, making the integration suite consistent with the adapter-first/default-agent migration strategy.
- hosted AG-UI response metadata now uses normalized execution-step counts before falling back to legacy planned-action counts.
- the workflow showcase pages now share a common decision-support surface for phase, approval boundary, next-step, outcome, blockers, warnings, and recommended follow-up actions instead of each page inventing its own status layout.
- the shared demo layout now brands the `/demo` experience as a workflow hub and adjusts assistant guidance by route, so workflow routes no longer read like a dojo/component-control sandbox by default.
- the component explorer overview and sample navigation now reinforce the workflow hub as the primary destination, with dojo/components positioned as supporting references rather than the default entry point.
- the workflow hub and each workflow route now use route-specific assistant defaults and semantic-first prompt guidance, so workflow conversations lead with semantic capabilities and approval-aware next steps before lower-level component advice.
- prompt tracing and report-style consumers now project normalized workflow-step views first, so reporting/devtools language no longer centers planner-era planned-action/result lists even though compatibility storage still exists underneath.
- prompt-trace reports and inspector phase labels now use workflow-step language (`Workflow Planning`, `Approval and Validation`, `Workflow Execution`) so devtools no longer present normalized semantic runs through planner-era terminology.

### In Progress

- `Core` is being narrowed, but the legacy planner/runtime stack still exists and still ships as the default compatibility path.
- the product story has been repositioned in docs, and the semantic capability authoring story is now real in code, but it still needs better host-authoring ergonomics and richer workflow examples.
- the runtime cleanup has gone well, and normalized execution, policy, approval, and context-freshness contracts are now present, but they are not yet the only response shape across the whole framework.
- workflow-first demos are now proven through focused integration tests, and the page-level workflow narrative is stronger; the showcase has started moving beyond isolated route demos into cross-system composition, but it still needs broader production-style depth before it fully replaces the old primitive-first story.
- the showcase now includes multiple semantic workflow pages, including supplier compliance, file-audit bundling, recipe-release readiness, incident escalation, and response orchestration, with blocked, approval-gated, and recovery-driven branches covered in focused integration tests.
- the demo shell is now being reshaped into a workflow-first hub so those scenarios become the default experience, while dojo/component pages move into a supporting-reference role.

### Not Done Yet

- the normalized execution contracts are now consumed by the adapter, AG-UI surface, and chat UI, but older planner-era compatibility shapes still exist beside them.
- policy, approval, and context versioning are now explicit contracts, but they are not yet enforced or surfaced uniformly across all runtime paths and UI flows.
- the package/module split in this document has not happened yet.
- the old `AgentBlazor.DefaultAgent` package has been removed from the active solution, and default-agent compatibility now lives only in the remaining Core/Hosting option surface.
- the remaining `AgentBlazorOptions.DefaultAgent` surface is now explicitly marked as legacy compatibility rather than looking like a normal first-class configuration path.
- built-in default-agent behavior is no longer auto-registered on the normal service path, and the unified hosting path now requires explicit legacy opt-in unless obsolete default-agent fields are used.
- the workflow-first demo/application layer from this plan is not built yet.
- workflow-first proof exists now, but it still needs broader production-style depth and richer end-to-end failure/approval storytelling before it can fully replace the old demo mix.

### Phase 1: Reposition

- update product/docs language away from "general agent framework"
- define AgentBlazor as app integration and UI execution layer
- define semantic capability model as the primary authoring story

Status:
- product/docs positioning is largely done
- semantic capability model has started in code, but is not yet the full authoring story

### Phase 2: Adapter

- introduce `AgentBlazor.RuntimeAdapter`
- bind external runtime runs/threads/streaming into current chat surfaces
- project app capabilities and mounted UI actions as tools

Status:
- adapter seam is done
- external-runtime-backed adapter is done
- external-runtime-backed adapter is now the default path when a chat client/provider is registered
- chat/hosting integration is done
- mounted UI actions and service/MCP tools are projected today
- semantic capability contracts exist
- semantic capabilities are now projected through the adapter path
- semantic capability, UI action, and service-tool turns now create normalized execution steps directly in the adapter path
- the remaining adapter gap is reducing how much planner-era compatibility data still rides beside the normalized execution model
- trace/devtools are now largely aligned with normalized execution data on the adapter path; the main remaining gap is trimming the last compatibility payloads from public response/history surfaces and updating legacy tracing coverage to match the adapter-first default.
- trace/devtools are now largely aligned with normalized execution data on the adapter path, and public response/history types now make the legacy compatibility payloads explicit; the next gap is reducing the remaining integration and compatibility test surfaces that still model execution around action/result lists.
- `AgUiHostingIntegrationTests` now explicitly opt into legacy default-agent fallback on the legacy planner path, which restores hosted AG-UI tool/state event assertions without weakening the newer adapter-first default.
- focused core and workflow integration tests no longer assert raw `PlannedActions` / `ExecutionResults` as if they were first-class normalized data; they now read `ExecutionPlan` first and use `Legacy*` payload accessors only when validating compatibility behavior.

### Phase 3: Narrow Core

- move bespoke planner/orchestration logic behind compatibility shims
- keep only what is needed for app context, execution, approvals, and UI state
- mark older runtime-first APIs as legacy

Status:
- underway
- legacy runtime is now isolated behind `LegacyAgentRuntimeAdapter`
- hosts/tests can now opt into that path explicitly through `UseLegacyRuntimeAdapter()`
- `AgentRuntime` has been significantly reduced
- adapter-backed responses now prefer normalized execution plans over legacy planned-action payloads
- legacy default-agent fallback is now an explicit compatibility switch instead of silently outranking explicit agents
- the built-in default agent is no longer auto-registered on the standard service path; it is now only enabled by the older hosting registration path or an explicit legacy-fallback opt-in
- the remaining default-agent option surface is now explicitly obsolete to push host apps toward `AddAgent(...)` registration instead of silent fallback-era configuration
- semantic capability workflows now cover successful, blocked, and approval-gated paths in focused integration tests across multiple demo workflows
- old runtime-first pieces still exist, but they are no longer the primary path when a chat client/provider is present

### Phase 3.5: Promote App-Layer Contracts

- introduce the semantic capability registry and authoring contracts
- introduce normalized execution contracts
- introduce explicit approval/policy contracts
- introduce context freshness/versioning contracts

Status:
- underway
- semantic capability contracts and registry now exist
- normalized execution, policy/approval, and context freshness contracts now exist
- chat UI, AG-UI hosting, and the external adapter now consume those contracts
- persisted conversation history now carries normalized execution plans
- adapter-backed traces and action history are being shifted onto normalized execution-plan semantics rather than planner-era payloads
- the main remaining gap is removing more planner-era compatibility dependence now that the adapter path is default

### Phase 4: New Demos

- replace primitive demos with workflow demos
- keep primitive pages only as reference and regression proof
- build one production-style app showcase with a strong semantic workflow

Status:
- started
- `AgentBlazor.Demo` now includes supplier compliance, file-audit, recipe-release, incident-escalation, and response-orchestration workflow showcases that are driven by semantic capabilities rather than primitive component commands
- focused integration tests now prove all five workflow paths through real prompts, scoped workflow state, and approval-gated semantic capability replay
- the workflow pages now expose phase, approval-boundary, and next-step state directly
- the workflow pages now share one normalized decision-support shell instead of separate bespoke state/outcome cards
- chat surfaces now render richer blocked/failure/result narratives from shared execution-step formatting
- the demo landing page and nav now foreground workflow showcases instead of the component explorer
- the incident-escalation workflow broadens the proof into tree/tab/stepper/command/dialog coordination and now includes a recovery path for blocked review-board handoffs rather than only a happy path
- the recipe-release workflow now includes a semantic recovery playbook so the showcase proves blocked -> recover -> approval-gated draft flow instead of stopping at blocker explanation
- the file-audit workflow now includes a semantic recovery playbook so the showcase proves blocked -> recover -> retry-success flow instead of stopping at remote handoff failure explanation
- the supplier-compliance workflow now includes a semantic recovery playbook so the showcase proves blocked -> recover -> approval-gated remediation drafting instead of staying the shallowest workflow path
- the response-orchestration workflow now composes supplier remediation, audit evidence, and incident escalation into one approval-gated recovery-aware response packet, giving the demo its first broader cross-system showcase route
- the response-orchestration workflow now hands users off into the live supplier, file-audit, and incident workflow routes with shared session state and route-scoped focus, so the cross-system showcase is beginning to span real surfaces instead of staying inside one composite page
- the response-orchestration workflow now carries guided return flow across those live subsystem routes, so the orchestration shell can recommend the next surface after a supplier/file/incident handoff instead of treating each route visit as a disconnected branch
- focused integration proof now covers one full cross-surface orchestration path through supplier, file-audit, and incident workflows before the final approval-gated response-packet completion, so the production-style showcase is starting to prove multi-route completion rather than only navigation and summaries
- the response-orchestration shell now consumes those guided returns directly and renders a live subsystem journey board, so the broader production-style showcase reflects cross-route progress in the demo UI itself instead of forcing a manual reassessment after every return
- the response-orchestration shell can now advance the next guided subsystem stage itself through a semantic orchestration action, so the production-style showcase is starting to prove shared-state coordination directly inside the orchestration surface rather than only navigation and summaries
- the live supplier, file-audit, and incident workflow pages now explain their orchestration contribution directly when opened from the cross-system route, and the orchestration shell now keeps a visible activity trail, so the broader production-style showcase reads more like one coordinated app workflow instead of a workflow hub plus isolated pages
- the showcase now includes a second broader orchestration route, `release-dossier`, which coordinates recipe release readiness and audit evidence into one approval-gated release dossier and brings the recipe-release workflow into the same live handoff/return pattern as the larger cross-system demos
- the remaining gap is moving from the current five-route workflow hub toward an even broader production-style showcase

### Phase 5: Proactive Insight

- add durable action history
- add read-only proactive recommendations
- add user-accepted workflow suggestions

Status:
- partially present as infrastructure only
- not yet shaped into the workflow-first product story

## Immediate Next Implementation Steps

1. Keep broadening workflow-first proof without sliding back into primitive-control demos, especially by moving from the now broader orchestration showcase toward more realistic cross-screen and cross-system scenarios and richer orchestration-state progression across live surfaces.
2. Continue narrowing the remaining legacy default-agent compatibility option surface now that the standalone package is gone and fallback is explicit.
3. Push the same normalized execution/trust model further into any remaining showcase/detail surfaces so chat, approval prompts, workflow pages, and diagnostics keep converging on one product surface.
4. Keep trimming the remaining planner-era compatibility payloads from trace/devtools and response models until normalized execution data is the canonical shape everywhere on the adapter path.
5. Carry the same explicit compatibility story into the eventual package/module split so host migrations stay predictable.

## Success Criteria

The redesign is working when:

- AgentBlazor no longer needs to own general orchestration to deliver value
- apps expose semantic capabilities with little boilerplate
- execution plans and steps are explicit, inspectable, and stable enough to support replay/debugging and approval UX
- MudBlazor execution remains deterministic and current-version compatible
- the best demos show workflow compression, not chat-driven clicking
- proactive suggestions feel useful instead of intrusive
