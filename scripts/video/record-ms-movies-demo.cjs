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
const prepareScriptPath = path.join(repoRoot, "scripts", "video", "prepare-ms-movies-demo.cjs");

function run(cmd, args, options = {}) {
  const result = spawnSync(cmd, args, {
    stdio: "pipe",
    encoding: "utf8",
    ...options
  });

  if (result.status !== 0) {
    throw new Error(`${cmd} ${args.join(" ")} failed\n${result.stdout}\n${result.stderr}`);
  }

  return result.stdout.trim();
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForServerReady(url, timeoutMs = 120000) {
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
    const approvalCount = await surface.locator(".ab-chat-surface__item--approval").count();

    if ((assistantCount > 0 || approvalCount > 0) && thinkingCount === 0) {
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

async function sendPrompt(surface, prompt) {
  const input = surface.getByLabel("Message input").first();
  await input.waitFor({ state: "visible", timeout: 30000 });
  await input.fill(prompt);
  await sleep(300);
  await surface.locator("button[aria-label*='Send']").first().click();
  await waitForSettled(surface);
}

async function recordFlow(browser, baseUrl, outDir) {
  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: outDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`${baseUrl}/movies`, { waitUntil: "networkidle", timeout: 120000 });
  await page.locator(".movies-shell").waitFor({ state: "visible", timeout: 30000 });

  const widget = await openFloatingChatWidget(page, 30000);

  await sendPrompt(widget.widgetSurface, "Filter movies with furiosa in the title");
  await page.waitForTimeout(1000);
  await sendPrompt(widget.widgetSurface, "Focus Furiosa");
  await page.waitForTimeout(1000);
  await sendPrompt(widget.widgetSurface, "Prepare a sequel draft for the focused movie");

  const approveButton = widget.widgetSurface
    .locator(".ab-chat-surface__item--approval .ab-chat-surface__submit--approve")
    .last();
  if (await approveButton.isVisible().catch(() => false)) {
    await approveButton.click();
    await waitForSettled(widget.widgetSurface);
  }

  await page.waitForTimeout(1500);

  const screenshotPath = path.join(outDir, "ms-movies-agentblazor.png");
  await page.screenshot({ path: screenshotPath, fullPage: true });

  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  const mp4Path = path.join(outDir, "ms-movies-agentblazor.mp4");
  transcode(webmPath, mp4Path);

  return { mp4Path, screenshotPath };
}

async function main() {
  const openAiKey = process.env.OPENAI_API_KEY || process.env.OpenAI__ApiKey;
  if (!openAiKey) {
    console.error("Set OPENAI_API_KEY or OpenAI__ApiKey before running this script.");
    process.exit(1);
  }

  const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts", "video", "ms-movies-demo"));
  const workspaceRoot = path.join(outDir, "workspace");
  const baseUrl = "http://127.0.0.1:5205";
  const serverLogPath = path.join(outDir, "server.log");

  fs.mkdirSync(outDir, { recursive: true });
  const sampleRoot = run("node", [prepareScriptPath, workspaceRoot], { cwd: repoRoot });

  run("dotnet", [
    "restore",
    path.join(sampleRoot, "BlazorWebAppMovies.csproj"),
    "-p:RuntimeIdentifier=linux-x64"
  ], { cwd: repoRoot });

  run("dotnet", [
    "build",
    path.join(sampleRoot, "BlazorWebAppMovies.csproj"),
    "-c",
    "Release",
    "--no-restore",
    "-nologo",
    "-p:RuntimeIdentifier=linux-x64"
  ], { cwd: repoRoot });

  const server = spawn(
    "dotnet",
    [
      "run",
      "--project",
      path.join(sampleRoot, "BlazorWebAppMovies.csproj"),
      "-c",
      "Release",
      "--no-build",
      "--no-launch-profile",
      "-p:RuntimeIdentifier=linux-x64",
      "--urls",
      baseUrl
    ],
    {
      cwd: sampleRoot,
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
    await waitForServerReady(`${baseUrl}/movies`);
    const browser = await chromium.launch({ headless: true });
    try {
      const { mp4Path, screenshotPath } = await recordFlow(browser, baseUrl, outDir);
      console.log(mp4Path);
      console.log(screenshotPath);
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
