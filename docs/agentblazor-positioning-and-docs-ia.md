# AgentBlazor Positioning and Docs IA

Last updated: 2026-02-24

## Product Positioning

### Category

Agentic frontend runtime for Blazor/.NET applications.

### One-liner

Build reliable AI-powered user interfaces in Blazor with deterministic execution, policy controls, and AG-UI interoperability.

### Target audience

1. .NET teams already shipping Blazor apps.
2. Platform/architecture teams needing governance over agent behavior.
3. Enterprises that require auditability and controlled tool execution.

### Core differentiators

1. Blazor-native component orchestration (not a React dependency).
2. Deterministic planning/execution path with explicit approvals.
3. AG-UI protocol compatibility for broader ecosystem connectivity.
4. Enterprise packaging model with policy + entitlement boundaries.

## Messaging Framework

### Headline options

1. "AgentBlazor: Agentic UI for Blazor and .NET"
2. "Deterministic AI UX for enterprise Blazor apps"
3. "AG-UI compatible agent runtime, built for .NET teams"

### "Why now" narrative

- Agent UX is moving from chat-only interactions to workflow-aware UI orchestration.
- .NET teams need a first-class option that preserves Blazor investments.
- Governance and deterministic behavior are now procurement-level requirements.

### "Why us" narrative

- Existing foundations already include AG-UI hosting, approvals, entitlements, and typed component wrappers.
- Product can move faster than general-purpose frontend frameworks in regulated .NET environments.

## Recommended Documentation Information Architecture

### Top-level docs structure

1. `Getting Started`
2. `Core Concepts`
3. `Component Actions`
4. `Generative UI` (new)
5. `AG-UI and Integrations`
6. `Security, Policy, and Approvals`
7. `Observability and Operations`
8. `Reference Apps and Templates`
9. `Commercial and Packaging`

### Proposed file map (next iteration)

- `docs/getting-started/quickstart.md`
- `docs/getting-started/first-agent-app.md`
- `docs/concepts/runtime-model.md`
- `docs/concepts/deterministic-execution.md`
- `docs/concepts/human-in-the-loop.md`
- `docs/components/datagrid.md`
- `docs/components/dialog.md`
- `docs/components/form.md`
- `docs/components/navigation.md`
- `docs/components/tabs.md`
- `docs/generative-ui/spec-v0.md` (new)
- `docs/generative-ui/renderer.md` (new)
- `docs/integrations/ag-ui.md`
- `docs/integrations/microsoft-agent-framework.md`
- `docs/integrations/semantic-kernel.md`
- `docs/security/policy-model.md`
- `docs/security/approval-flows.md`
- `docs/operations/telemetry.md`
- `docs/operations/replay-and-audit.md` (new)
- `docs/reference-apps/supplier-risk.md`
- `docs/reference-apps/onboarding-assistant.md`
- `docs/commercial/pricing-tiers.md`
- `docs/commercial/deployment-profiles.md` (new)

### Keep / Update / Consolidate guidance

#### Keep
- `docs/quickstart.md`
- `docs/architecture.md`
- `docs/pricing-tiers.md`
- `docs/compatibility-matrix.md`
- `docs/spec-references.md`

#### Update
- `docs/architecture.md`: align wording to deterministic runtime and AG-UI event strategy.
- `docs/quickstart.md`: add scenario-based "first success" path.

#### Consolidate
- Move plan-heavy internal docs under `docs/archive/plans/` after extracting implemented outcomes.
- Keep one "active roadmap" doc for current quarter.

## Content Standards

1. Every doc has a "Who is this for?" section.
2. Every feature doc includes:
   - supported actions
   - approval behavior
   - failure/clarification behavior
   - copy-paste code sample
3. Every integration doc includes:
   - minimum working setup
   - production hardening checklist
   - known limitations

## 30-Day Content Backlog

- `DOC-001` Publish new landing narrative and capability matrix.
- `DOC-002` Add reference-app walkthrough docs for 2 vertical scenarios.
- `DOC-003` Publish AG-UI event mapping table (AgentBlazor event kinds -> AG-UI concepts).
- `DOC-004` Add deterministic replay and audit guidance.
- `DOC-005` Add "production readiness checklist" doc.

## External References

- CopilotKit: https://www.copilotkit.ai/
- CopilotKit GitHub: https://github.com/CopilotKit/CopilotKit
- AG-UI docs: https://docs.ag-ui.com/
- AG-UI GitHub: https://github.com/ag-ui-protocol/ag-ui
