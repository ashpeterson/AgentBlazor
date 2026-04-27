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
const { openAssistantChatSurface } = require(path.resolve(
  __dirname,
  "../../tests/e2e/specs/chat-helpers.cjs"
));

const repoRoot = path.resolve(__dirname, "../..");
const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/live-provider"));
const baseUrl = "http://127.0.0.1:5192";
const paidDataDir = path.join(outDir, "paid-data");
const serverLogPath = path.join(outDir, "server.log");
const workflowRoute = "/demo/workflows/runtime-probe";
const dashboardRoute = "/demo/dashboard";
const projectPath = path.join(repoRoot, "demo", "AgentBlazor.Demo", "AgentBlazor.Demo.csproj");

const openAiKey = process.env.OPENAI_API_KEY || process.env.OpenAI__ApiKey;
if (!openAiKey) {
  console.error("Set OPENAI_API_KEY or OpenAI__ApiKey before running this script.");
  process.exit(1);
}

fs.mkdirSync(outDir, { recursive: true });
fs.mkdirSync(paidDataDir, { recursive: true });

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForServerReady(url, timeoutMs = 60000) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { method: "GET" });
      if (response.ok || response.status === 404) {
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

function buildDemoProject() {
  const result = spawnSync(
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
    {
      cwd: repoRoot,
      stdio: "pipe",
      encoding: "utf8"
    }
  );

  if (result.status !== 0) {
    throw new Error(`dotnet build failed\n${result.stdout}\n${result.stderr}`);
  }
}

async function waitForSettled(chatSurface, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;
  let stableTicks = 0;

  while (Date.now() < deadline) {
    const thinkingCount = await chatSurface.locator(".ab-chat-surface__state--thinking").count();
    const assistantCount = await chatSurface.locator(".ab-chat-surface__item--assistant").count();

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

async function clickSend(chatSurface) {
  const sendButton = chatSurface.locator("button[aria-label*='Send']").first();
  await sendButton.waitFor({ state: "visible", timeout: 30000 });
  await sendButton.evaluate((button) => button.click());
}

async function getLatestAssistantText(chatSurface) {
  const items = await chatSurface.locator(".ab-chat-surface__item").evaluateAll((nodes) =>
    nodes.map((node) => ({
      role: node.classList.contains("ab-chat-surface__item--assistant")
        ? "assistant"
        : node.classList.contains("ab-chat-surface__item--approval")
          ? "approval"
          : node.classList.contains("ab-chat-surface__item--user")
            ? "user"
            : "other",
      text: node.textContent || ""
    }))
  );

  const assistantItems = items.filter((item) => item.role === "assistant");
  return assistantItems.length > 0 ? assistantItems[assistantItems.length - 1].text : "";
}

async function resolveApprovals(chatSurface, timeoutMs = 90000) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const latestAssistantText = await getLatestAssistantText(chatSurface);
    if (/ready for review|prepared the dossier/i.test(latestAssistantText || "")) {
      return;
    }

    const approveButton = chatSurface
      .locator(".ab-chat-surface__item--approval .ab-chat-surface__submit--approve")
      .last();

    if (await approveButton.isVisible().catch(() => false)) {
      await approveButton.click();
      await sleep(700);
      await waitForSettled(chatSurface, timeoutMs);
      return;
    }

    await sleep(400);
  }
}

async function runPrompt(chatSurface, prompt, timeoutMs = 90000) {
  const promptInput = chatSurface.getByLabel("Message input").first();
  await promptInput.waitFor({ state: "visible", timeout: 30000 });
  await promptInput.fill(prompt);
  await sleep(500);
  await clickSend(chatSurface);
  await waitForSettled(chatSurface, timeoutMs);
  await resolveApprovals(chatSurface, timeoutMs);
  await waitForSettled(chatSurface, timeoutMs);
}

async function recordWorkflow(browser) {
  const scenarioDir = path.join(outDir, "workflow-live");
  fs.mkdirSync(scenarioDir, { recursive: true });

  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: scenarioDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`${baseUrl}${workflowRoute}`, { waitUntil: "networkidle", timeout: 90000 });
  const chatSurface = await waitForAssistantSurface(page);

  const prompts = ["run the runtime approval probe"];

  for (const prompt of prompts) {
    await runPrompt(chatSurface, prompt);
    await sleep(900);
  }

  await page.waitForTimeout(1200);
  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  const mp4Path = path.join(scenarioDir, "workflow-live.mp4");
  transcode(webmPath, mp4Path);
  return mp4Path;
}

async function waitForAssistantSurface(page) {
  const assistantPane = page.locator(".demo-shell__assistant-pane").first();
  if (await assistantPane.count()) {
    await assistantPane.waitFor({ state: "visible", timeout: 30000 });
  }

  const loadingAssistant = page.getByText("Loading assistant…").first();
  if (await loadingAssistant.isVisible().catch(() => false)) {
    await loadingAssistant.waitFor({ state: "hidden", timeout: 30000 }).catch(() => {});
  }

  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      return await openAssistantChatSurface(page, 20000);
    } catch (error) {
      if (attempt === 2) {
        throw error;
      }

      await page.waitForTimeout(2000);
    }
  }

  throw new Error("Assistant surface did not become interactive.");
}

async function recordDashboard(browser) {
  const scenarioDir = path.join(outDir, "dashboard-live");
  fs.mkdirSync(scenarioDir, { recursive: true });

  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: scenarioDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`${baseUrl}${dashboardRoute}`, { waitUntil: "networkidle", timeout: 90000 });
  await page.locator(".ab-dashboard").waitFor({ state: "visible", timeout: 30000 });
  await page.waitForTimeout(1200);
  await page.getByRole("tab", { name: "Audit Log", exact: true }).click();
  await page.waitForTimeout(1000);
  await page.getByRole("tab", { name: "Patterns", exact: true }).click();
  await page.waitForTimeout(1000);
  await page.getByRole("button", { name: /open agent chat/i }).first().click().catch(() => {});
  await page.waitForTimeout(1000);

  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  const mp4Path = path.join(scenarioDir, "dashboard-live.mp4");
  transcode(webmPath, mp4Path);
  return mp4Path;
}

async function main() {
  buildDemoProject();

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
        OpenAI__ApiKey: openAiKey,
        OpenAI__Model: process.env.OpenAI__Model || "gpt-5.4-mini",
        AgentBlazor__LicenseKey: process.env.AGENTBLAZOR_LICENSE_KEY || "AB-PRO-VALID-KEY-12345678",
        AgentBlazor__DataDirectory: paidDataDir
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
    await waitForServerReady(`${baseUrl}${workflowRoute}`, 120000);
    const browser = await chromium.launch({ headless: true });
    try {
      const workflowVideo = await recordWorkflow(browser);
      const dashboardVideo = await recordDashboard(browser);
      console.log(workflowVideo);
      console.log(dashboardVideo);
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
