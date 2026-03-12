# AgentBlazor Architecture

Last updated: 2026-03-12

## Purpose

Describe the current architecture as it exists in this repository today.

The main architectural decision is unchanged:

- AgentBlazor is Blazor-first
- external protocols are interoperability targets
- the native rendering model stays in C# and Blazor

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
  e2e/
```

## Layer Responsibilities

### AgentBlazor.Core

Owns deterministic runtime behavior and canonical contracts:

- planning, validation, and execution
- component capability metadata
- policy and entitlement checks
- route and agent resolution
- shared state contracts
- generative UI document model
- declarative UI import adapters
- tool and MCP abstractions

Core is the source of truth for:

- `AgentUiDocument`
- `AgentComponentCapabilityProfile`
- `AgentComponentTierBoundaries`
- `AgentPlanner`
- `PlanValidator`
- `AgentRuntime`

### AgentBlazor.Components

Owns the native Blazor UI surface:

- chat components
- inspector UI
- built-in agentic components
- generative UI rendering components

This layer is where Blazor and MudBlazor stay first-class.

### AgentBlazor.Hosting

Owns app startup and protocol/endpoint wiring:

- `AddAgentBlazor(...)`
- AG-UI endpoint mapping
- dev tools and paid-service registration
- host integration for ASP.NET / Blazor apps

### AgentBlazor.ProviderAdapters

Owns LLM provider registration only:

- OpenAI
- Azure OpenAI
- Ollama

### AgentBlazor.DefaultAgent

Owns default agent descriptor plumbing for the built-in component-aware agent.

### AgentBlazor.Licensing

Owns entitlement primitives:

- `Free`
- `Paid`
- `Premium`

## Dependency Direction

Current compile-time direction:

- `AgentBlazor.Core -> AgentBlazor.Licensing`
- `AgentBlazor.Hosting -> AgentBlazor.Core`, `AgentBlazor.ProviderAdapters`, `AgentBlazor.DefaultAgent`, `AgentBlazor.Licensing`
- `AgentBlazor.Components -> AgentBlazor.Core`, `AgentBlazor.Hosting`

This keeps runtime decisions out of the UI layer and keeps provider-specific code out of Core.

## Runtime Pipeline

The runtime flow remains:

1. gather request context
2. plan
3. normalize targets/actions
4. apply policy and entitlement filters
5. validate against mounted components
6. execute
7. emit events and persist conversation/state

The important current behavior is:

- validation checks mounted live components, not only static catalog metadata
- planner/validator recovery paths repair some invented component/action ids when the intent can be resolved deterministically
- blocked actions return deterministic diagnostics

## Native UI Model

The native UI model is still Blazor-first.

### Controlled UI

Controlled UI is built on:

- mounted Blazor components
- typed actions/readables
- deterministic host-owned layouts
- wrappers such as `AgentDataGrid`, `AgentForm`, `AgentDialog`, `AgentNavMenu`, and others

This is the most mature pattern in the repository.

### Declarative UI

The native declarative model is:

- `AgentUiDocument`

The native renderer is:

- `AgentGenerativeSurface`

Supported native block types include:

- card
- form
- table
- chart

### Open-ended UI

Open-ended UI is currently handled as a hosted surface pattern rather than as the main authoring model.

The current Dojo demonstrates this as a sandboxed hosted app surface inside the Blazor shell.

## Declarative Interoperability

AgentBlazor now supports importing external declarative UI payloads into the native model instead of maintaining separate rendering pipelines.

Current adapters:

- `A2UI -> AgentUiDocument`
- `Open-JSON-UI -> AgentUiDocument`

Implemented in:

- `AgentUiInterchangeAdapters`

This is the key interoperability choice:

- open standards can be consumed by Blazor
- they do not replace the native rendering model

That keeps one renderer, one validation model, and one Blazor/MudBlazor visual system.

## AG-UI and Hosting

AG-UI is present as the runtime interaction and hosting layer:

- AG-UI endpoints are mapped in Hosting
- `DeterministicAgUiHostedAgent` bridges AG-UI runs into the native runtime
- stream events, state events, and blocked outcomes are surfaced consistently

This means AgentBlazor is not approximating AG-UI conceptually; it has an explicit AG-UI hosting path.

## Shared State Model

Shared state is a first-class runtime contract.

Current pieces:

- `IAgentSharedStateStore`
- in-memory provider by default
- optional JSON file persistence
- snapshot and delta events
- merge policy support

Current role in architecture:

- route and mounted-component state is gathered into shared runtime context
- that state is available to the planner
- runtime emits structured state events for debugging and AG-UI flow

## Multi-Agent Model

Multi-agent support is part of the runtime, not an add-on demo trick.

Current capabilities:

- route-scoped agent resolution
- locked agent mode
- explicit handoff commands
- approval policies
- handoff policies
- loop guards
- handoff diagnostics in the inspector

## Inspector Model

The inspector is now a real in-app debugging surface.

Current responsibilities:

- per-run summaries
- event timeline
- planning / validation / execution phases
- payload inspection
- stream filtering
- handoff correlation
- state delta inspection

The widget and embedded chat surfaces can expose this in development without requiring a paid tier.

## Component Model

The shipped built-in component set is:

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

Custom component authoring is also a first-class architectural path through:

- `AgentControllableComponentBase`
- attribute-based discovery such as `[AgentComponent]`, `[AgentAction]`, `[AgentReadable]`, and `[AgentParam]`

This is part of the core product story, not a side example.

## Demo App Architecture

The demo app is now organized around one coherent journey:

- Home: product framing
- Dojo: capability patterns
- Agentic Components: drop-in component exploration

### Dojo

The Dojo is now a focused three-pillar shell:

1. Controlled Generative UI
2. Declarative Generative UI
3. Open-ended Generative UI

Each pillar has:

- `Preview`
- `Code`
- `Docs`

The embedded chat is route-locked and tested across pillar changes.

### Agentic Components

The components explorer is now a docs-style layout:

- top navigation bar
- left catalog
- central live example and contract sections
- right contents rail
- floating chat bubble

This is meant to demonstrate drop-in agentic components, not to duplicate the Dojo.

## Licensing Architecture

Current architecture split:

- component actions are broadly free
- paid value is primarily service-backed intelligence

Today `UseProLicense(...)` swaps in:

- `InMemoryActionHistoryStore`
- `LlmAdaptiveSuggestionService`
- `LlmProactiveInsightService`
- `InMemoryAgentInspectorStore`

Important architectural limitation:

- durable persistent user-intelligence is not complete yet
- the current action history implementation is still in-memory

## Clean Architecture Guardrails

The current direction should stay:

- Core owns runtime logic and canonical models
- Components owns UI rendering and Blazor ergonomics
- Hosting owns protocols, endpoints, and startup wiring
- ProviderAdapters owns model providers only
- Licensing owns entitlement primitives only

Do not:

- move provider logic into Core
- make external UI schemas the primary authoring model
- move planning/execution logic into Components
- turn the demo app into the source of truth for runtime behavior

## Current Strategic Gaps

- persistent paid intelligence is incomplete
- declarative interoperability is useful but still a subset implementation
- open-ended hosted-app support is present as a demo pattern but not yet deeply productized
- some component demos still need richer workflow depth

## Reference Docs

- Current status: `docs/STATUS.md`
- Product roadmap: `docs/component-expansion-plan.md`
- Tier model: `docs/pricing-tiers.md`
