# AgentBlazor Capability Matrix

Last updated: 2026-02-24

## Purpose

Provide a clear view of what AgentBlazor supports today versus planned roadmap capabilities.

Status legend:
- `Shipped`
- `In Progress`
- `Planned`

## Runtime and Protocol

| Capability | Status | Notes |
|---|---|---|
| Deterministic planning/execution runtime | `Shipped` | Plan -> validate -> execute flow is active in core runtime |
| Streaming assistant text deltas | `Shipped` | Chat surfaces support progressive response rendering |
| AG-UI hosted endpoint (`/agentblazor/agui/run`) | `Shipped` | Hosted path available and tested |
| AG-UI lifecycle/tool/state event parity in UI | `In Progress` | Runtime emits core stream events; UI parity and richer mapping still expanding |
| Interrupt/resume semantics for approvals/clarification | `In Progress` | Approval + clarification exist; richer interrupted/resume model planned |

## Component Orchestration

| Capability | Status | Notes |
|---|---|---|
| AgentDataGrid action execution | `Shipped` | Filter/sort/select/paging/navigation action support |
| AgentDialog action execution | `Shipped` | Open/close/confirm support with policy checks |
| AgentForm action execution | `Shipped` | Set/validate/reset/submit with approval controls |
| AgentNavMenu + AgentTabs action execution | `Shipped` | Navigation and tab switching support |
| Deferred action queue + auto-apply on mount | `Shipped` | Pending intents execute when target component mounts |
| Deferred action completion visibility in chat | `Shipped` | Activity entries now show when deferred actions are applied |

## Safety and Governance

| Capability | Status | Notes |
|---|---|---|
| Allowed component/action policy filters | `Shipped` | Component and per-action allow-list controls |
| Approval-required action gating | `Shipped` | Approval policy enforced in runtime and hosted paths |
| Tier-based entitlement filtering | `Shipped` | Free/Paid/Premium boundaries documented and enforced |
| Environment policy presets (dev/stage/prod) | `Planned` | Defined in roadmap Week 5 |
| Full audit/replay toolkit for enterprise ops | `Planned` | Defined in roadmap Weeks 5-6 |

## Generative UI

| Capability | Status | Notes |
|---|---|---|
| Blazor-native generative UI spec (`UI.Spec`) | `In Progress` | v0 contract scaffolded (`AgentGenerativeUiSpec`) |
| Razor renderer for generated UI blocks | `In Progress` | `AgentGenerativeSurface` renders card/form/table blocks |
| End-to-end generative UI reference scenario | `Planned` | Roadmap Weeks 2 + 6 |

## Integrations and Distribution

| Capability | Status | Notes |
|---|---|---|
| Microsoft Agent Framework integration path | `Shipped` | Referenced by architecture + hosted AG-UI wiring |
| Semantic Kernel integration guide | `Planned` | Roadmap Week 4 |
| Scenario-first starter templates | `Planned` | Roadmap Week 4 |
| 15-minute onboarding path | `Planned` | Roadmap Week 4 |

## Reference Docs

- `docs/quickstart.md`
- `docs/architecture.md`
- `docs/generative-ui-spec-v0.md`
- `docs/mudblazor-capability-taxonomy.md`
- `docs/pricing-tiers.md`
- `docs/agentblazor-push-roadmap-q2-2026.md`
