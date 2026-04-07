#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");
const { chromium } = require("@playwright/test");
const { openAssistantChatSurface } = require("../specs/chat-helpers.cjs");

const repoRoot = path.resolve(__dirname, "..", "..", "..");
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outputDir = path.join(repoRoot, "tests", "e2e", "artifacts", "paid-dashboard", stamp);
const serverLogPath = path.join(outputDir, "demo-server.log");
const reportPath = path.join(outputDir, "report.json");
const screenshotPath = path.join(outputDir, "dashboard.png");
const baseUrl = process.env.PAID_DASHBOARD_BASE_URL || "http://127.0.0.1:5191";
const workflowRoute = "/demo/workflows/release-dossier?reset=true";
const dashboardRoute = "/demo/dashboard";
const timeoutMs = Number.parseInt(process.env.PAID_DASHBOARD_TIMEOUT_MS || "120000", 10);
const serverReadyTimeoutMs = Number.parseInt(process.env.PAID_DASHBOARD_SERVER_TIMEOUT_MS || "180000", 10);
const paidDataDir = path.join(outputDir, "paid-data");
const paidLicenseKey = process.env.AGENTBLAZOR_LICENSE_KEY
  || process.env.AgentBlazor__LicenseKey
  || "AB-PRO-VALID-KEY-12345678";

if (!isProviderConfigured()) {
  console.error(
    "Paid dashboard run requires a live provider. Configure OpenAI or Ollama via environment variables or demo appsettings."
  );
  process.exit(1);
}

fs.mkdirSync(outputDir, { recursive: true });
fs.mkdirSync(paidDataDir, { recursive: true });

let serverProcess;

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function run() {
  const report = {
    generatedAtUtc: new Date().toISOString(),
    baseUrl,
    outputDir,
    paidDataDir,
    paidLicenseKeyPrefix: paidLicenseKey.slice(0, 7),
    prompts: [],
    approvalsClicked: 0,
    assertions: {}
  };

  serverProcess = startDemoServer();
  await waitForServerReady(`${baseUrl}${workflowRoute}`, serverReadyTimeoutMs);

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  try {
    await page.goto(`${baseUrl}${workflowRoute}`, { waitUntil: "networkidle", timeout: timeoutMs });

    const chatSurface = await openAssistantChatSurface(page, 30000);
    const interactions = { approvalsClicked: 0 };

    const prompts = [
      "apply the release dossier recovery playbook",
      "advance the next guided subsystem stage",
      "advance the next guided subsystem stage",
      "prepare the release dossier"
    ];

    for (const prompt of prompts) {
      const transcript = await runPrompt(chatSurface, prompt, interactions, timeoutMs);
      report.prompts.push({ prompt, transcript });
    }

    report.approvalsClicked = interactions.approvalsClicked;

    await page.goto(`${baseUrl}${dashboardRoute}`, { waitUntil: "networkidle", timeout: timeoutMs });
    await page.locator(".ab-dashboard").waitFor({ state: "visible", timeout: timeoutMs });

    const totalActionsText = await page.locator(".ab-dashboard__metric-value").first().textContent();
    const totalActions = Number.parseInt((totalActionsText || "0").replace(/[^\d]/g, ""), 10) || 0;
    if (totalActions <= 0) {
      throw new Error(`Expected paid dashboard Total Actions to be > 0, got '${totalActionsText}'.`);
    }

    await page.getByRole("tab", { name: "Audit Log" }).click();
    await page.locator(".ab-dashboard__table--audit").waitFor({ state: "visible", timeout: timeoutMs });

    const auditText = await page.locator(".ab-dashboard__table--audit").textContent();
    if (!/ActionApproved|ActionExecuted/i.test(auditText || "")) {
      throw new Error("Expected paid dashboard audit log to contain an approval or execution event.");
    }

    const expectedDbFiles = [
      "agentblazor-history.db",
      "agentblazor-inspector.db",
      "agentblazor-audit.db"
    ];
    const existingDbFiles = expectedDbFiles.filter((file) => fs.existsSync(path.join(paidDataDir, file)));
    if (existingDbFiles.length !== expectedDbFiles.length) {
      throw new Error(`Expected paid DB files ${expectedDbFiles.join(", ")} in ${paidDataDir}. Found ${existingDbFiles.join(", ")}.`);
    }

    report.assertions = {
      totalActions,
      auditLogContainsApprovalOrExecution: true,
      paidDbFiles: existingDbFiles
    };

    await page.screenshot({ path: screenshotPath, fullPage: true });
    fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));

    console.log(`Paid dashboard report: ${reportPath}`);
    console.log(`Paid dashboard screenshot: ${screenshotPath}`);
    console.log(`Total actions on dashboard: ${totalActions}`);
  } finally {
    await context.close().catch(() => {});
    await browser.close().catch(() => {});
    await stopDemoServer(serverProcess);
  }
}

function startDemoServer() {
  const projectPath = path.join(repoRoot, "demo", "AgentBlazor.Demo", "AgentBlazor.Demo.csproj");
  const args = ["run", "--project", projectPath, "--urls", baseUrl];
  const child = spawn("dotnet", args, {
    cwd: repoRoot,
    env: {
      ...process.env,
      AgentBlazor__LicenseKey: paidLicenseKey,
      AgentBlazor__DataDirectory: paidDataDir
    },
    stdio: ["ignore", "pipe", "pipe"]
  });

  const stream = fs.createWriteStream(serverLogPath, { flags: "a" });
  child.stdout.on("data", (chunk) => stream.write(chunk));
  child.stderr.on("data", (chunk) => stream.write(chunk));
  child.on("exit", (code, signal) => {
    stream.write(`\n[server exited] code=${code ?? "null"} signal=${signal ?? "null"}\n`);
    stream.end();
  });

  return child;
}

async function stopDemoServer(child) {
  if (!child || child.killed) {
    return;
  }

  child.kill("SIGTERM");
  const exited = await waitForExit(child, 10000);
  if (exited) {
    return;
  }

  if (process.platform === "win32") {
    await runCommand("taskkill", ["/pid", String(child.pid), "/t", "/f"]);
    return;
  }

  child.kill("SIGKILL");
}

function waitForExit(child, timeoutMs) {
  return new Promise((resolve) => {
    let settled = false;
    const done = (value) => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timer);
      resolve(value);
    };

    const timer = setTimeout(() => done(false), timeoutMs);
    child.once("exit", () => done(true));
  });
}

async function waitForServerReady(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { method: "GET" });
      if (response.ok || response.status === 404) {
        return;
      }
    } catch {
    }

    await sleep(1000);
  }

  throw new Error(`Demo server did not become ready within ${timeoutMs}ms.`);
}

async function runPrompt(chatSurface, prompt, interactions, timeoutMs) {
  const promptInput = chatSurface.getByLabel("Message input");
  await promptInput.waitFor({ state: "visible", timeout: 30000 });
  await promptInput.fill(prompt);
  await clickSend(chatSurface);
  await waitForSettled(chatSurface, timeoutMs);
  await resolveHumanInLoop(chatSurface, interactions, timeoutMs);
  await waitForSettled(chatSurface, timeoutMs);
  return extractTranscript(chatSurface);
}

async function clickSend(chatSurface) {
  const sendButton = chatSurface.locator("button[aria-label*='Send']").first();
  await sendButton.waitFor({ state: "visible", timeout: 30000 });
  await sendButton.evaluate((button) => button.click());
}

async function waitForSettled(chatSurface, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  let stableTicks = 0;

  while (Date.now() < deadline) {
    const assistantCount = await chatSurface.locator(".ab-chat-surface__item--assistant").count();
    const thinkingCount = await chatSurface.locator(".ab-chat-surface__state--thinking").count();

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

async function resolveHumanInLoop(chatSurface, interactions, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  const maxApprovals = 5;

  while (Date.now() < deadline) {
    const latestAssistantText = await getLatestAssistantText(chatSurface);
    if (/ready for review|prepared the dossier/i.test(latestAssistantText || "")) {
      return;
    }

    const approveButton = chatSurface.getByRole("button", { name: "Approve" }).first();
    if (await approveButton.isVisible().catch(() => false)) {
      if (interactions.approvalsClicked >= maxApprovals) {
        return;
      }

      await approveButton.evaluate((button) => button.click());
      interactions.approvalsClicked++;
      await waitForSettled(chatSurface, timeoutMs);
      continue;
    }

    const clarificationPanel = chatSurface.locator(".ab-chat-surface__item--clarification").first();
    if (await clarificationPanel.isVisible().catch(() => false)) {
      const input = clarificationPanel.locator("input").first();
      const submit = clarificationPanel.locator("button").first();
      await input.fill("Use default values and continue.");
      await submit.click();
      await waitForSettled(chatSurface, timeoutMs);
      continue;
    }

    return;
  }
}

async function getLatestAssistantText(chatSurface) {
  const assistantItems = chatSurface.locator(".ab-chat-surface__item--assistant .ab-chat-surface__item-text");
  const count = await assistantItems.count();
  if (count === 0) {
    return "";
  }

  return (await assistantItems.nth(count - 1).textContent())?.trim() ?? "";
}

async function extractTranscript(chatSurface) {
  return await chatSurface.evaluate((surface) => {
    return [...surface.querySelectorAll(".ab-chat-surface__item")]
      .map((item) => {
        const role = item.classList.contains("ab-chat-surface__item--user")
          ? "user"
          : item.classList.contains("ab-chat-surface__item--approval")
            ? "approval"
            : "assistant";
        const text = item.querySelector(".ab-chat-surface__item-text")?.textContent?.trim() ?? "";
        return { role, text };
      })
      .filter((entry) => entry.text.length > 0);
  });
}

function isProviderConfigured() {
  const envOpenAiKey = process.env.OPENAI_API_KEY || process.env.OpenAI__ApiKey;
  if (envOpenAiKey) {
    return true;
  }

  const envOllamaModel = process.env.OLLAMA_MODEL || process.env.Ollama__Model;
  if (envOllamaModel) {
    return true;
  }

  const appSettingsPath = path.join(repoRoot, "demo", "AgentBlazor.Demo", "appsettings.Development.json");
  if (!fs.existsSync(appSettingsPath)) {
    return false;
  }

  try {
    const appSettings = JSON.parse(fs.readFileSync(appSettingsPath, "utf8"));
    const configuredOpenAiKey = appSettings?.OpenAI?.ApiKey;
    const configuredOllamaModel = appSettings?.Ollama?.Model;
    return Boolean(configuredOpenAiKey || configuredOllamaModel);
  } catch {
    return false;
  }
}

function runCommand(command, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      stdio: "ignore",
      windowsHide: true
    });

    child.on("exit", (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(`${command} exited with code ${code}`));
    });

    child.on("error", reject);
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
