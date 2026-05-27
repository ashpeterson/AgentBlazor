# AgentBlazor CLI

The CLI is the advanced setup path for existing Blazor apps.

Read-only analysis:

```bash
agentblazor analyze ./MySolution.slnx --host MyBlazorApp
```

Set `OPENAI_API_KEY` and optionally `AGENTBLAZOR_ANALYZE_MODEL` for LLM workflow suggestions, or pass `--static-only` to skip the LLM call.

See:

- `docs/advanced/cli.md`
- `docs/quickstart.md`
