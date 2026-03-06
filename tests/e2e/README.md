# AgentBlazor E2E (Playwright)

Run locally from `tests/e2e`:

```bash
npm ci
npm run install:browsers
npm run test:e2e
npm run test:real-usability
```

This suite starts the demo app via Playwright `webServer` and validates the current dojo and component explorer surfaces.

The suite uses the real runtime path (no deterministic e2e mock client). Configure one provider before running:

1. OpenAI:
```bash
set OPENAI_API_KEY=...
set OpenAI__Model=gpt-4o-mini
```

2. Ollama:
```bash
set OLLAMA_MODEL=...
set OLLAMA_ENDPOINT=http://127.0.0.1:11434/v1
```

`test:real-usability` runs the nightly-style dojo suite on `/demo/dojo` using:
- `tests/e2e/real-usability.prompts.json`
- `tests/e2e/real-usability-baseline.json`

Evidence output is written to:
- `tests/e2e/artifacts/real-usability/<timestamp>/`
