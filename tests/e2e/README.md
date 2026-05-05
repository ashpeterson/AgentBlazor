# AgentBlazor E2E (Playwright)

Last updated: 2026-04-20

Run locally from `tests/e2e`:

```bash
npm ci
npm run install:browsers
npm run test:e2e
npm run test:e2e:headed
npm run test:e2e:ui
npm run test:real-usability
npm run test:paid-dashboard
npm run test:external-chat-widget
npm run test:external-chat-surfaces
npm run test:hosted-wasm-remote-chat
```

This suite starts the demo app via Playwright `webServer` and validates the current workflow and component explorer surfaces.

The component explorer suite includes floating `AgentChatWidget` coverage for opening the widget, entering a prompt, minimizing with the visible control, reopening, and minimizing with Escape. Browser execution requires the Playwright browser system libraries to be installed on the machine.

`npm run test:external-chat-widget` and `npm run test:external-chat-surfaces` run the same hardened external-app validation. The runner clones a real Blazor app, installs AgentBlazor from either a local package build or a published package feed, runs `init`, `scaffold`, `doctor`, and `validate`, then launches the cloned app and uses Playwright to submit prompts through the installed floating widget and any requested embedded chat surfaces. By default it uses `damienbod/BlazorSecurityNet10`; override with `AGENTBLAZOR_EXTERNAL_REPO`, `AGENTBLAZOR_EXTERNAL_REF`, `AGENTBLAZOR_EXTERNAL_PROJECT`, and `AGENTBLAZOR_EXTERNAL_APP_PATH`.

Set `AGENTBLAZOR_PACKAGE_SOURCE_MODE=published` to install `AgentBlazor` and `AgentBlazor.Cli` from NuGet.org instead of the local source build. Leave `AGENTBLAZOR_PACKAGE_SOURCE_MODE` unset or set it to `local` for source-package validation.

`npm run test:hosted-wasm-remote-chat` creates a fresh hosted WebAssembly Blazor Web App, installs `AgentBlazor`, `AgentBlazor.Client`, and `AgentBlazor.Cli`, maps the server `MapAgentBlazorRemoteChat()` endpoint, registers WebAssembly `HttpClient`, and uses Playwright to submit prompts through `AgentRemoteChatWidget`, `AgentRemoteChatSurface`, `AgentRemoteChatPanel`, and `AgentRemoteChatBar`. It supports the same `AGENTBLAZOR_PACKAGE_SOURCE_MODE=local|published` package-source modes as the external runner, with `published` using NuGet.org.

Set `AGENTBLAZOR_EXTERNAL_TEMPLATE=blazor` with `AGENTBLAZOR_EXTERNAL_PROJECT=AgentBlazorExternalTemplate.csproj` to generate and test a fresh `dotnet new blazor` app instead of cloning a repository. The `external-chat-widget-matrix` workflow runs a fresh no-provider target, a fresh deterministic-provider target, `neozhu/CleanArchitectureWithBlazorServer` on a public route for the CLI-installed widget, and the same CleanArchitecture app on an authenticated users route for widget, surface, panel, and bar coverage. This keeps the public-route case honest while still testing every chat surface in a production-style Blazor layout.

Set `AGENTBLAZOR_EXTERNAL_PROVIDER_MODE=deterministic` to inject a local `IAgentRuntimeAdapter` into the external app after scaffold approval. This mode validates the provider-backed chat path by submitting prompts, consuming streaming runtime events, and asserting that the deterministic assistant response renders in each requested surface. Leave it unset or set it to `none` to verify the no-provider guidance path.

Set `AGENTBLAZOR_EXTERNAL_CHAT_SURFACES=widget,surface,panel,bar` to choose which surfaces to validate. `widget` tests the CLI-installed floating assistant on the configured app route. `surface`, `panel`, and `bar` inject a temporary routed harness into the external app after scaffold approval, then test `AgentChatSurface`, `AgentChatPanel`, and `AgentChatBar` in the production app process. Use `AGENTBLAZOR_EXTERNAL_CHAT_SURFACES_PATH` when the target app has route-based authorization; the harness route must be reachable by the current browser session, so public-route jobs should use a public path and protected-route jobs should configure login.

Override the production prompts with `AGENTBLAZOR_EXTERNAL_PROMPT`, `AGENTBLAZOR_EXTERNAL_SURFACE_PROMPT`, `AGENTBLAZOR_EXTERNAL_PANEL_PROMPT`, and `AGENTBLAZOR_EXTERNAL_BAR_PROMPT`. Separate multiple prompts with `||`. The default set submits three production-style prompts per surface, covering explanation, administrator checks, route/risk summaries, operator checklists, validation checks, runbooks, rollback plans, audit evidence, status updates, handoff notes, and user-intent summaries so real production apps exercise more than a generic chat echo.

The external chat surface runner now verifies scaffold idempotency, submitted prompt rendering, no-provider guidance or deterministic provider response rendering, computed open/minimized widget CSS, repeated open/minimize cycles, Escape minimization, reload/reopen behavior, embedded surface prompt submission, side-panel prompt submission, inline chat-bar prompt submission, and AgentBlazor asset request failures. Artifacts include per-state screenshots, browser console diagnostics, failed request diagnostics, server logs, `report.json`, and a human-readable `report.md` that summarizes target details, assertion status, failed requests, widget states, embedded surface states, and failure stack traces.

For external apps that require authentication before the main layout is reachable, set `AGENTBLAZOR_EXTERNAL_LOGIN_PATH`, `AGENTBLAZOR_EXTERNAL_LOGIN_USERNAME`, and `AGENTBLAZOR_EXTERNAL_LOGIN_PASSWORD`. The runner signs in before opening the installed floating widget and before visiting the injected surface harness. Set `AGENTBLAZOR_EXTERNAL_EXPECTED_TEXT` to require a protected-page marker before chat assertions begin; the matrix uses this for the CleanArchitecture `/identity/users` authenticated route.

Current release context: `0.1.0-preview.11` is the current public prerelease. Local e2e runs still require a configured OpenAI, Azure OpenAI, or Ollama provider.

The real-usability runner requires an explicit live provider from environment variables. It intentionally does not treat demo `appsettings.json` sample values as proof that CI can reach a provider, because empty GitHub secrets or missing Ollama services otherwise produce misleading no-provider transcripts instead of a clear preflight failure.

Use `npm run test:e2e:headed` when you want to watch the browser, and `npm run test:e2e:ui` when you want Playwright's interactive runner.

By default the suite starts a fresh demo app instance so it does not silently attach to a stale local build. If you intentionally want to reuse an already running server, set:

```bash
set PLAYWRIGHT_REUSE_SERVER=1
```

The suite uses the real runtime path (no deterministic e2e mock client). Configure one provider before running:

1. OpenAI:
```bash
set OPENAI_API_KEY=...
set OpenAI__Model=gpt-4o-mini
```

For GitHub Actions:
```bash
gh secret set OPENAI_API_KEY --repo ashpeterson/AgentBlazor
gh variable set OPENAI_MODEL --repo ashpeterson/AgentBlazor --body gpt-4o-mini
```

`OPENAI_API_KEY` must contain the API key value, not the model name. OpenAI keys normally start with `sk-`.

2. Azure OpenAI:
```bash
set AzureOpenAI__Endpoint=https://<resource>.openai.azure.com
set AzureOpenAI__DeploymentName=<deployment-name>
set AzureOpenAI__ApiKey=...
```

3. Ollama:
```bash
set OLLAMA_MODEL=...
set OLLAMA_ENDPOINT=http://127.0.0.1:11434/v1
```

`test:real-usability` runs the nightly-style support-inbox launch suite using:
- `tests/e2e/real-usability.prompts.json`
- `tests/e2e/real-usability-baseline.json`

Evidence output is written to:
- `tests/e2e/artifacts/real-usability/<timestamp>/`

`test:paid-dashboard` starts the demo with a temporary paid license/data directory, seeds paid data through the release-dossier workflow, and verifies the Pro Dashboard plus SQLite paid stores.

Evidence output is written to:
- `tests/e2e/artifacts/paid-dashboard/<timestamp>/`

External chat surface evidence output is written to:
- `tests/e2e/artifacts/external-chat-widget/<timestamp>/`

Failure artifacts for `test:e2e` are written to:
- `tests/e2e/test-results/`
- `tests/e2e/playwright-report/`
