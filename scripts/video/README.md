# Video Scripts

These scripts generate repeatable demo assets under `artifacts/video/`.

Current scripts:

- `record-cli-install-demo.sh`
  Records a real fresh-app CLI install session with `asciinema`.
- `render-terminal-video.cjs`
  Converts the recorded cast into an animated SVG and then into MP4.
- `record-code-tour.cjs`
  Records a compact code walkthrough using the starter sample: runtime wiring, one capability class, and the host page.
- `record-demo-browser.cjs`
  Records short browser clips from the demo app with Playwright and transcodes them to MP4.
- `record-starter-ui-live.cjs`
  Runs the starter sample with a live provider and records the widget, prompts, approval, and result state.
- `record-capability-reel.cjs`
  Produces one stitched reel: CLI install, starter code tour, then the official Microsoft movie app chat/result clip.
- `prepare-ms-movies-demo.cjs`
  Clones the official Microsoft Blazor movies sample, switches it to SQLite, overlays AgentBlazor integration files, and creates a runnable scratch workspace.
- `record-ms-movies-demo.cjs`
  Runs the prepared official Microsoft movie sample, drives the chat widget with real prompts, and records the resulting page-state changes.
- `record-support-inbox-demo.cjs`
  Runs the demo app support inbox route, drives the embedded assistant through queue focus, escalation, draft, and approval, and records the visible page-state changes.
- `record-structured-error-reference.cjs`
  Records the hosted runtime-probe structured-error path and captures a screenshot/transcript showing a recoverable `missing_argument` response.

Useful commands:

```bash
bash scripts/video/record-cli-install-demo.sh
node scripts/video/render-terminal-video.cjs
node scripts/video/record-code-tour.cjs
OPENAI_API_KEY=... node scripts/video/record-starter-ui-live.cjs
OPENAI_API_KEY=... node scripts/video/record-capability-reel.cjs
node scripts/video/prepare-ms-movies-demo.cjs
OPENAI_API_KEY=... node scripts/video/record-ms-movies-demo.cjs
OPENAI_API_KEY=... node scripts/video/record-support-inbox-demo.cjs
node scripts/video/record-structured-error-reference.cjs
```

Notes:

- `record-cli-install-demo.sh` now preserves the generated scaffolded app under `generated-project/` inside the chosen output folder.
- Live UI capture still requires a working provider through `OPENAI_API_KEY`, `OpenAI__ApiKey`, or a local Ollama endpoint.
- The Microsoft sample recorder uses the official `dotnet/blazor-samples` `10.0/BlazorWebAppMovies` sample as its host app.
- The main reel now treats the starter sample as the code reference and the Microsoft movie app as the filmed real-app proof.
