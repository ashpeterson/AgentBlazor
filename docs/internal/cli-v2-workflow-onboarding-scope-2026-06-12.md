# AgentBlazor CLI V2 Workflow Onboarding Scope

Last updated: 2026-06-12

## Goal

`agentblazor scaffold workflows` adds an approved workflow-onboarding path on top of the existing analyze and scaffold stack. It keeps manual `[AgentCapability]` and `[AgentAction]` authoring valid, but gives the CLI enough app context to propose multi-step workflows, generate `.agentblazor` SOUL and skill files, and separate baseline install wiring from workflow artifacts.

## Initial Implementation

- Extend `ProjectModel` with an `AnalysisCorpus` built from routes, pages, services, methods, DI registrations, existing capability actions, workflow clusters, domain terms, route correlations, and file references.
- Add an in-memory semantic retrieval abstraction with a lexical implementation for the first release slice.
- Keep `agentblazor analyze` read-only. It may use the deeper model and corpus, but it must only write the analysis report requested by the user.
- Add `agentblazor scaffold workflows [path] --host <project> --provider <provider> --scan-scope <references|solution> --diff --approve --non-interactive`.
- Support `--reviewed-by <name>` so workflow approval, rejection, and pin decisions can carry a reviewer identity into generated review artifacts.
- In non-interactive mode, write a report unless workflow ids are supplied with `--approve`.
- Generate deterministic `.agentblazor/workflow-onboarding.json`, `.agentblazor/workflow-onboarding.md`, and `.agentblazor/workflow-onboarding.html` review artifacts for candidate approval, team review, and audit trails.
- Generate deterministic `.agentblazor/SOUL.md`, `.agentblazor/skills/index.json`, `.agentblazor/skills/<skill-slug>/SKILL.md`, optional references, and `.agentblazor/skills/.metadata.json` for approved workflows.
- Apply approved workflow artifacts through the AgentBlazor-owned agent-loop patch proposal path and write `.agentblazor/audit/workflow-onboarding-<timestamp>.json` with reviewer, workflow decision metadata, proposed/applied files, and tool transcript.
- Treat baseline AgentBlazor wiring and workflow artifacts as separate approval groups.
- Implement skill progressive disclosure APIs in the analysis layer: index, full `SKILL.md`, and reference file views.
- Track skill reads/executions and curator metadata immediately. Preserve pinned skills, mark stale after 30 inactive days, and archive unpinned stale skills after an additional 60 inactive days.
- Add the first AgentBlazor-owned C# agent-loop primitives: root-scoped file reads, patch proposals, diff rendering, preview conversion, approved patch apply, validation command execution, and an auditable tool transcript.

## Later Phases

- Add embedding-backed retrieval behind the same retrieval abstraction.
- Connect the AgentBlazor-owned C# agent loop to model-backed planning and workflow scaffolding. The loop must propose patches into the existing preview/apply pipeline and must not let a model write files directly.
- Add cheap-model skill refinement only after usage tracking and archive behavior are covered by tests.

## Safety Rules

- Reject workflow suggestions that reference methods not present in static evidence.
- Reject direct writes outside the detected solution root unless that path is explicitly approved.
- Require separate approvals for analysis artifacts, SOUL.md, skill files, capability/workflow classes, Program.cs/service wiring, UI/chat wiring, and validation commands.
- Encode restrictions at three levels: SOUL.md project restrictions, CLI session mode/file-scope restrictions, and SKILL.md frontmatter restrictions.

## Verification

- Unit tests cover workflow cluster evidence, retrieval, unsupported suggestion rejection, SOUL/SKILL determinism, skill view restrictions, and stale/archive behavior.
- CLI verification covers read-only analyze, workflow scaffold diff preview, approved artifact generation, non-interactive refusal for ambiguous workflow application, and unchanged baseline scaffold behavior.
