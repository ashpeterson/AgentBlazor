#!/usr/bin/env node

const fs = require("fs");
const path = require("path");

function loadPlaywright() {
  try {
    return require("playwright");
  } catch {
    return require(path.resolve(__dirname, "../../tests/e2e/node_modules/playwright"));
  }
}

const { chromium } = loadPlaywright();
const { openAssistantChatSurface } = require(path.resolve(
  __dirname,
  "../../tests/e2e/specs/chat-helpers.cjs"
));

const repoRoot = path.resolve(__dirname, "../..");
const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/structured-error-reference"));
const baseUrl = process.env.AGENTBLAZOR_DEMO_URL || "https://demo.agentblazor.com";
const prompt = "Run the structured error date range probe";

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForSurfaceText(surface, pattern, timeoutMs = 120000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    const text = await surface.innerText().catch(() => "");
    if (pattern.test(text)) {
      return text;
    }

    await sleep(750);
  }

  throw new Error(`Timed out waiting for chat surface text matching ${pattern}.`);
}

async function waitForSurfaceSettled(surface, timeoutMs = 60000) {
  const started = Date.now();
  let stableTicks = 0;
  while (Date.now() - started < timeoutMs) {
    const text = await surface.innerText().catch(() => "");
    const stillRunning =
      /Thinking\.\.\.|Sending…|Stop/i.test(text);

    if (!stillRunning) {
      stableTicks++;
      if (stableTicks >= 2) {
        return text;
      }
    } else {
      stableTicks = 0;
    }

    await sleep(750);
  }

  throw new Error("Timed out waiting for chat surface to settle.");
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1440, height: 960 },
    recordVideo: { dir: outDir, size: { width: 1440, height: 960 } }
  });

  const page = await context.newPage();
  page.setDefaultTimeout(90000);

  await page.goto(`${baseUrl.replace(/\/$/, "")}/demo/workflows/runtime-probe`, {
    waitUntil: "networkidle",
    timeout: 90000
  });

  await page.getByText("missing_argument", { exact: false }).waitFor();

  const surface = await openAssistantChatSurface(page, 60000);
  await surface.getByLabel("Message input").first().fill(prompt);
  await surface.locator("button[aria-label*='Send']").first().click();

  const transcript = await waitForSurfaceText(
    surface,
    /missing_argument|Required parameter 'startDate' is missing/i
  );

  const settledTranscript = await waitForSurfaceSettled(surface);

  const screenshotPath = path.join(outDir, "structured-error-runtime-probe.png");
  const transcriptPath = path.join(outDir, "structured-error-runtime-probe.txt");

  await page.screenshot({ path: screenshotPath, fullPage: true });
  fs.writeFileSync(transcriptPath, settledTranscript || transcript, "utf8");

  await context.close();
  await browser.close();

  const videos = fs
    .readdirSync(outDir)
    .filter((fileName) => fileName.endsWith(".webm"))
    .map((fileName) => path.join(outDir, fileName));

  console.log(JSON.stringify({
    baseUrl,
    outDir,
    screenshot: screenshotPath,
    transcript: transcriptPath,
    videos
  }, null, 2));
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
