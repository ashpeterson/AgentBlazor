# AgentBlazor Runtime Realignment Plan

Last updated: 2026-03-18

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
- approval surfaces now render policy/risk intent in the chat UI rather than only raw `component.action` identifiers.
- adapter-backed turns now stop surfacing legacy `PlannedActions` once a normalized `ExecutionPlan` is available.

### In Progress

- `Core` is being narrowed, but the legacy planner/runtime stack still exists and still ships as the default compatibility path.
- the product story has been repositioned in docs, and the semantic capability authoring story is now real in code, but it still needs better host-authoring ergonomics and richer workflow examples.
- the runtime cleanup has gone well, and normalized execution, policy, approval, and context-freshness contracts are now present, but they are not yet the only response shape across the whole framework.
- the first workflow-first demo is now proven through focused integration tests, and the page-level workflow narrative is stronger, but it still needs broader showcase depth before it fully replaces the old primitive-first story.

### Not Done Yet

- the normalized execution contracts are now consumed by the adapter, AG-UI surface, and chat UI, but older planner-era compatibility shapes still exist beside them.
- policy, approval, and context versioning are now explicit contracts, but they are not yet enforced or surfaced uniformly across all runtime paths and UI flows.
- the package/module split in this document has not happened yet.
- `AgentBlazor.DefaultAgent` and host-level default-agent registration options are now explicitly marked as legacy in code, but they have not been removed or isolated out of the current package shape.
- the workflow-first demo/application layer from this plan is not built yet.

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
- the main remaining gap is removing more planner-era compatibility dependence now that the adapter path is default

### Phase 4: New Demos

- replace primitive demos with workflow demos
- keep primitive pages only as reference and regression proof
- build one production-style app showcase with a strong semantic workflow

Status:
- started
- `AgentBlazor.Demo` now includes a supplier compliance workflow showcase that is driven by semantic capabilities rather than primitive component commands
- focused integration tests now prove the supplier workflow through real prompts, scoped workflow state, and approval-gated semantic capability replay
- the supplier workflow page now exposes phase, approval-boundary, and next-step state directly
- the remaining gap is broadening the showcase beyond one workflow page and deepening richer result/failure narratives

### Phase 5: Proactive Insight

- add durable action history
- add read-only proactive recommendations
- add user-accepted workflow suggestions

Status:
- partially present as infrastructure only
- not yet shaped into the workflow-first product story

## Immediate Next Implementation Steps

1. Keep removing the remaining planner-era compatibility payloads and response shims from adapter-backed flows where the normalized execution model already covers them.
2. Continue isolating or removing `AgentBlazor.DefaultAgent` from package shape now that legacy configuration entry points are explicitly marked.
3. Expand the workflow-first showcase beyond the supplier page without sliding back into primitive-control demos.
4. Deepen the execution/result narrative in the workflow UX, especially richer structured outputs and failure/blocked states.
5. Carry the same explicit compatibility story into the eventual package/module split so host migrations stay predictable.

## Success Criteria

The redesign is working when:

- AgentBlazor no longer needs to own general orchestration to deliver value
- apps expose semantic capabilities with little boilerplate
- execution plans and steps are explicit, inspectable, and stable enough to support replay/debugging and approval UX
- MudBlazor execution remains deterministic and current-version compatible
- the best demos show workflow compression, not chat-driven clicking
- proactive suggestions feel useful instead of intrusive
