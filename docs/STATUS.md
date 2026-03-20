# AgentBlazor Development Status

Last updated: 2026-03-19

## Current Product Shape

AgentBlazor is now positioned as a Blazor-first agentic UI framework with two primary demo surfaces:

- `/` explains the product and routes users into the current story
- `/demo` is the workflow hub and demonstrates the workflow-first product story
- `/demo/dojo` is now a supporting reference for the three core generative UI pillars
- `/demo/components` is now a supporting reference for drop-in agentic components for Blazor apps

The current story is no longer "many unrelated demo routes". It is:

1. learn the platform on the landing page
2. validate the workflow-first story in the workflow hub
3. use the dojo and component explorer as supporting references

The runtime realignment is now materially underway:

- the external runtime adapter path is the default when a chat client/provider is present
- semantic capabilities are now a first-class authoring surface in code
- normalized execution, approval, policy, and context-freshness contracts now exist and are consumed by the adapter path
- supplier-compliance, file-audit, recipe-release, incident-escalation, and response-orchestration workflow validation now exist as focused integration proof, not only demo wiring

## Shipped and Working

### Core Runtime

- Deterministic runtime pipeline is shipped:
  - `Plan -> Validate -> Execute`
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

- side-by-side parity pages exist for:
  - `MudForm -> AgentForm`
  - `MudDataGrid -> AgentDataGrid`
  - `MudDialog -> AgentDialog`
  - `MudSelect/MudAutocomplete -> AgentSelect/AgentAutocomplete`
  - `MudFileUpload -> AgentFileUpload`
  - `MudDatePicker/MudDateRangePicker -> AgentDatePicker/AgentDateRangePicker`
  - `MudTabs/MudStepper -> AgentTabs/AgentStepper`
  - `MudNavMenu/MudTreeView -> AgentNavMenu/AgentTreeView`
  - a composed multi-control workflow screen
- rendered parity coverage now exists in `AgentBlazor.Components.Tests`
- browser coverage now exercises the proof routes in the Playwright suite

Release posture:

- the next package should be treated as a parity-foundation preview for real-project validation
- public proof is strong enough for an initial NuGet prerelease, but not yet for claiming full complex-screen parity across every MudBlazor workflow

The components explorer is now a docs-style surface:

- top navigation bar
- left component catalog with independent scrolling
- centered component detail/live example panel
- right contents rail
- floating `AgentChatWidget` for prompts

### Dojo

The Dojo is now a focused three-pillar workspace instead of the older mixed demo shell.

Current pillars:

1. Controlled Generative UI
2. Declarative Generative UI
3. Open-ended Generative UI

Current behavior:

- `Preview`, `Code`, and `Docs` modes are route-local and trustworthy
- the controlled pillar uses a narrow, host-owned incident workflow
- the declarative pillar demonstrates:
  - native `AgentUiDocument`
  - imported `A2UI`
  - imported `Open-JSON-UI`
- the open-ended pillar demonstrates a hosted, sandboxed app surface
- the embedded Dojo chat is route-locked and prompt-tested across pillar switches

### Demo Journey

The current demo app flow is intentionally unified:

- Home explains the platform
- Workflow Hub proves the workflow-first story
- Dojo explains the generative UI patterns
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

Important limitation:

- persistent cross-session user intelligence is not complete yet
- the current paid action history store is still in-memory
- the currently wired suggestion path is not a durable user-profile system yet

## Verification Snapshot

Latest local verification:

- `dotnet build AgentBlazor.sln -nologo /p:UseSharedCompilation=false`
- `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj`
- `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj`
- `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj`
- `npm --prefix tests/e2e run test:e2e`

Latest browser status:

- full end-to-end Playwright suite passed
- current suite count: `12/12`

Coverage includes:

- landing page journey
- Dojo layout and pillar switching
- prompt-backed Dojo validation across all three pillars
- failure-path / clarification-path checks
- components explorer layout and focused component routes
- side-by-side parity proof routes, including the composed workflow screen

## Current Gaps

### Product Gaps

- Persistent user-level intelligence is not complete:
  - no durable `IActionHistoryStore` implementation yet
  - paid suggestions are not yet a mature long-term personalization system
- Open-ended generative UI is demonstrated in Dojo, but broader productized hosting controls are still limited.
- Some component demos still prove isolated control better than full workflow depth, but the workflow showcase now spans multiple blocked and approval-gated scenarios.

### Demo Gaps

- The Dojo now tells the right story, and it covers supplier, file, recipe, incident, and response-orchestration workflows with a shared decision-support shell, explicit recovery paths on the richer scenarios, and one cross-system composition route, but it is still a curated showcase rather than a complete playground.
- Compatibility proof now exists for every shipped Mud-backed `Agent*` component, but some surfaces are still only proven in one or two shapes:
  - `AgentTreeView` still needs deeper hierarchy and expansion scenarios
  - composed workflow proof should keep expanding beyond the current supplier, file, recipe, incident, and response-orchestration workflow set
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

1. Keep tightening the workflow-first shell and UX so the supplier-compliance, file-audit, recipe-release, incident-escalation, and response-orchestration scenarios are the default story, not the component explorer.
2. Keep removing planner-era compatibility plumbing now that the adapter-backed runtime path is the default.
3. Continue narrowing and deprecating the remaining legacy runtime-first surfaces, especially the now-explicit default-agent compatibility options that remain after removing the standalone `AgentBlazor.DefaultAgent` package from the active solution.
4. Keep aligning any remaining showcase/detail surfaces around the same normalized execution-plan and approval model now used by chat, approval prompts, inspector, and workflow pages.
5. Continue expanding workflow-first proof from the current five-route showcase into broader production-style scenarios, especially deeper cross-screen and cross-system compositions.
6. Continue parity depth where needed, but stop treating primitive component-control coverage as the main product measure.

Current note:
- `AgentBlazorOptions.DefaultAgent` is now explicitly obsolete as a legacy compatibility surface; host apps should move toward explicit `AddAgent(...)` registration and only keep default-agent fallback during migration.
- Adapter-backed inspector and trace persistence now record normalized step-oriented execution data as the canonical devtools shape; remaining legacy action/result payloads should only survive where the legacy runtime path still needs them.
- The landing page, workflow hub, dojo, and components explorer now consistently reinforce a workflow-first journey, with dojo/components positioned as supporting reference surfaces rather than the default destination.
- The workflow hub and workflow routes now use route-specific assistant profiles and semantic-first prompt guidance, so the assistant defaults line up with the workflow-first product story instead of falling back to generic component language.
- Prompt tracing and report-style consumers now expose normalized workflow-step views as the primary reporting language, with planner-era action/result lists left as compatibility storage rather than the first-class presentation model.
- Component-mocking and report-generation test helpers now prefer normalized `ExecutionPlan` data and only fall back to legacy action/result payloads when the legacy runtime path returns no plan.
- `AgentRuntimeIntegrationTests` now use normalized execution-plan helpers first and explicitly opt into legacy default-agent fallback where they are validating the legacy deterministic runtime path.
- Hosted AG-UI response metadata now reports normalized execution-step counts before falling back to legacy planned-action counts.
- `AgUiHostingIntegrationTests` now opt into legacy default-agent fallback explicitly on the legacy planner path, so the hosted AG-UI event assertions are green again without reintroducing implicit fallback on the normal service path.
- focused response/history tests now treat `ExecutionPlan` as the primary model and use `LegacyPlannedActions` / `LegacyExecutionResults` only when they are explicitly validating compatibility behavior.
- the shared `/demo` shell now presents the experience as a workflow hub and uses route-aware assistant guidance so workflow routes lead with semantic workflow language rather than dojo/component-control framing.
- prompt-trace reports and inspector phase labels now use workflow-step terminology, so workflow runs read consistently across reports, chat, workflow pages, and inspector/devtools.
- Legacy prompt-tracing and component-mocking test suites now opt into default-agent fallback explicitly so they exercise the compatibility path on purpose rather than depending on deprecated defaults.
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

## Reference Docs

- Architecture: `docs/architecture.md`
- Expansion roadmap: `docs/component-expansion-plan.md`
- MudBlazor compatibility roadmap: `docs/mudblazor-compatibility-roadmap.md`
- NuGet prerelease checklist: `docs/nuget-prerelease-checklist.md`
- GitHub Packages private preview: `docs/github-packages-private-preview.md`
- Tier model: `docs/pricing-tiers.md`
- Runtime realignment plan: `docs/runtime-realignment-plan.md`
