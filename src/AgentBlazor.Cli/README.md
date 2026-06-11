# AgentBlazor CLI

The CLI is the advanced setup path for existing Blazor apps.

Read-only analysis:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

If no OpenAI key is configured and the terminal is interactive, `analyze` prompts for one and uses it for that run only. For repeat runs or CI, set `OPENAI_API_KEY` and optionally `AGENTBLAZOR_ANALYZE_MODEL`. Pass `--static-only` to skip the LLM call.

Version `0.2.5` and later include a static source-file fallback for Windows/Roslyn MSBuildWorkspace load failures. Current reports filter helper noise, show AgentBlazor action adoption, and include approval guidance for mutating workflow suggestions.

Version `0.2.7` and later include `--scan-scope solution` for multi-tenant and modular `.slnx` files where sibling projects should be scanned even when the Blazor host project does not reference them directly.

Version `0.2.8` hardens solution-scope workflow suggestions when multiple scanned projects expose the same service and method names.

Version `0.2.9` excludes test projects and test asset folders from default solution-scope analysis.

Version `0.2.10` prioritizes safe read-only workflow suggestions, labels suggestion risk, and requires approval for mutating command suggestions.

Version `0.2.11` filters internal chat persistence, state store, runner, scheduler, and tenant store services from workflow suggestions and recommended next steps.

Version `0.2.12` improves real-project solution scans by linking injected interfaces to implementation services, filtering infrastructure/identity/storage/http plumbing, and clearly reporting when all discovered services were filtered out.

Version `0.2.13` improves framework-style Blazor app analysis by explaining component-driven routing, filtering manager/state/auth/token/http plumbing, and using validated LLM workflow suggestions instead of appending static action guesses when LLM suggestions are available.

Version `0.2.14` improves report usability by putting top workflow recommendations and install blockers before the detailed inventory, adding route quality notes, and classifying services by likely agent fit.

Version `0.2.15` was superseded by `0.2.16`.

Version `0.2.16` removes the `0.2.15` map-layer workflow framing and filters UI layer applier, renderer, styling, and map-layer infrastructure from workflow suggestion inputs.

Version `0.2.17` improves workflow relevance by demoting pure read-only data/view suggestions from top recommendations when process-oriented workflow candidates exist.

Version `0.2.18` improves top recommendation quality by demoting raw integration, admin/sensitive, data-access, and infrastructure suggestions so reports prioritize real business/process workflows instead of plumbing.

Version `0.2.19` adds workflow-cluster context before LLM suggestion generation. The analyzer now groups lifecycle, route-correlated, and domain-correlated methods into multi-step process candidates so reports can identify pipelines rather than treating every public method as a standalone workflow.

Version `0.2.20` tightens clustered workflow analysis by preferring non-admin process clusters in the LLM prompt, validating cluster-backed suggestions against the whole pipeline instead of every method verb, and falling back to preferred static clusters when LLM suggestions are only sensitive or supporting surfaces.

Version `0.2.21` is a corrective package release for `0.2.20`; it carries the same clustered workflow ranking fixes and ensures the packaged CLI executable reports the same version as the NuGet package.

Current reports put top workflow recommendations and install blockers before the detailed inventory. Workflow clusters are shown before LLM suggestions, preferred process clusters are separated from sensitive/supporting clusters in the prompt, and the service inventory is classified by agent fit so data-access, admin/sensitive, integration, and workflow surfaces are easier to review without treating every public service as an equal first action candidate.

See:

- `docs/advanced/cli.md`
- `docs/quickstart.md`
