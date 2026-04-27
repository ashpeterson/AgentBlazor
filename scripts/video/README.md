# Video Scripts

These scripts generate repeatable demo assets under `artifacts/video/`.

Current scripts:

- `record-cli-install-demo.sh`
  Records a real fresh-app CLI install session with `asciinema`.
- `render-terminal-video.cjs`
  Converts the recorded cast into an animated SVG and then into MP4.
- `record-demo-browser.cjs`
  Records short browser clips from the demo app with Playwright and transcodes them to MP4.

Current blocker for a full end-to-end capability reel:

- this Linux environment still needs a live provider configured through `OPENAI_API_KEY`, `OpenAI__ApiKey`, or a working local Ollama endpoint if the final browser videos should show real prompt/response execution instead of install and UI-only proof.
