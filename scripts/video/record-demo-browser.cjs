#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

function loadPlaywright() {
  try {
    return require("playwright");
  } catch {
    return require(path.resolve(__dirname, "../../tests/e2e/node_modules/playwright"));
  }
}

const { chromium } = loadPlaywright();

const repoRoot = path.resolve(__dirname, "../..");
const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/browser"));
const baseUrl = "http://127.0.0.1:5188";

const scenarios = [
  {
    name: "response-workflow",
    route: "/demo/workflows/response-orchestration?reset=true",
    steps: async (page) => {
      await page.waitForTimeout(1400);
      await page.locator(".workflow-board__journey-card").first().scrollIntoViewIfNeeded();
      await page.waitForTimeout(800);
      await page.getByRole("button", { name: "Reset", exact: true }).click();
      await page.waitForTimeout(1000);
    }
  },
  {
    name: "components-explorer",
    route: "/demo/components/file-upload",
    steps: async (page) => {
      await page.waitForTimeout(1200);
      await page.locator("#prompts").scrollIntoViewIfNeeded();
      await page.waitForTimeout(1000);
      await page.getByRole("button", { name: "Sync Remote Handoff", exact: true }).click();
      await page.waitForTimeout(900);
      await page.getByRole("button", { name: "Validate Tokens", exact: true }).click();
      await page.waitForTimeout(900);
    }
  },
  {
    name: "pro-dashboard",
    route: "/demo/dashboard",
    steps: async (page) => {
      await page.waitForTimeout(1400);
      await page.getByRole("tab", { name: "Audit Log", exact: true }).click();
      await page.waitForTimeout(1000);
      await page.getByRole("tab", { name: "Patterns", exact: true }).click();
      await page.waitForTimeout(1000);
    }
  }
];

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForServer(url, timeoutMs) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // Ignore retries while the server starts.
    }

    await sleep(500);
  }

  throw new Error(`Timed out waiting for ${url}`);
}

async function recordScenario(browser, scenario) {
  const scenarioDir = path.join(outDir, scenario.name);
  fs.mkdirSync(scenarioDir, { recursive: true });

  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: scenarioDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`${baseUrl}${scenario.route}`, { waitUntil: "networkidle" });
  await scenario.steps(page);
  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  return { scenarioDir, webmPath };
}

function transcodeToMp4(webmPath, mp4Path) {
  const { spawnSync } = require("child_process");
  const result = spawnSync("ffmpeg", [
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
  ], { stdio: "pipe", encoding: "utf8" });

  if (result.status !== 0) {
    throw new Error(`ffmpeg failed for ${webmPath}\n${result.stdout}\n${result.stderr}`);
  }
}

async function main() {
  fs.mkdirSync(outDir, { recursive: true });

  const server = spawn("dotnet", [
    "run",
    "--project",
    path.join(repoRoot, "demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj"),
    "-c",
    "Release",
    "--no-build",
    "--no-restore",
    "--no-launch-profile",
    "--urls",
    baseUrl
  ], {
    cwd: repoRoot,
    stdio: "pipe",
    env: {
      ...process.env,
      AGENTBLAZOR_LICENSE_KEY: process.env.AGENTBLAZOR_LICENSE_KEY || "AB-PRO-VALID-KEY-12345678",
      AGENTBLAZOR_DATA_DIRECTORY: path.join(outDir, "paid-data")
    }
  });

  let serverOutput = "";
  server.stdout.on("data", (chunk) => {
    serverOutput += chunk.toString();
  });
  server.stderr.on("data", (chunk) => {
    serverOutput += chunk.toString();
  });

  try {
    await waitForServer(baseUrl, 60000);
    const browser = await chromium.launch({ headless: true });

    for (const scenario of scenarios) {
      const { scenarioDir, webmPath } = await recordScenario(browser, scenario);
      transcodeToMp4(webmPath, path.join(scenarioDir, `${scenario.name}.mp4`));
    }

    await browser.close();
  } finally {
    server.kill("SIGTERM");
    await sleep(1000);
    if (!server.killed) {
      server.kill("SIGKILL");
    }
    fs.writeFileSync(path.join(outDir, "server.log"), serverOutput);
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
