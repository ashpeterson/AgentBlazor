#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

function loadPlaywright() {
  try {
    return require("playwright");
  } catch {
    return require(path.resolve(__dirname, "../../tests/e2e/node_modules/playwright"));
  }
}

const { chromium } = loadPlaywright();
const repoRoot = path.resolve(__dirname, "../..");

function readSnippet(relativePath, startLine, endLine) {
  const absolutePath = path.join(repoRoot, relativePath);
  const lines = fs.readFileSync(absolutePath, "utf8").split(/\r?\n/);
  return lines.slice(startLine - 1, endLine).join("\n").trimEnd();
}

function escapeHtml(value) {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function transcode(webmPath, mp4Path) {
  const result = spawnSync(
    "ffmpeg",
    [
      "-y",
      "-i",
      webmPath,
      "-c:v",
      "libx264",
      "-pix_fmt",
      "yuv420p",
      "-movflags",
      "+faststart",
      mp4Path
    ],
    { stdio: "pipe", encoding: "utf8" }
  );

  if (result.status !== 0) {
    throw new Error(`ffmpeg failed for ${webmPath}\n${result.stdout}\n${result.stderr}`);
  }
}

async function main() {
  const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/code-tour"));
  const htmlPath = path.join(outDir, "code-tour.html");
  const mp4Path = path.join(outDir, "code-tour.mp4");
  const durationMs = 11000;

  fs.mkdirSync(outDir, { recursive: true });

  const snippets = [
    {
      title: "1. Wire the runtime",
      file: "samples/AgentBlazor.Starter/Program.cs",
      detail: "Provider setup, persistence, workflow registration, and endpoint mapping.",
      code: readSnippet("samples/AgentBlazor.Starter/Program.cs", 29, 67) + "\n\n" +
        readSnippet("samples/AgentBlazor.Starter/Program.cs", 79, 85)
    },
    {
      title: "2. Add one capability class",
      file: "samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs",
      detail: "Small semantic actions, one approval boundary, one workflow surface.",
      code: readSnippet("samples/AgentBlazor.Starter/Workflows/OpsReviewCapabilities.cs", 1, 24)
    },
    {
      title: "3. Mount the chat UI on the page",
      file: "samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor",
      detail: "Keep the page small. Show status. Let the widget drive the same workflow.",
      code: readSnippet("samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor", 1, 34) + "\n...\n" +
        readSnippet("samples/AgentBlazor.Starter/Components/Pages/OpsReview.razor", 96, 102)
    }
  ];

  const cards = snippets
    .map(
      (snippet, index) => `
        <section class="card" style="--delay:${index * 3.2}s">
          <div class="card__head">
            <div>
              <p class="eyebrow">${snippet.title}</p>
              <h2>${snippet.file}</h2>
            </div>
            <span class="pill">${snippet.detail}</span>
          </div>
          <pre><code>${escapeHtml(snippet.code)}</code></pre>
        </section>`
    )
    .join("\n");

  fs.writeFileSync(
    htmlPath,
    `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>AgentBlazor Code Tour</title>
  <style>
    :root {
      color-scheme: dark;
      font-family: "JetBrains Mono", "Cascadia Code", "Consolas", monospace;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      background:
        radial-gradient(circle at top right, rgba(255, 106, 61, 0.14), transparent 26%),
        radial-gradient(circle at bottom left, rgba(110, 168, 254, 0.16), transparent 34%),
        #07111b;
      color: #edf3ff;
      overflow: hidden;
    }
    main {
      width: 1600px;
      height: 980px;
      margin: 0 auto;
      padding: 48px;
      display: grid;
      grid-template-rows: auto 1fr;
      gap: 22px;
    }
    header {
      display: flex;
      align-items: end;
      justify-content: space-between;
      gap: 24px;
    }
    header h1 {
      margin: 0;
      font: 700 52px/1.02 "Segoe UI", sans-serif;
      letter-spacing: -0.04em;
    }
    header p {
      margin: 8px 0 0;
      max-width: 54rem;
      color: rgba(225, 235, 248, 0.78);
      font: 500 22px/1.45 "Segoe UI", sans-serif;
    }
    .meta {
      padding: 12px 18px;
      border-radius: 999px;
      border: 1px solid rgba(255, 255, 255, 0.12);
      background: rgba(255, 255, 255, 0.04);
      color: rgba(230, 237, 251, 0.82);
      font-size: 17px;
      white-space: nowrap;
    }
    .grid {
      display: grid;
      gap: 18px;
      align-content: start;
    }
    .card {
      opacity: 0.28;
      transform: translateY(18px) scale(0.985);
      animation: focus 9.6s ease-in-out infinite;
      animation-delay: var(--delay);
      padding: 22px 24px;
      border-radius: 22px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: rgba(7, 14, 24, 0.82);
      box-shadow: 0 20px 52px rgba(0, 0, 0, 0.22);
    }
    .card__head {
      display: flex;
      align-items: start;
      justify-content: space-between;
      gap: 18px;
      margin-bottom: 16px;
    }
    .eyebrow {
      margin: 0 0 6px;
      color: rgba(173, 197, 255, 0.72);
      font: 700 12px/1.2 "Segoe UI", sans-serif;
      letter-spacing: 0.14em;
      text-transform: uppercase;
    }
    h2 {
      margin: 0;
      font: 700 24px/1.2 "Segoe UI", sans-serif;
      color: #f4f8ff;
    }
    .pill {
      max-width: 32rem;
      padding: 10px 14px;
      border-radius: 14px;
      background: rgba(49, 99, 255, 0.12);
      border: 1px solid rgba(49, 99, 255, 0.28);
      color: rgba(233, 240, 255, 0.86);
      font: 500 16px/1.4 "Segoe UI", sans-serif;
    }
    pre {
      margin: 0;
      overflow: hidden;
      padding: 18px 20px;
      border-radius: 16px;
      border: 1px solid rgba(255, 255, 255, 0.06);
      background: rgba(2, 7, 14, 0.96);
      color: #dbe8ff;
      font-size: 17px;
      line-height: 1.42;
      white-space: pre-wrap;
    }
    @keyframes focus {
      0%, 100% {
        opacity: 0.28;
        transform: translateY(18px) scale(0.985);
        border-color: rgba(255, 255, 255, 0.08);
      }
      10%, 28% {
        opacity: 1;
        transform: translateY(0) scale(1);
        border-color: rgba(110, 168, 254, 0.28);
      }
      38%, 100% {
        opacity: 0.28;
        transform: translateY(18px) scale(0.985);
        border-color: rgba(255, 255, 255, 0.08);
      }
    }
  </style>
</head>
<body>
  <main>
    <header>
      <div>
        <h1>CLI first. Then the smallest code shape.</h1>
        <p>Show the runtime wiring, one capability class, then the page that hosts the widget. No framework tour. Just the parts a developer needs to copy.</p>
      </div>
      <div class="meta">Starter sample code tour</div>
    </header>
    <div class="grid">${cards}</div>
  </main>
</body>
</html>`
  );

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: outDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`file://${htmlPath}`);
  await page.waitForTimeout(durationMs);

  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  await browser.close();

  transcode(webmPath, mp4Path);
  console.log(mp4Path);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
