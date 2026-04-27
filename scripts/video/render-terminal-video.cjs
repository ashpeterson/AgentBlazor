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

function run(cmd, args, options = {}) {
  const result = spawnSync(cmd, args, {
    stdio: "pipe",
    encoding: "utf8",
    ...options
  });

  if (result.status !== 0) {
    throw new Error(`${cmd} ${args.join(" ")} failed\n${result.stdout}\n${result.stderr}`);
  }

  return result;
}

function readDurationMs(castPath) {
  const lines = fs.readFileSync(castPath, "utf8").trim().split(/\r?\n/);
  let lastTimestamp = 0;

  for (const line of lines.slice(1)) {
    const entry = JSON.parse(line);
    if (Array.isArray(entry) && typeof entry[0] === "number") {
      lastTimestamp = Math.max(lastTimestamp, entry[0]);
    }
  }

  return Math.ceil((lastTimestamp + 1.2) * 1000);
}

async function main() {
  const castPath = path.resolve(process.argv[2] || "artifacts/video/cli-install/cli-install.cast");
  const outDir = path.resolve(process.argv[3] || path.dirname(castPath));
  const svgPath = path.join(outDir, "cli-install.svg");
  const htmlPath = path.join(outDir, "cli-install.html");
  const mp4Path = path.join(outDir, "cli-install.mp4");

  fs.mkdirSync(outDir, { recursive: true });

  run("npx", [
    "-y",
    "svg-term-cli",
    "--in",
    castPath,
    "--out",
    svgPath,
    "--window",
    "--padding",
    "24",
    "--width",
    "100",
    "--height",
    "30"
  ]);

  fs.writeFileSync(
    htmlPath,
    `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>AgentBlazor CLI Install Demo</title>
  <style>
    html, body {
      margin: 0;
      width: 100%;
      height: 100%;
      background: #050913;
      display: grid;
      place-items: center;
      overflow: hidden;
    }
    img {
      width: 1500px;
      max-width: 96vw;
      height: auto;
      display: block;
      box-shadow: 0 24px 80px rgba(0, 0, 0, 0.45);
      border-radius: 18px;
    }
  </style>
</head>
<body>
  <img src="./${path.basename(svgPath)}" alt="AgentBlazor CLI install demo" />
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
  await page.waitForTimeout(readDurationMs(castPath));

  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  await browser.close();

  run("ffmpeg", [
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
  ]);

  console.log(`Rendered ${mp4Path}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
