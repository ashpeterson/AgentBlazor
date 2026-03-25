# AgentBlazor Development Status

Last updated: 2026-03-23

## Current Product Shape

AgentBlazor is now positioned as a Blazor-first agentic UI framework with two primary demo surfaces:

- `/` explains the product and routes users into the current story
- `/demo` now jumps straight into the featured live workflow
- `/demo/components` is now a supporting reference for drop-in agentic components for Blazor apps

The current story is no longer "many unrelated demo routes". It is:

1. learn the platform on the landing page
2. validate the workflow-first story in the featured response-orchestration flow
3. use the component explorer only as a supporting reference when needed

The current visible demo funnel is intentionally narrow:

- `/`
- `/docs`
- `/demo`
- `/demo/workflows/response-orchestration?reset=true`
- `/demo/workflows/release-dossier?reset=true` as a secondary proof
- `/demo/components` as a supporting reference surface

The acquisition story is now intentionally simple too:

- `Free` should look shippable in one sprint
- `Paid` should look like the app gets smarter with use
- `Premium` should read as the team/governance layer

The free-plan onboarding path is now also explicit:

- `/docs` gives the developer-facing setup path
- `samples/AgentBlazor.Starter` is the current golden-path starter
- `AddWorkflow<T>()` is the lowest-ceremony workflow registration path in code

The runtime realignment is now materially underway:

- the external runtime adapter path is the default when a chat client/provider is present
- the legacy planner/runtime bridge has now been removed from the normal product path and deleted from the codebase
- semantic capabilities are now a first-class authoring surface in code
- normalized execution, approval, policy, and context-freshness contracts now exist and are consumed by the adapter path
- the remaining plan-oriented helper models now live under `Runtime/ExecutionPlans` rather than `Runtime/Planning`
- supplier-compliance, file-audit, recipe-release, incident-escalation, response-orchestration, and release-dossier workflow validation now exist as focused integration proof, not only demo wiring

## Shipped and Working

### Core Runtime

- Deterministic runtime pipeline is shipped:
  - request context capture
  - capability and UI tool projection
  - adapter-led execution
  - normalized `ExecutionPlan`
  - policy / approval checks
  - deterministic UI execution
- Runtime policy and validation hardening is in place:
  - mounted live-component validation
  - tier-aware action filtering
  - deterministic blocked-action diagnostics
  - clarification recovery for explicit field edits
- AG-UI hosting is implemented and integrated with the runtime.
- Shared-state infrastructure is shipped:
  - `IAgentSharedStateStore`
  - in-memory default provider
  - optional JSON file persistence
  - snapshot + delta events
- Multi-agent V1/V2 foundations are shipped:
  - route lock
  - explicit handoff commands
  - approval policies
  - transfer policies
  - loop guards
- Embedded inspector is shipped and usable:
  - run timeline
  - planning/validation/execution phases
  - stream filters
  - payload lenses
  - handoff/run correlation

### Blazor UI Surface

- Built-in chat surfaces are shipped:
  - `AgentChatSurface`
  - `AgentChatPanel`
  - `AgentChatWidget`
  - `AgentChatBar`
- The floating widget open path was stabilized:
  - no fresh DOM-style open flash
  - state now transitions in a stable widget shell
- Generative UI rendering is shipped natively in Blazor through:
  - `AgentGenerativeSurface`
  - `AgentUiDocument`
  - generated card/form/table/chart blocks
- Declarative import adapters are now present:
  - `A2UI -> AgentUiDocument`
  - `Open-JSON-UI -> AgentUiDocument`

### Agentic Components

The current built-in component set is:

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
- attribute-based custom components via `AgentControllableComponentBase`

Most high-surface MudBlazor-backed components have now been moved onto native-first implementations:

- `AgentDataGrid -> MudDataGrid`
- `AgentDialog -> MudDialog`
- `AgentForm -> MudForm`
- `AgentNavMenu -> MudNavMenu`
- `AgentTabs -> MudTabs`
- `AgentSelect -> MudSelect`
- `AgentAutocomplete -> MudAutocomplete`
- `AgentDatePicker -> MudDatePicker`
- `AgentDateRangePicker -> MudDateRangePicker`
- `AgentTreeView -> MudTreeView`
- `AgentStepper -> MudStepper`
- `AgentFileUpload -> MudFileUpload`

Baseline compatibility proof is now materially in place, not just planned:

- the components explorer exposes focused live examples for every shipped Mud-backed `Agent*` component
- rendered compatibility coverage exists in `AgentBlazor.Components.Tests`
- browser coverage exercises the public landing, workflow funnel, and focused component routes

Release posture:

- the next package should be treated as a parity-foundation preview for real-project validation
- public proof is strong enough for an initial NuGet prerelease, but not yet for claiming full complex-screen parity across every MudBlazor workflow

The components explorer is now a docs-style surface:

- top navigation bar
- left component catalog with independent scrolling
- centered component detail/live example panel
- right contents rail
- floating `AgentChatWidget` for prompts

### Demo Journey

The current demo app flow is intentionally unified:

- Home explains the platform
- Workflow Hub proves the workflow-first story
- Agentic Components shows the reusable primitives as a reference surface

This replaced the earlier fragmented "try the demo" / separate-feeling routes.

## Pricing and Tier Status

The tier model still exists:

- `Free`
- `Paid`
- `Premium`

But the component action surface has been realigned:

- core component actions are free again
- DataGrid paging/selection/navigation is free
- `AgentForm.submit` is free
- external navigation is free

Current paid differentiation is service-oriented, not component-action-oriented:

- action history
- adaptive suggestions
- proactive insights

Current funnel intent:

- landing page and README should make the free quickstart obvious
- `/demo` should make the orchestration proof obvious
- `Paid` should be introduced as compounding workflow intelligence rather than gated primitive control

Important limitation:

- persistent cross-session user intelligence is not complete yet
- the current paid action history store is still in-memory
- the currently wired suggestion path is not a durable user-profile system yet

## Verification Snapshot

Latest local verification:

- `dotnet build demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj -c Release -nologo /p:UseSharedCompilation=false`
- `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj --filter "SharedStateStoreTests|PromptTracingTests|ComponentMockingTests|ComponentMockingReportTests|ServiceRegistrationTests" -c Release -nologo /p:UseSharedCompilation=false`
- `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj --filter "ResponseOrchestrationWorkflowIntegrationTests|ReleaseDossierWorkflowIntegrationTests|AgUiHostingIntegrationTests|AgentRuntimeIntegrationTests" -c Release -nologo /p:UseSharedCompilation=false`
- `npm --prefix tests/e2e run test:e2e`

Latest browser status:

- full end-to-end Playwright suite passed
- current suite count: `3/3`

Coverage includes:

- landing page journey
- workflow-first launch into the featured orchestration route
- components explorer layout and focused component routes

## Current Gaps

### Product Gaps

- Persistent user-level intelligence is not complete:
  - no durable `IActionHistoryStore` implementation yet
  - paid suggestions are not yet a mature long-term personalization system
- Some component demos still prove isolated control better than full workflow depth, but the workflow showcase now spans multiple blocked and approval-gated scenarios.

### Demo Gaps

- The broader demo now covers supplier, file, recipe, incident, response-orchestration, and release-dossier workflows with shared decision-support patterns, explicit recovery paths on the richer scenarios, and two cross-system orchestration routes, but it is still a curated showcase rather than a complete playground.
- Compatibility proof now exists for every shipped Mud-backed `Agent*` component, but some surfaces are still only proven in one or two shapes:
  - `AgentTreeView` still needs deeper hierarchy and expansion scenarios
  - composed workflow proof should keep expanding beyond the current supplier, file, recipe, incident, response-orchestration, and release-dossier workflow set
  - `AgentDataGrid` still needs stronger public proof around richer server-backed and templated usage
- `AgentFileUpload` should be treated as host-workflow-first:
  - file-name actions are useful for deterministic workflow state and demos
  - real browser file payloads still remain host-owned and should not be implied to be agent-synthesizable
- The components explorer is strong for drop-in primitives, but richer production narratives are still uneven across:
  - `AgentDataGrid`
  - `AgentTreeView`
  - `AgentCommandBar`
  - `AgentFileUpload`

### Platform Gaps

- Declarative interoperability exists, but only as an adapter subset today, not full schema coverage for every external declarative UI feature.
- MCP tool integration exists, but a broader production-grade hosted-app contract still needs more depth if open-ended hosting becomes a larger platform story.

## What Needs Doing Next

1. Keep tightening the workflow-first shell and UX so the orchestration routes and their supporting supplier/file/recipe/incident modules are the default story, not the component explorer.
2. Keep aligning any remaining showcase/detail surfaces around the same normalized execution-plan and approval model now used by chat, approval prompts, inspector, and workflow pages.
3. Continue expanding workflow-first proof from the current orchestration showcase into broader production-style scenarios, especially deeper cross-screen and cross-system compositions.
4. Keep tightening the live demo journey so the orchestration routes, fresh-start links, and route-aware presenter cues feel effortless in a fast product video.
5. Begin the eventual package/module split only after the demo/product proof is strong enough that the new package story can follow the product story.
6. Continue parity depth where needed, but stop treating primitive component-control coverage as the main product measure.

Current note:
- `AgentBlazorOptions.DefaultAgent` is now explicitly obsolete as a legacy compatibility surface; host apps should move toward explicit `AddAgent(...)` registration, and normal runtime resolution no longer synthesizes or prefers a built-in default agent.
- Adapter-backed inspector and trace persistence now record normalized step-oriented execution data as the canonical devtools shape; remaining legacy action/result payloads should only survive where the legacy runtime path still needs them.
- The landing page, workflow hub, and components explorer now reinforce a workflow-first journey, with the component surface positioned as a fallback reference rather than a default destination.
- The workflow hub and workflow routes now use route-specific assistant profiles and semantic-first prompt guidance, so the assistant defaults line up with the workflow-first product story instead of falling back to generic component language.
- `/docs`, `README.md`, and `samples/AgentBlazor.Starter` now present one package-first free onboarding path centered on `AddAgentBlazor(... ConfigureBuilder(... AddWorkflow<T> ...))`, with repo-local source mode treated as maintainer-only validation rather than the public story.
- Prompt tracing and report-style consumers now expose normalized workflow-step views as the primary reporting language, with planner-era action/result lists left as compatibility storage rather than the first-class presentation model.
- Component-mocking and report-generation test helpers now prefer normalized `ExecutionPlan` data and only fall back to legacy action/result payloads when the legacy runtime path returns no plan.
- the legacy planner runtime, `LegacyAgentRuntimeAdapter`, `IAgentRuntime`, `IAgentRuntimeStreaming`, `AgentRuntime`, `AgentPlanner`, and `PlanExecutor` have now been removed from the codebase entirely; remaining plan-oriented helpers are execution-model utilities rather than a hidden runtime path.
- Hosted AG-UI response metadata now reports normalized execution-step counts before falling back to legacy planned-action counts.
- `AgUiHostingIntegrationTests` now run fully adapter-first, including reconnect/stop control coverage through adapter-native test doubles; the hosted AG-UI suite no longer depends on any legacy runtime seam.
- focused response/history tests now treat `ExecutionPlan` as the primary model and use `LegacyPlannedActions` / `LegacyExecutionResults` only when they are explicitly validating compatibility behavior.
- the shared `/demo` shell now presents the experience as a workflow hub and uses route-aware assistant guidance so workflow routes lead with semantic workflow language rather than component-control framing.
- prompt-trace reports and inspector phase labels now use workflow-step terminology, so workflow runs read consistently across reports, chat, workflow pages, and inspector/devtools.
- prompt tracing, component mocking/reporting, shared-state, hosted AG-UI, and runtime integration coverage now all run adapter-first by default.
- Public response/history records now expose normalized execution-plan state directly and label the old planned-action / execution-result lists as legacy compatibility payloads instead of implying they are co-equal primary data.
- The workflow hub now includes an incident-escalation scenario that exercises `AgentTreeView`, `AgentTabs`, `AgentStepper`, `AgentCommandBar`, and `AgentDialog` together behind semantic capabilities, with focused integration proof for blocked, approval-gated, and recovery-driven execution.
- The recipe-release workflow now includes a semantic recovery playbook that clears safe metadata blockers and proves blocked -> recover -> approval-gated draft flow through focused integration coverage.
- The file-audit workflow now includes a semantic recovery playbook that replaces rejected files and proves blocked -> recover -> retry-success flow through focused integration coverage.
- The supplier-compliance workflow now includes a semantic recovery playbook that clears severe supplier blockers and proves blocked -> recover -> approval-gated remediation drafting through focused integration coverage.
- The workflow hub now includes a response-orchestration route that composes supplier remediation, audit evidence, and incident escalation into one approval-gated recovery-aware response packet, giving the showcase its first real cross-system workflow proof.
- The response-orchestration route now deep-links into the live supplier, file-audit, and incident workflow surfaces with shared session state and route-scoped focus, so the showcase is no longer limited to a single composite orchestration shell.
- The response-orchestration route now carries guided return progression too: subsystem pages return with surface/state metadata, and the orchestration shell recommends the next live route instead of treating each handoff as isolated.
- Focused integration proof now covers a full cross-surface orchestration journey: supplier progression, file progression, incident progression, and final approval-gated response-packet completion in one shared-session scenario.
- The response-orchestration shell now processes guided route returns directly and renders a live journey board for supplier, file, incident, and packet phases, so cross-surface progress is visible in the demo without requiring a manual reassessment step after each return.
- The workflow hub now features the response-orchestration route as the strongest current production-style path, with an explicit cross-system progression panel rather than treating all workflow cards as equivalent isolated demos.
- The response-orchestration route can now advance the next guided subsystem stage itself through a semantic orchestration action and visible demo controls, so the shared session can move from supplier -> file -> incident readiness directly inside the orchestration shell before the final approval-gated packet step.
- The response-orchestration shell now keeps a visible orchestration activity trail, and the supplier, file, and incident workflow pages now explain their current orchestration contribution directly in the live surface, so the cross-route demo reads more like one coordinated application workflow than a hub plus disconnected pages.
- The workflow hub now includes a second cross-system production-style route, `release-dossier`, which coordinates recipe release readiness and audit evidence into one approval-gated release dossier with guided live-surface handoffs.
- The recipe release page now participates in orchestration handoff/return flow, so recipe readiness is no longer isolated from the broader multi-surface showcase story.
- The workflow hub now includes an explicit suggested live-demo sequence and promotes the orchestration routes as featured live demos, so `/demo` reads more like a presenter flow than a flat route directory.
- The featured response-orchestration and release-dossier routes now sit at the center of a much narrower workflow-first demo shell, so the product story is easier to grasp and faster to present live.
- The landing page and `/demo` hub have now been refactored into a faster, lower-text demo shell that pushes two orchestration-led live demos first and hides older reference material behind supporting routes.
- `DefaultAgentOptions` is now explicitly hidden and marked legacy in code, so the remaining built-in default-agent surface reads as migration scaffolding rather than a normal first-class host authoring model.
- the public legacy default-agent registration entry points are now removed, and the remaining built-in default-agent surface is migration-only component-catalog scaffolding rather than runtime behavior.
- implicit built-in default-agent registration and implicit default-agent runtime fallback are now removed from the normal service path; explicit registered agents and route/context targeting are the only non-legacy resolution modes.
- the normal `IAgentRuntimeAdapter` registration now returns a no-provider response when no chat client/runtime adapter is configured, instead of silently falling back to a hidden planner runtime.
- the normal `AddAgentBlazorServices()` path is now adapter-only; there is no `IAgentRuntime` registration path left in the product container.
- `PromptTracingTests`, `ComponentMockingTests`, `ComponentMockingReportTests`, `SharedStateStoreTests`, `AgentRuntimeIntegrationTests`, and `AgUiHostingIntegrationTests` now run adapter-first, so the old planner-runtime architecture is no longer a test blocker.
- the adapter now has an explicit opt-in legacy component-tool alias path for compatibility experiments, but it is disabled by default so normal tool projection stays normalized and single-shaped.
- the public demo surface is now intentionally minimal: the old Dojo, parity pages, redirects, and playbook route are gone from the visible product story.

## Reference Docs

- Architecture: `docs/architecture.md`
- Expansion roadmap: `docs/component-expansion-plan.md`
- MudBlazor compatibility roadmap: `docs/mudblazor-compatibility-roadmap.md`
- NuGet prerelease checklist: `docs/nuget-prerelease-checklist.md`
- GitHub Packages private preview: `docs/github-packages-private-preview.md`
- Tier model: `docs/pricing-tiers.md`
- Runtime realignment plan: `docs/runtime-realignment-plan.md`
