#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawn, spawnSync } = require("child_process");

function loadPlaywright() {
  try {
    return require("playwright");
  } catch {
    return require(path.resolve(__dirname, "../../tests/e2e/node_modules/playwright"));
  }
}

const { chromium } = loadPlaywright();
const { openFloatingChatWidget } = require(path.resolve(
  __dirname,
  "../../tests/e2e/specs/chat-helpers.cjs"
));

const repoRoot = path.resolve(__dirname, "../..");
const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/starter-ui-live"));
const projectPath = path.join(repoRoot, "samples", "AgentBlazor.Starter", "AgentBlazor.Starter.csproj");
const baseUrl = "http://127.0.0.1:5193";
const route = "/ops-review";
const serverLogPath = path.join(outDir, "server.log");

const openAiKey = process.env.OPENAI_API_KEY || process.env.OpenAI__ApiKey;
if (!openAiKey) {
  console.error("Set OPENAI_API_KEY or OpenAI__ApiKey before running this script.");
  process.exit(1);
}

fs.mkdirSync(outDir, { recursive: true });

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

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

function buildStarterProject() {
  run(
    "dotnet",
    [
      "build",
      projectPath,
      "-c",
      "Release",
      "--no-restore",
      "-nologo",
      "-p:RuntimeIdentifier=linux-x64"
    ],
    { cwd: repoRoot }
  );
}

async function waitForServerReady(url, timeoutMs = 60000) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { method: "GET" });
      if (response.ok) {
        return;
      }
    } catch {
      // Retry.
    }

    await sleep(1000);
  }

  throw new Error(`Timed out waiting for ${url}`);
}

function transcode(webmPath, mp4Path) {
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
}

async function waitForSettled(surface, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;
  let stableTicks = 0;

  while (Date.now() < deadline) {
    const thinkingCount = await surface.locator(".ab-chat-surface__state--thinking").count();
    const assistantCount = await surface.locator(".ab-chat-surface__item--assistant").count();

    if (assistantCount > 0 && thinkingCount === 0) {
      stableTicks++;
      if (stableTicks >= 3) {
        return;
      }
    } else {
      stableTicks = 0;
    }

    await sleep(500);
  }

  throw new Error("Chat surface did not settle before timeout.");
}

async function clickSend(surface) {
  const sendButton = surface.locator("button[aria-label*='Send']").first();
  await sendButton.waitFor({ state: "visible", timeout: 30000 });
  await sendButton.evaluate((button) => button.click());
}

async function resolveApprovalIfVisible(surface, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const approveButton = surface
      .locator(".ab-chat-surface__item--approval .ab-chat-surface__submit--approve")
      .last();

    if (await approveButton.isVisible().catch(() => false)) {
      await approveButton.click();
      return true;
    }

    const latestText = await surface.locator(".ab-chat-surface__item").last().textContent().catch(() => "");
    if (latestText && /prepared the ops review draft|approval is required/i.test(latestText)) {
      return false;
    }

    await sleep(300);
  }

  return false;
}

async function runPrompt(surface, prompt) {
  const input = surface.getByLabel("Message input").first();
  await input.waitFor({ state: "visible", timeout: 30000 });
  await input.fill(prompt);
  await sleep(500);
  await clickSend(surface);
  await waitForSettled(surface);
}

async function recordUi(browser) {
  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: outDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`${baseUrl}${route}`, { waitUntil: "networkidle", timeout: 90000 });
  await page.locator(".ops-review").waitFor({ state: "visible", timeout: 30000 });
  await page.waitForTimeout(1200);

  const widget = await openFloatingChatWidget(page, 30000);
  await page.waitForTimeout(600);

  await runPrompt(widget.widgetSurface, "Assess the current review workflow");
  await page.waitForTimeout(1200);

  await runPrompt(widget.widgetSurface, "Apply the recovery playbook");
  await page.waitForTimeout(1200);

  await runPrompt(widget.widgetSurface, "Prepare the review draft");
  await resolveApprovalIfVisible(widget.widgetSurface);
  await waitForSettled(widget.widgetSurface);
  await page.waitForTimeout(1800);

  if (await widget.minimizeButton.isVisible().catch(() => false)) {
    await widget.minimizeButton.click();
    await page.waitForTimeout(900);
    await widget.openButton.click();
    await page.waitForTimeout(1200);
  }

  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  const mp4Path = path.join(outDir, "starter-ui-live.mp4");
  transcode(webmPath, mp4Path);
  return mp4Path;
}

async function main() {
  buildStarterProject();

  const server = spawn(
    "dotnet",
    [
      "run",
      "--project",
      projectPath,
      "-c",
      "Release",
      "--no-build",
      "--no-restore",
      "--no-launch-profile",
      "-p:RuntimeIdentifier=linux-x64",
      "--urls",
      baseUrl
    ],
    {
      cwd: repoRoot,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Production",
        DOTNET_ENVIRONMENT: "Production",
        OPENAI_API_KEY: openAiKey,
        OpenAI__ApiKey: openAiKey,
        OpenAI__Model: process.env.OpenAI__Model || "gpt-4o-mini"
      },
      stdio: ["ignore", "pipe", "pipe"]
    }
  );

  let serverOutput = "";
  server.stdout.on("data", (chunk) => {
    serverOutput += chunk.toString();
  });
  server.stderr.on("data", (chunk) => {
    serverOutput += chunk.toString();
  });

  try {
    await waitForServerReady(`${baseUrl}${route}`, 120000);
    const browser = await chromium.launch({ headless: true });
    try {
      const mp4Path = await recordUi(browser);
      console.log(mp4Path);
    } finally {
      await browser.close();
    }
  } finally {
    server.kill("SIGTERM");
    await sleep(1000);
    if (!server.killed) {
      server.kill("SIGKILL");
    }
    fs.writeFileSync(serverLogPath, serverOutput);
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
