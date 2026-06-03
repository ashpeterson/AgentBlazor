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

See:

- `docs/advanced/cli.md`
- `docs/quickstart.md`
