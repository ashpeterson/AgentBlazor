# AgentBlazor Development Status

Last updated: 2026-03-12

## Current Product Shape

AgentBlazor is now positioned as a Blazor-first agentic UI framework with two primary demo surfaces:

- `/` explains the product and routes users into the current story
- `/demo/dojo` demonstrates the three core generative UI pillars
- `/demo/components` demonstrates drop-in agentic components for Blazor apps

The current story is no longer "many unrelated demo routes". It is:

1. learn the platform on the landing page
2. understand the patterns in the Dojo
3. inspect the reusable components in the components explorer

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
- Dojo explains the patterns
- Agentic Components shows the reusable primitives

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
- `npx playwright test --config=playwright.config.cjs`

Latest browser status:

- full end-to-end Playwright suite passed
- current suite count: `12/12`

Coverage includes:

- landing page journey
- Dojo layout and pillar switching
- prompt-backed Dojo validation across all three pillars
- failure-path / clarification-path checks
- components explorer layout and focused component routes

## Current Gaps

### Product Gaps

- Persistent user-level intelligence is not complete:
  - no durable `IActionHistoryStore` implementation yet
  - paid suggestions are not yet a mature long-term personalization system
- Open-ended generative UI is demonstrated in Dojo, but broader productized hosting controls are still limited.
- Some component demos still prove isolated control better than full workflow depth.

### Demo Gaps

- The Dojo now tells the right story, but it is still a curated showcase rather than a complete playground.
- The components explorer is strong for drop-in primitives, but richer production narratives are still uneven across:
  - `AgentTreeView`
  - `AgentStepper`
  - `AgentCommandBar`
  - `AgentFileUpload`

### Platform Gaps

- Declarative interoperability exists, but only as an adapter subset today, not full schema coverage for every external declarative UI feature.
- MCP tool integration exists, but a broader production-grade hosted-app contract still needs more depth if open-ended hosting becomes a larger platform story.

## What Needs Doing Next

1. Build a durable action history store so paid suggestions and insights are grounded in persistent user behavior.
2. Keep strengthening prompt determinism and observability on focused component routes.
3. Deepen workflow examples for the more complex agentic components.
4. Expand declarative adapter coverage while keeping `AgentUiDocument` as the native rendering model.
5. Continue refining the home -> dojo -> components journey so the public demo feels like one product, not a set of samples.

## Reference Docs

- Architecture: `docs/architecture.md`
- Expansion roadmap: `docs/component-expansion-plan.md`
- Tier model: `docs/pricing-tiers.md`
