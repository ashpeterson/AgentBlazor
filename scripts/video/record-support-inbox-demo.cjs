#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const os = require("os");
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
const outDir = path.resolve(process.argv[2] || path.join(repoRoot, "artifacts/video/support-inbox-demo"));
const baseUrl = "http://127.0.0.1:5194";
const serverLogPath = path.join(outDir, "server.log");

fs.mkdirSync(outDir, { recursive: true });

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

function prepareWorkspace() {
  const workspaceRoot = fs.mkdtempSync(path.join(os.tmpdir(), "agentblazor-support-video-"));
  const copyTargets = [
    "demo",
    "src",
    "Directory.Build.props",
    "Directory.Packages.props",
    "NuGet.Config",
    "global.json"
  ];

  for (const target of copyTargets) {
    const sourcePath = path.join(repoRoot, target);
    const destinationPath = path.join(workspaceRoot, target);

    if (fs.statSync(sourcePath).isDirectory()) {
      fs.cpSync(sourcePath, destinationPath, { recursive: true });
    } else {
      fs.copyFileSync(sourcePath, destinationPath);
    }
  }

  const projectPath = path.join(workspaceRoot, "demo", "AgentBlazor.Demo", "AgentBlazor.Demo.csproj");
  const programPath = path.join(workspaceRoot, "demo", "AgentBlazor.Demo", "Program.cs");
  const adapterDir = path.join(workspaceRoot, "demo", "AgentBlazor.Demo", "AgentBlazorVideoProvider");
  const adapterPath = path.join(adapterDir, "SupportInboxDeterministicRuntimeAdapter.cs");

  fs.mkdirSync(adapterDir, { recursive: true });
  fs.writeFileSync(adapterPath, deterministicRuntimeAdapterSource(), "utf8");

  let program = fs.readFileSync(programPath, "utf8");
  program = program.replace(
    "builder.Services.AddScoped<SupportInboxWorkflowService>();",
    "builder.Services.AddSingleton<SupportInboxWorkflowService>();");

  const registrationPattern = /(builder\.Services\.AddAgentBlazor\(\s*options\s*=>\s*\r?\n\s*\{\r?\n)/m;
  if (!registrationPattern.test(program)) {
    throw new Error(`Unable to locate AddAgentBlazor registration in ${programPath}.`);
  }

  program = program.replace(
    registrationPattern,
    `$1    options.UseRuntimeAdapter<AgentBlazorVideoProvider.SupportInboxDeterministicRuntimeAdapter>();\n`);

  fs.writeFileSync(programPath, program, "utf8");

  return {
    workspaceRoot,
    projectPath
  };
}

function deterministicRuntimeAdapterSource() {
  return `using System.Runtime.CompilerServices;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Demo.Services;

namespace AgentBlazorVideoProvider;

internal sealed class SupportInboxDeterministicRuntimeAdapter(
    SupportInboxWorkflowService workflow) : IAgentRuntimeAdapter
{
    private const string AgentName = "Support Inbox Agent";

    public bool SupportsStreaming => true;

    public bool SupportsReconnect => false;

    public bool SupportsCancellation => false;

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyPrompt(request.UserMessage));
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runId = $"support-video-{Guid.NewGuid():N}";
        var sequence = 0L;
        var response = ApplyPrompt(request.UserMessage);

        yield return new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.RunStarted,
            RunId = runId,
            Sequence = ++sequence,
            AgentName = AgentName
        };

        yield return new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.TextMessageStart,
            RunId = runId,
            Sequence = ++sequence,
            AgentName = AgentName
        };

        await Task.Delay(25, cancellationToken);

        yield return new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.TextMessageContent,
            RunId = runId,
            Sequence = ++sequence,
            AgentName = AgentName,
            TextDelta = response.ResponseText
        };

        yield return new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.TextMessageEnd,
            RunId = runId,
            Sequence = ++sequence,
            AgentName = AgentName
        };

        yield return new AgentTurnStreamEvent
        {
            Kind = AgentTurnStreamEventKind.RunFinished,
            RunId = runId,
            Sequence = ++sequence,
            AgentName = AgentName,
            Response = response
        };
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    public Task<bool> StopRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private AgentTurnResponse ApplyPrompt(string prompt)
    {
        var normalized = (prompt ?? string.Empty).Trim().ToLowerInvariant();
        string responseText;

        if (normalized.Contains("show open tickets"))
        {
            responseText = workflow.FocusOpenTickets(7);
        }
        else if (normalized.Contains("explain why"))
        {
            responseText = workflow.ExplainFocusedTickets();
        }
        else if (normalized.Contains("escalate"))
        {
            responseText = workflow.ApplyEscalationPlaybook();
        }
        else if (normalized.Contains("draft"))
        {
            responseText = workflow.PrepareReplyDraft();
        }
        else if (normalized.Contains("reset"))
        {
            workflow.Reset();
            responseText = "Reset the support inbox workflow.";
        }
        else
        {
            responseText = "Use the support inbox prompts shown on the page to focus tickets, explain blockers, draft a reply, or escalate blocked cases.";
        }

        return new AgentTurnResponse(AgentName, responseText, [], []);
    }
}
`;
}

function buildDemoProject(projectPath, workspaceRoot) {
  run(
    "dotnet",
    [
      "restore",
      projectPath,
      "-p:RuntimeIdentifier=linux-x64"
    ],
    { cwd: workspaceRoot });

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
    {
      cwd: workspaceRoot
    }
  );
}

async function waitForServerReady(url, timeoutMs = 120000) {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { method: "GET" });
      if (response.ok || response.status === 404) {
        return;
      }
    } catch {
      // Retry while the server starts.
    }

    await sleep(1000);
  }

  throw new Error(`Timed out waiting for ${url}`);
}

async function waitForSettled(chatSurface, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  let stableTicks = 0;

  while (Date.now() < deadline) {
    const thinkingCount = await chatSurface.locator(".ab-chat-surface__state--thinking").count();
    const assistantCount = await chatSurface.locator(".ab-chat-surface__item--assistant").count();

    if (assistantCount > 0 && thinkingCount === 0) {
      stableTicks++;
      if (stableTicks >= 2) {
        return;
      }
    } else {
      stableTicks = 0;
    }

    await sleep(350);
  }

  throw new Error("Chat surface did not settle before timeout.");
}

async function clickSend(chatSurface) {
  const sendButton = chatSurface.locator("button[aria-label*='Send']").first();
  await sendButton.waitFor({ state: "visible", timeout: 30000 });
  await sendButton.evaluate((button) => button.click());
}

async function sendPrompt(chatSurface, prompt, timeoutMs = 30000) {
  const promptInput = chatSurface.getByLabel("Message input").first();
  await promptInput.waitFor({ state: "visible", timeout: 30000 });
  await promptInput.fill(prompt);
  await sleep(250);
  await clickSend(chatSurface);
  await waitForSettled(chatSurface, timeoutMs);
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

      await page.waitForTimeout(1500);
    }
  }

  throw new Error("Assistant surface did not become interactive.");
}

async function recordFlow(browser) {
  const scenarioDir = path.join(outDir, "support-inbox-live");
  fs.mkdirSync(scenarioDir, { recursive: true });

  const context = await browser.newContext({
    viewport: { width: 1600, height: 980 },
    recordVideo: {
      dir: scenarioDir,
      size: { width: 1600, height: 980 }
    }
  });

  const page = await context.newPage();
  await page.goto(`${baseUrl}/demo/workflows/support-inbox`, { waitUntil: "networkidle", timeout: 90000 });
  await page.locator(".demo-doc-page").first().waitFor({ state: "visible", timeout: 30000 });

  const chatSurface = await waitForAssistantSurface(page);

  await sendPrompt(chatSurface, "Show open tickets from this week");
  await page.waitForTimeout(900);
  await sendPrompt(chatSurface, "Explain why they need attention");
  await page.waitForTimeout(900);
  await sendPrompt(chatSurface, "Draft a reply for the highlighted tickets");
  await page.waitForTimeout(1000);
  await sendPrompt(chatSurface, "Escalate the blocked tickets");
  await page.waitForTimeout(1000);
  await sendPrompt(chatSurface, "Draft a reply for the highlighted tickets again");
  await page.waitForTimeout(1800);

  const screenshotPath = path.join(outDir, "support-inbox-agentblazor.png");
  await page.screenshot({ path: screenshotPath, fullPage: true });

  const video = page.video();
  await context.close();
  const webmPath = await video.path();
  const mp4Path = path.join(outDir, "support-inbox-agentblazor.mp4");
  transcode(webmPath, mp4Path);

  return { mp4Path, screenshotPath };
}

async function main() {
  const { workspaceRoot, projectPath } = prepareWorkspace();
  buildDemoProject(projectPath, workspaceRoot);

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
      cwd: workspaceRoot,
      env: {
        ...process.env,
        AgentBlazor__LicenseKey: process.env.AGENTBLAZOR_LICENSE_KEY || "AB-PRO-VALID-KEY-12345678",
        AgentBlazor__DataDirectory: path.join(outDir, "paid-data")
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
    await waitForServerReady(`${baseUrl}/demo/workflows/support-inbox`, 120000);
    const browser = await chromium.launch({ headless: true });
    try {
      const { mp4Path, screenshotPath } = await recordFlow(browser);
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
    fs.rmSync(workspaceRoot, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
