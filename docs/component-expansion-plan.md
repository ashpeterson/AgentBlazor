# Product Expansion Plan

Last updated: 2026-04-17

## Goal

Move AgentBlazor forward as the native agentic UI framework for Blazor and .NET teams.

That means:

- keep the platform Blazor-first
- make the demo app tell one coherent story
- deepen the production value of the shipped agentic components
- expand interoperability without surrendering the native rendering model

## Current Baseline

### Platform

- deterministic runtime is stable
- AG-UI hosting is present
- shared state exists
- multi-agent route lock and handoff support exist
- in-app inspector is shipped
- native generative UI rendering exists through `AgentUiDocument`
- declarative import adapters now exist for:
  - `A2UI`
  - `Open-JSON-UI`

### Demo App

- landing page introduces the product
- `/demo` is now the workflow hub and primary product-story entry point
- the workflow hub now includes production-style orchestration routes such as `response-orchestration` and `release-dossier`
- the landing page and workflow hub now lead with fast-launch orchestration flows and minimal supporting copy for repeatable recordings
- Agentic Components demonstrates drop-in components as a supporting reference surface
- `.agentblazor/AGENT.md` was regenerated on 2026-04-15 and currently reports 23 routes and 196 actions

### Agentic Components

The built-in drop-in component set is broad, the docs-style explorer is in place, and the MudBlazor compatibility rewrite is now materially underway rather than hypothetical.

The next need is less breadth and more believable workflow depth.

## Strategic Direction

The repo should not try to become "CopilotKit rewritten in C#".

The correct direction is:

- native Blazor components
- C# contracts and DI
- MudBlazor-friendly rendering
- ASP.NET and AG-UI interoperability
- external spec adapters where useful

## Product Workstreams

### 1. Agentic Components Depth

Objective:

- make the built-in components feel production-ready, not just controllable

Critical requirement:

- shipped `Agent*` components need to become true drop-in replacements for complex MudBlazor usage, not only simplified wrapper examples

Priority components:

- `AgentDataGrid`
- `AgentTreeView`
- `AgentStepper`
- `AgentCommandBar`
- `AgentFileUpload`

Needed work:

- finish the compatibility-first proof story around the native-first component set
- preserve and validate richer MudBlazor scenarios such as server-backed grids, deeper trees, and more composed workflows
- richer live examples
- more realistic prompt scenarios
- better state transitions and workflow traces
- clearer contract docs inside the explorer

Current status:

- the high-surface Mud-backed `Agent*` components have already been moved onto native-first implementations
- the components explorer now exposes focused live examples for every shipped Mud-backed `Agent*` component
- rendered parity tests and browser coverage are in place
- external chat validation now exercises every shipped chat surface, not just the floating widget, by injecting a temporary harness route into real cloned Blazor apps and prompt-testing `AgentChatSurface`, `AgentChatPanel`, and `AgentChatBar`
- prompt examples now focus on production Blazor workflows such as operations queues, finance review, identity audit, release readiness, approval gates, and incident support

Remaining work:

- deepen `AgentDataGrid` proof around richer server-backed and templated usage
- deepen `AgentTreeView` proof around hierarchy-heavy screens
- decide and document the long-term role of `AgentCommandBar`
- continue broadening composed-screen proofs so the story is not only isolated component parity

### 2. Declarative UI Interop

Objective:

- expand standards interoperability while keeping one native rendering pipeline

Current approach:

- `A2UI -> AgentUiDocument`
- `Open-JSON-UI -> AgentUiDocument`

Needed work:

- broaden mapping coverage
- improve diagnostics for unsupported external payloads
- add more realistic end-to-end examples inside the workflow-first demo or focused reference surfaces

### 3. Paid Intelligence

Objective:

- make paid differentiation real, not just wired

Current status:

- action history abstraction exists
- LLM-based suggestions and insights exist
- current action history store is still in-memory

Needed work:

- durable `IActionHistoryStore`
- persistent user-level behavior model
- clearer productization of suggestions and insights

### 4. Observability

Objective:

- keep debugging fast and trustworthy

Current status:

- inspector is already useful for planning, validation, execution, and state events

Needed work:

- deeper cross-run troubleshooting views
- cleaner correlation for larger workflows
- continued prompt-backed browser validation against real routes

## Recommended Delivery Order

1. deepen the workflow-first product proof and the higher-value component scenarios that support it
2. implement durable paid action history
3. expand declarative adapter coverage
4. continue inspector and troubleshooting improvements
5. deepen hosted/open-ended surface capabilities only if product demand justifies it

## Definition of Done

For each major increment:

- behavior is implemented in the correct layer
- docs are updated in `docs/`
- demo app reflects the new behavior
- Playwright validates the public-facing flow
- unit and integration tests cover the underlying contracts

## Guardrails

Do:

- keep `AgentUiDocument` as the canonical native declarative model
- keep Agentic Components focused on reusable primitives
- use adapters for external declarative schemas

Do not:

- replace the native model with external schemas
- monetize baseline component interactions before the intelligence story is real
- let demo-specific abstractions become the platform architecture

## Success Criteria

AgentBlazor should clearly communicate:

- "Here are the agentic components you can drop into a Blazor app today."
- "Here is how the broader workflow-first UI patterns work in Blazor."
- "Here is where paid intelligence adds value once persistence is real."

Current state against that standard:

- the drop-in component story is now credible and publicly demonstrated
- the broader Blazor-first pattern story is now visible through Home, the workflow hub, and Agentic Components
- the weakest remaining product message is still paid intelligence, because persistence is not complete yet

Compatibility planning reference:

- `docs/mudblazor-compatibility-roadmap.md`
