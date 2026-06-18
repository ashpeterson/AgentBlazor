# AgentBlazor CLI V2 NuGet Release Testing Readiness

Date: 2026-06-18

## Scope

This readiness note covers the CLI V2 workflow onboarding path for NuGet release testing. It does not claim the whole AgentBlazor package set is production-ready; it records whether the packaged `AgentBlazor.Cli` can be handed to release testers for the V2 `scaffold workflows` flow.

## Release-Testing Decision

Status: ready for local NuGet release testing of CLI V2 workflow onboarding.

The current worktree has enough evidence to begin NuGet release testing for:

- `agentblazor scaffold workflows [path]`
- developer app description and desired agent workflow capture
- deep static workflow analysis with corpus/retrieval evidence
- review artifacts: `.agentblazor/workflow-onboarding.json`, `.agentblazor/workflow-onboarding.md`, `.agentblazor/workflow-onboarding.html`
- approved SOUL/skill generation
- reviewer-gated non-interactive approval
- AgentBlazor-owned agent-loop patch application and audit output
- packaged CLI install and workflow smoke from a local NuGet feed

## Evidence Matrix

| Requirement | Evidence | Status |
| --- | --- | --- |
| V2 scope is documented separately from CLI v1 | `docs/internal/cli-v2-workflow-onboarding-scope-2026-06-12.md` | Pass |
| `analyze` remains read-only | V2 scope preserves read-only `analyze`; V2 implementation is routed through `ScaffoldCommand` workflow mode | Pass |
| `scaffold workflows` exists as the workflow onboarding surface | `src/AgentBlazor.Cli/Commands/ScaffoldCommand.cs`; `src/AgentBlazor.Cli/Program.cs` examples | Pass |
| App description and desired agent workflows can guide analysis | `--description`, `--agent-goals`, `.agentblazorc` persistence; covered by `ScaffoldWorkflowsCommandTests` | Pass |
| Deep analysis corpus and lexical retrieval exist | `AnalysisCorpusBuilder`, `SemanticRetrieval`, `ProjectModel.Corpus`; covered by `WorkflowOnboardingTests.CorpusBuilder_CreatesRetrievableWorkflowEvidence` | Pass |
| Review artifacts are deterministic and include candidate evidence | `WorkflowOnboardingArtifactWriter`; covered by `WorkflowOnboardingTests` and scaffold workflow integration tests | Pass |
| Approved workflows generate SOUL and skill artifacts | `WorkflowOnboardingArtifactWriter`; covered by `ScaffoldWorkflowsApproveNonInteractive_WithWorkflowSelection_WritesSoulAndSkillArtifacts` | Pass |
| Review-only decisions do not generate SOUL/skills | Covered by `ScaffoldWorkflowsApproveNonInteractive_WithReviewActions_UpdatesReviewOnly` | Pass |
| Non-interactive approval refuses ambiguous workflow selection | Covered by `ScaffoldWorkflowsApproveNonInteractive_WithoutWorkflowSelection_RefusesApply` | Pass |
| Non-interactive workflow application requires reviewer identity | Covered by `ScaffoldWorkflowsApproveNonInteractive_WithWorkflowSelectionWithoutReviewer_RefusesApply` | Pass |
| Approved artifacts are applied through AgentBlazor-owned agent-loop patch proposal path | `AgentLoop`, `ScaffoldCommand` workflow apply path; covered by `AgentLoopTests` and scaffold workflow integration tests | Pass |
| Audit record includes reviewer, workflow decision metadata, proposed/applied files, and tool trace | `AgentLoop.WriteAuditAsync`, scaffold workflow integration tests, packaged smoke script | Pass |
| Skill progressive disclosure and curator behavior exist | `SkillSystem`; covered by `WorkflowOnboardingTests.SkillViewStore_LoadsIndexSkillAndReferenceWithinSkillRoot` and `SkillCurator_MarksStalePreservesPinnedAndArchivesEligibleSkills` | Pass |
| Packaged CLI can be installed from local NuGet output and run V2 workflow onboarding | `scripts/smoke-test-cli-v2-workflows.sh` | Pass |
| Package README documents V2 workflow onboarding | `src/AgentBlazor.Cli/PACKAGE_README.md` | Pass |
| NuGet prerelease checklist includes V2 workflow onboarding gate | `docs/internal/nuget-prerelease-checklist.md` | Pass |

## Verification Commands

Latest local verification for this readiness state:

```bash
scripts/smoke-test-cli-v2-workflows.sh
dotnet test tests/AgentBlazor.Cli.IntegrationTests/AgentBlazor.Cli.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ScaffoldWorkflowsCommandTests --logger "console;verbosity=minimal"
dotnet build src/AgentBlazor.Cli/AgentBlazor.Cli.csproj --no-restore
git diff --check
```

Observed results:

- `scripts/smoke-test-cli-v2-workflows.sh`: passed for packaged `AgentBlazor.Cli 0.2.22`
- `ScaffoldWorkflowsCommandTests`: 5 passed
- CLI project build: passed with 0 warnings and 0 errors
- `git diff --check`: passed

## Packaged Smoke Coverage

`scripts/smoke-test-cli-v2-workflows.sh` proves the current local package path can:

- pack `AgentBlazor.Cli`
- install the packaged tool using an isolated NuGet config
- verify the installed tool reports the package version
- run `agentblazor scaffold workflows --diff` against `tests/cli-targets/realistic-blazor-app`
- confirm preview mode does not write `SOUL.md`
- approve `same-service-lifecycle-inventory-pipeline` with `--reviewed-by`
- verify review JSON, Markdown, HTML, SOUL, skill index, metadata, SKILL, evidence reference, and audit JSON
- verify audit JSON contains reviewer, workflow ID, `propose_patch`, and `apply_approved_patch`
- verify audit JSON does not include the absolute scratch app path
- verify non-interactive approval without `--reviewed-by` fails

## Known Boundaries

- Model-backed autonomous workflow scaffolding is not included in this release-testing slice. The implemented loop is the controlled patch/audit substrate; model-backed planning remains a later phase.
- Embedding-backed retrieval is not included. The first release-testing slice uses in-memory lexical retrieval behind the retrieval abstraction.
- Published-feed validation has not been run for V2 workflow onboarding yet. The next external gate is to publish a prerelease feed package and repeat `scaffold workflows --diff` plus one reviewer-approved workflow from the published tool.
- Full solution restore/build remains broader than this CLI V2 gate because unrelated demo/package source mapping can affect full-solution commands. The V2 readiness gate is the packaged CLI workflow smoke plus targeted CLI tests.

## Next Release Testing Step

Publish the candidate package to the intended prerelease feed, then repeat:

```bash
agentblazor --version
agentblazor scaffold workflows path/to/App.csproj --description "..." --agent-goals "..." --diff --non-interactive
agentblazor scaffold workflows path/to/App.csproj --workflow <workflow-id-or-slug> --reviewed-by "<reviewer>" --approve --non-interactive
```

Verify the generated app contains:

- `.agentblazor/workflow-onboarding.json`
- `.agentblazor/workflow-onboarding.md`
- `.agentblazor/workflow-onboarding.html`
- `.agentblazor/SOUL.md`
- `.agentblazor/skills/index.json`
- at least one `.agentblazor/skills/<skill>/SKILL.md`
- `.agentblazor/skills/.metadata.json`
- `.agentblazor/audit/workflow-onboarding-*.json`
