# AgentBlazor Push Roadmap (Q2 2026)

Last updated: 2026-02-24

## Goal

Position AgentBlazor as the default agentic UI runtime for Blazor/.NET teams by combining:
- AG-UI protocol compatibility
- deterministic enterprise-safe execution
- Blazor-native generative UI and component orchestration

## Strategic Positioning

### Positioning statement

AgentBlazor is the Blazor/.NET-first agentic UI platform for teams that need production reliability, policy control, and AG-UI interoperability without moving to a JavaScript frontend stack.

### Where we should lead

1. Deterministic execution + approval/policy controls as first-class product features.
2. Blazor-native generative UI patterns (Razor components, typed contracts, testability).
3. AG-UI compatibility so AgentBlazor fits into broader agent ecosystems.
4. Enterprise operations: observability, replay, auditability, and entitlement controls.

### Competitive adaptation map (CopilotKit -> AgentBlazor)

| CopilotKit pattern | Why it matters | AgentBlazor adaptation |
|---|---|---|
| Generative UI emphasis | Frontend value is in interaction, not only model calls | Add typed declarative UI spec for Razor rendering (`AgentCard`, `AgentFormBlock`, `AgentTableBlock`) |
| AG-UI as transport layer | Avoid lock-in and improve ecosystem interoperability | Continue AG-UI-first runtime/events and complete event parity in UI components |
| Human-in-the-loop workflows | Required for real business workflows | Expand approval/clarification into full interrupt/resume timeline semantics |
| Strong starter/template distribution | Accelerates adoption more than feature docs alone | Publish scenario-first Blazor starter apps and "drop-in" component recipes |
| Open source + commercial packaging | Creates top-of-funnel and revenue path | Keep OSS core, package enterprise observability/security/deployment capabilities |

## 6-Week Execution Backlog

### Week 1: Product Surface Lock + Message Clarity

- `AB-PUSH-001` Publish positioning doc and value pillars
  - Acceptance criteria:
  - One public-facing message: "AgentBlazor = Agentic Frontend for Blazor/.NET".
  - 3 differentiators explicitly documented: deterministic execution, governance, Blazor-native integration.
- `AB-PUSH-002` Publish capability matrix (what works today, what is roadmap)
  - Acceptance criteria:
  - Matrix includes AG-UI, deterministic runtime, approvals, clarification, component wrappers, streaming.
  - Gaps explicitly marked with target milestone.
  - Current artifact: `docs/agentblazor-capability-matrix.md`
- `AB-PUSH-003` Create "Why not React?" decision guide for .NET teams
  - Acceptance criteria:
  - Includes migration cost, governance concerns, staffing realities, and operational ownership.

### Week 2: Generative UI MVP (Blazor-native)

- `AB-PUSH-010` Define `AgentBlazor.UI.Spec` v0 schema
  - Acceptance criteria:
  - Schema supports cards, forms, data tables, actions, and layout hints.
  - Versioned contract + compatibility notes documented.
- `AB-PUSH-011` Build Razor renderer for v0 schema
  - Acceptance criteria:
  - Can render at least 3 block types from streamed/returned spec payloads.
  - Rendering supports action callbacks into existing action executors.
- `AB-PUSH-012` Add demo page "Generative UI in Blazor"
  - Acceptance criteria:
  - End-to-end flow: prompt -> generated UI blocks -> user action -> agent continuation.

### Week 3: AG-UI Event Completeness + HITL Hardening

- `AB-PUSH-020` Expand runtime stream events to full lifecycle coverage
  - Acceptance criteria:
  - Run/step/tool/result/state lifecycle events emitted with stable event kinds.
  - Event schema docs mapped against AG-UI concepts.
- `AB-PUSH-021` Add interrupt/resume semantics for approvals and clarifications
  - Acceptance criteria:
  - Explicit "interrupted" state in chat timeline and resumable continuation tokens.
  - Multi-step approvals survive navigation and remount flows.
- `AB-PUSH-022` Add event replay support in demo/dev tooling
  - Acceptance criteria:
  - User can inspect timeline of planned actions, execution results, deferred completions.

### Week 4: Distribution and Adoption Assets

- `AB-PUSH-030` Ship 4 scenario-first starter templates
  - Acceptance criteria:
  - `supplier-risk`, `onboarding-assistant`, `operations-dashboard`, `support-desk`.
  - Each starter has one-page quickstart and runnable demo.
- `AB-PUSH-031` Publish "15-minute getting started" with copy-paste code
  - Acceptance criteria:
  - New app setup to first successful action in <= 15 minutes.
  - Includes local model and cloud model variants.
- `AB-PUSH-032` Add integration adapters docs
  - Acceptance criteria:
  - Microsoft Agent Framework and Semantic Kernel integration walkthroughs.
  - AG-UI endpoint examples with minimal host setup.

### Week 5: Enterprise Readiness Layer

- `AB-PUSH-040` Add run analytics + audit event model
  - Acceptance criteria:
  - Every turn has traceable run id, action outcomes, approval decisions, timestamps.
  - Export hooks documented for SIEM/App Insights/OpenTelemetry.
- `AB-PUSH-041` Add policy pack presets by environment (dev/stage/prod)
  - Acceptance criteria:
  - Preset policy profiles configurable at startup with override hooks.
  - Dangerous actions disabled by default in production preset.
- `AB-PUSH-042` Add compliance-focused deployment profiles
  - Acceptance criteria:
  - Documented cloud, VPC, and on-prem reference topologies.
  - Offline/air-gapped operation constraints listed.

### Week 6: Launch Motion + Proof

- `AB-PUSH-050` Publish benchmark-style reliability report
  - Acceptance criteria:
  - Measures clarification rate, action success rate, deterministic replay behavior.
  - Includes known failure categories and mitigation patterns.
- `AB-PUSH-051` Launch "AgentBlazor Reference App"
  - Acceptance criteria:
  - Demonstrates generative UI, approvals, deferred actions, and route-aware orchestration.
  - Includes test suite and scripted demo prompts.
- `AB-PUSH-052` Public release package + upgrade guide
  - Acceptance criteria:
  - NuGet packages + migration notes + changelog + docs links all aligned.

## Success Metrics

### Product
- `turn_action_success_rate >= 90%` on reference scenarios.
- `clarification_recovery_rate >= 80%` (clarification leads to successful action within 2 turns).
- `deferred_action_visibility = 100%` (queued action completions visible in UI timeline).

### Adoption
- 4 production-like starters published.
- 3 end-to-end demos recorded and documented.
- Time-to-first-action <= 15 minutes for new users.

### Commercial
- Clear Free/Paid/Premium feature boundary reflected in docs and samples.
- At least one enterprise-focused deployment guide published.

## Immediate Execution Order (Next 3 PRs)

1. PR-1: Product narrative + capability matrix + docs IA scaffolding.
2. PR-2: Generative UI spec v0 + renderer MVP + demo page.
3. PR-3: AG-UI event parity and interrupt/resume hardening in chat surfaces.

## External Research Inputs

- CopilotKit site: https://www.copilotkit.ai/
- CopilotKit GitHub: https://github.com/CopilotKit/CopilotKit
- AG-UI protocol docs: https://docs.ag-ui.com/
- AG-UI protocol GitHub: https://github.com/ag-ui-protocol/ag-ui
- CopilotKit AG-UI vs A2UI article: https://www.copilotkit.ai/ag-ui-and-a2ui
