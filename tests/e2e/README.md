# AgentBlazor E2E (Playwright)

Run locally from `tests/e2e`:

```bash
npm ci
npm run install:browsers
npm run test:e2e
npm run test:e2e:headed
npm run test:e2e:ui
npm run test:real-usability
npm run test:paid-dashboard
```

This suite starts the demo app via Playwright `webServer` and validates the current workflow and component explorer surfaces.

Use `npm run test:e2e:headed` when you want to watch the browser, and `npm run test:e2e:ui` when you want Playwright's interactive runner.

By default the suite starts a fresh demo app instance so it does not silently attach to a stale local build. If you intentionally want to reuse an already running server, set:

```bash
set PLAYWRIGHT_REUSE_SERVER=1
```

The suite uses the real runtime path (no deterministic e2e mock client). Configure one provider before running:

1. OpenAI:
```bash
set OPENAI_API_KEY=...
set OpenAI__Model=gpt-5.4-mini
```

2. Ollama:
```bash
set OLLAMA_MODEL=...
set OLLAMA_ENDPOINT=http://127.0.0.1:11434/v1
```

`test:real-usability` runs the nightly-style orchestration suite using:
- `tests/e2e/real-usability.prompts.json`
- `tests/e2e/real-usability-baseline.json`

Evidence output is written to:
- `tests/e2e/artifacts/real-usability/<timestamp>/`

`test:paid-dashboard` starts the demo with a temporary paid license/data directory, seeds paid data through the release-dossier workflow, and verifies the Pro Dashboard plus SQLite paid stores.

Evidence output is written to:
- `tests/e2e/artifacts/paid-dashboard/<timestamp>/`

Failure artifacts for `test:e2e` are written to:
- `tests/e2e/test-results/`
- `tests/e2e/playwright-report/`
