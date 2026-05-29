# AgentBlazor CLI

The CLI is the advanced setup path for existing Blazor apps.

Read-only analysis:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

If no OpenAI key is configured and the terminal is interactive, `analyze` prompts for one and uses it for that run only. For repeat runs or CI, set `OPENAI_API_KEY` and optionally `AGENTBLAZOR_ANALYZE_MODEL`. Pass `--static-only` to skip the LLM call.

Version `0.2.5` and later include a static source-file fallback for Windows/Roslyn MSBuildWorkspace load failures. Current reports filter helper noise, show AgentBlazor action adoption, and include approval guidance for mutating workflow suggestions.

See:

- `docs/advanced/cli.md`
- `docs/quickstart.md`
