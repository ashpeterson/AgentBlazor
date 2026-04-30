#!/usr/bin/env node

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawn } = require("child_process");
const { chromium, expect } = require("@playwright/test");

const repoRoot = path.resolve(__dirname, "..", "..", "..");
const packageVersion = process.env.AGENTBLAZOR_PACKAGE_VERSION || readPackageVersion();
const packageSourceMode = normalizePackageSourceMode(process.env.AGENTBLAZOR_PACKAGE_SOURCE_MODE || "local");
const publishedFeedUrl = process.env.AGENTBLAZOR_PUBLISHED_FEED_URL || "https://api.nuget.org/v3/index.json";
const baseUrl = process.env.AGENTBLAZOR_HOSTED_WASM_BASE_URL || "http://127.0.0.1:5305";
const timeoutMs = Number.parseInt(process.env.AGENTBLAZOR_HOSTED_WASM_TIMEOUT_MS || "180000", 10);
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outputRoot = process.env.AGENTBLAZOR_HOSTED_WASM_OUTPUT_DIR
  || path.join(repoRoot, "tests", "e2e", "artifacts", "hosted-wasm-remote-chat", stamp);
const workspaceRoot = process.env.AGENTBLAZOR_HOSTED_WASM_WORKSPACE_DIR
  ? path.resolve(process.env.AGENTBLAZOR_HOSTED_WASM_WORKSPACE_DIR)
  : fs.mkdtempSync(path.join(os.tmpdir(), `agentblazor-hosted-wasm-${stamp}-`));
const appRoot = path.join(workspaceRoot, "HostedWasmRemote");
const serverProject = path.join(appRoot, "HostedWasmRemote", "HostedWasmRemote.csproj");
const clientProject = path.join(appRoot, "HostedWasmRemote.Client", "HostedWasmRemote.Client.csproj");
const localFeed = path.join(workspaceRoot, "local-feed");
const packageFeedUrl = packageSourceMode === "local" ? localFeed : publishedFeedUrl;
const dotnetEnv = {
  ...process.env,
  DOTNET_CLI_HOME: path.join(workspaceRoot, ".dotnet-home"),
  NUGET_PACKAGES: path.join(workspaceRoot, ".nuget-packages")
};
const serverLogPath = path.join(outputRoot, "hosted-wasm-server.log");
const reportPath = path.join(outputRoot, "report.json");
const markdownReportPath = path.join(outputRoot, "report.md");
const screenshotPath = path.join(outputRoot, "hosted-wasm-remote-chat.png");

const prompts = {
  widget: "Summarize this hosted WebAssembly app from the floating widget.",
  surface: "Create an operator checklist from the embedded remote surface.",
  panel: "Draft a risk review from the remote side panel.",
  bar: "Write a one-line handoff from the remote command bar."
};

const state = {
  packagesPacked: false,
  templateCreated: false,
  serverPackageInstalled: false,
  clientPackageInstalled: false,
  serverPatched: false,
  clientPatched: false,
  buildPassed: false,
  widgetPromptPassed: false,
  surfacePromptPassed: false,
  panelPromptPassed: false,
  barPromptPassed: false,
  widgetMinimizePassed: false,
  widgetReopenPassed: false
};

let serverProcess;

run().catch(async (error) => {
  console.error(error);
  writeReport("failed", error);
  await stopServer(serverProcess).catch((stopError) => console.error(stopError));
  process.exit(1);
});

async function run() {
  fs.mkdirSync(outputRoot, { recursive: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });

  console.log(`Hosted WASM remote chat output: ${outputRoot}`);
  console.log(`Package version: ${packageVersion}`);
  console.log(`Package source mode: ${packageSourceMode}`);

  if (packageSourceMode === "local") {
    await packLocalPackages();
    state.packagesPacked = true;
  }
  await createHostedWasmApp();
  state.templateCreated = true;
  await writeNuGetConfig();
  await installPackages();
  await patchServerProject();
  await patchClientProject();
  await runCommand("dotnet", ["restore", serverProject, "--force-evaluate"], { cwd: appRoot, env: dotnetEnv });
  await runCommand("dotnet", ["build", serverProject, "-c", "Release", "--no-restore", "-nologo"], { cwd: appRoot, env: dotnetEnv });
  state.buildPassed = true;

  serverProcess = startServer();
  await waitForServerReady(baseUrl, timeoutMs);
  await runBrowserAssertions();
  await stopServer(serverProcess);
  serverProcess = undefined;

  writeReport("passed");
  console.log(`Hosted WASM remote chat report: ${reportPath}`);
  console.log(`Hosted WASM remote chat markdown report: ${markdownReportPath}`);
}

async function packLocalPackages() {
  fs.rmSync(localFeed, { recursive: true, force: true });
  fs.mkdirSync(localFeed, { recursive: true });

  const projects = [
    "src/AgentBlazor.Components/AgentBlazor.Components.csproj",
    "src/AgentBlazor.Client/AgentBlazor.Client.csproj",
    "src/AgentBlazor.Cli/AgentBlazor.Cli.csproj"
  ];

  for (const project of projects) {
    await runCommand(
      "dotnet",
      ["pack", project, "-c", "Release", "-o", localFeed, `-p:PackageVersion=${packageVersion}`, "-nologo"],
      { cwd: repoRoot, env: dotnetEnv });
  }
}

async function createHostedWasmApp() {
  fs.rmSync(appRoot, { recursive: true, force: true });
  await runCommand(
    "dotnet",
    [
      "new",
      "blazor",
      "-n",
      "HostedWasmRemote",
      "-o",
      appRoot,
      "--interactivity",
      "WebAssembly",
      "--all-interactive",
      "--no-https",
      "--no-restore"
    ],
    { cwd: workspaceRoot, env: dotnetEnv });
}

async function writeNuGetConfig() {
  const sourceName = packageSourceMode === "local" ? "agentblazor-local" : "agentblazor-published";

  const content = `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="${sourceName}" value="${escapeXml(packageFeedUrl)}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
`;

  fs.writeFileSync(path.join(appRoot, "nuget.config"), content, "utf8");
}

async function installPackages() {
  await runCommand("dotnet", ["add", serverProject, "package", "AgentBlazor", "--version", packageVersion], { cwd: appRoot, env: dotnetEnv });
  state.serverPackageInstalled = true;
  await runCommand("dotnet", ["add", clientProject, "package", "AgentBlazor.Client", "--version", packageVersion], { cwd: appRoot, env: dotnetEnv });
  state.clientPackageInstalled = true;
}

async function patchServerProject() {
  const serverDirectory = path.dirname(serverProject);
  const adapterPath = path.join(serverDirectory, "DeterministicRemoteRuntimeAdapter.cs");
  const programPath = path.join(serverDirectory, "Program.cs");

  fs.writeFileSync(adapterPath, `using System.Runtime.CompilerServices;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;

namespace HostedWasmRemote;

public sealed class DeterministicRemoteRuntimeAdapter : IAgentRuntimeAdapter
{
    private const string AgentName = "Hosted WASM Remote Assistant";

    public bool SupportsStreaming => false;
    public bool SupportsReconnect => false;
    public bool SupportsCancellation => false;

    public Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AgentTurnResponse(AgentName, $"Hosted WASM remote response: {request.UserMessage}", [], []));
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> ConnectRunStreamAsync(
        string runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }

    public Task<bool> StopRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
`, "utf8");

  let program = fs.readFileSync(programPath, "utf8");
  program = `using AgentBlazor;\n${program}`;
  program = program.replace(
    "builder.Services.AddRazorComponents()\n    .AddInteractiveWebAssemblyComponents();",
    `builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddAgentBlazor(options =>
{
    options.UseRuntimeAdapter<HostedWasmRemote.DeterministicRemoteRuntimeAdapter>();
    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddAgent("assistant", agent =>
        {
            agent.WithDescription("Deterministic hosted WebAssembly remote chat validation agent.");
            agent.WithRoutePrefixes("/");
        });
    });
});`);
  program = program.replace(
    "app.MapRazorComponents<App>()\n    .AddInteractiveWebAssemblyRenderMode()\n    .AddAdditionalAssemblies(typeof(HostedWasmRemote.Client._Imports).Assembly);\n\napp.Run();",
    `app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(HostedWasmRemote.Client._Imports).Assembly);
app.MapAgentBlazorEndpoints();
app.MapAgentBlazorRemoteChat();

app.Run();`);

  fs.writeFileSync(programPath, program, "utf8");
  state.serverPatched = true;
}

async function patchClientProject() {
  const clientDirectory = path.dirname(clientProject);
  const programPath = path.join(clientDirectory, "Program.cs");
  const importsPath = path.join(clientDirectory, "_Imports.razor");
  const homePath = path.join(clientDirectory, "Pages", "Home.razor");

  let program = fs.readFileSync(programPath, "utf8");
  program = `using System.Net.Http;\n${program}`;
  program = program.replace(
    "var builder = WebAssemblyHostBuilder.CreateDefault(args);\n\nawait builder.Build().RunAsync();",
    `var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();`);
  fs.writeFileSync(programPath, program, "utf8");

  fs.appendFileSync(importsPath, "\n@using AgentBlazor.Client.Chat\n", "utf8");
  fs.writeFileSync(homePath, `@page "/"

<PageTitle>Hosted WASM Remote Chat</PageTitle>

<main class="hosted-wasm-remote-chat" data-testid="hosted-wasm-remote-chat">
    <h1>Hosted WASM Remote Chat</h1>
    <p>This page validates browser-safe AgentBlazor.Client components running in WebAssembly.</p>

    <section data-testid="remote-widget-section">
        <h2>Remote Widget</h2>
        <AgentRemoteChatWidget Endpoint="/agentblazor/chat/run"
                               Title="Remote Widget Assistant"
                               SessionId="hosted-wasm-widget"
                               Style="right: 2rem; bottom: 7rem;"
                               InitiallyOpen="true" />
    </section>

    <section data-testid="remote-surface-section">
        <h2>Remote Surface</h2>
        <AgentRemoteChatSurface Endpoint="/agentblazor/chat/run"
                                Title="Remote Surface Assistant"
                                SessionId="hosted-wasm-surface" />
    </section>

    <section data-testid="remote-panel-section">
        <h2>Remote Panel</h2>
        <AgentRemoteChatPanel Endpoint="/agentblazor/chat/run"
                              Title="Remote Panel Assistant"
                              SessionId="hosted-wasm-panel" />
    </section>

    <section data-testid="remote-bar-section">
        <h2>Remote Bar</h2>
        <AgentRemoteChatBar Endpoint="/agentblazor/chat/run"
                            Title="Remote Bar Assistant"
                            SessionId="hosted-wasm-bar" />
    </section>
</main>
`, "utf8");

  state.clientPatched = true;
}

function startServer() {
  const child = spawn(
    "dotnet",
    ["run", "--project", serverProject, "-c", "Release", "--no-build", "--no-launch-profile", "--urls", baseUrl],
    {
      cwd: appRoot,
      env: {
        ...dotnetEnv,
        ASPNETCORE_ENVIRONMENT: "Development",
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

async function waitForServerReady(url, timeout) {
  const deadline = Date.now() + timeout;

  while (Date.now() < deadline) {
    if (serverProcess?.exitCode !== null) {
      throw new Error(`Hosted WASM server exited early. See ${serverLogPath}.`);
    }

    try {
      const response = await fetch(url);
      if (response.ok || response.status === 404) {
        return;
      }
    } catch {
    }

    await sleep(1000);
  }

  throw new Error(`Hosted WASM server did not become ready within ${timeout}ms. See ${serverLogPath}.`);
}

async function runBrowserAssertions() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  try {
    await page.goto(baseUrl, { waitUntil: "networkidle", timeout: timeoutMs });
    await expect(page.getByTestId("hosted-wasm-remote-chat")).toBeVisible({ timeout: 60000 });

    const widgetWindow = page.getByTestId("agent-remote-chat-widget-window");
    await expect(widgetWindow).toBeVisible({ timeout: 30000 });
    await submitRemotePrompt(widgetWindow.getByTestId("agent-remote-chat-surface").first(), prompts.widget);
    state.widgetPromptPassed = true;

    await page.getByTestId("agent-remote-chat-widget-minimize").click();
    await expect(widgetWindow).toBeHidden({ timeout: 30000 });
    state.widgetMinimizePassed = true;
    await page.getByTestId("agent-remote-chat-widget-open").click();
    await expect(widgetWindow).toBeVisible({ timeout: 30000 });
    state.widgetReopenPassed = true;
    await page.getByTestId("agent-remote-chat-widget-minimize").click();
    await expect(widgetWindow).toBeHidden({ timeout: 30000 });

    await submitRemotePrompt(page.getByTestId("remote-surface-section").getByTestId("agent-remote-chat-surface"), prompts.surface);
    state.surfacePromptPassed = true;
    await submitRemotePrompt(page.getByTestId("remote-panel-section").getByTestId("agent-remote-chat-surface"), prompts.panel);
    state.panelPromptPassed = true;
    await submitRemotePrompt(page.getByTestId("remote-bar-section").getByTestId("agent-remote-chat-surface"), prompts.bar);
    state.barPromptPassed = true;

    await page.screenshot({ path: screenshotPath, fullPage: true });
  } catch (error) {
    await page.screenshot({ path: path.join(outputRoot, "failure.png"), fullPage: true }).catch(() => {});
    await fs.promises.writeFile(path.join(outputRoot, "failure.html"), await page.content().catch(() => ""), "utf8").catch(() => {});
    throw error;
  } finally {
    await context.close().catch(() => {});
    await browser.close().catch(() => {});
  }
}

async function submitRemotePrompt(surface, prompt) {
  await expect(surface).toBeVisible({ timeout: 30000 });
  await surface.evaluate((element) => element.scrollIntoView({ block: "center", inline: "center" }));
  await sleep(250);
  await surface.getByTestId("agent-remote-chat-input").fill(prompt);
  await surface.getByTestId("agent-remote-chat-send").click();
  await expect(surface.getByText(prompt, { exact: true }).first()).toBeVisible({ timeout: 30000 });
  await expect(surface.getByText(`Hosted WASM remote response: ${prompt}`, { exact: true }).first()).toBeVisible({ timeout: 30000 });
}

function writeReport(status, failure = null) {
  const report = {
    generatedAtUtc: new Date().toISOString(),
    status,
    packageVersion,
    packageSourceMode,
    baseUrl,
    workspaceRoot,
    appRoot,
    serverProject,
    clientProject,
    outputRoot,
    screenshotPath,
    serverLogPath,
    prompts,
    assertions: state,
    failure: failure ? { message: failure.message, stack: failure.stack || "" } : null
  };

  fs.mkdirSync(outputRoot, { recursive: true });
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), "utf8");
  fs.writeFileSync(markdownReportPath, buildMarkdownReport(report), "utf8");
}

function normalizePackageSourceMode(value) {
  const normalized = value.trim().toLowerCase();
  if (normalized === "local" || normalized === "published" || normalized === "github") {
    return normalized === "github" ? "published" : normalized;
  }

  throw new Error(`Unsupported AGENTBLAZOR_PACKAGE_SOURCE_MODE '${value}'. Supported values: local, published.`);
}

function buildMarkdownReport(report) {
  const rows = Object.entries(report.assertions)
    .map(([name, value]) => `| ${name} | ${value ? "pass" : "fail"} |`)
    .join("\n");

  return [
    "# Hosted WASM Remote Chat Report",
    "",
    `Status: **${report.status.toUpperCase()}**`,
    `Generated: ${report.generatedAtUtc}`,
    `Package version: ${report.packageVersion}`,
    `Workspace: ${report.workspaceRoot}`,
    "",
    "## Assertions",
    "",
    "| Assertion | Result |",
    "| --- | --- |",
    rows,
    "",
    "## Artifacts",
    "",
    `- Screenshot: ${path.relative(outputRoot, screenshotPath)}`,
    `- Server log: ${path.relative(outputRoot, serverLogPath)}`
  ].join("\n") + "\n";
}

async function runCommand(command, args, options = {}) {
  const output = [];
  await new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: options.cwd || repoRoot,
      env: options.env || process.env,
      stdio: ["ignore", "pipe", "pipe"]
    });

    child.stdout.on("data", (chunk) => {
      const text = chunk.toString();
      output.push(text);
      process.stdout.write(text);
    });
    child.stderr.on("data", (chunk) => {
      const text = chunk.toString();
      output.push(text);
      process.stderr.write(text);
    });
    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${command} ${args.join(" ")} failed with exit code ${code}`));
      }
    });
  });

  return output.join("");
}

async function stopServer(child) {
  if (!child || child.exitCode !== null) {
    return;
  }

  child.kill("SIGTERM");
  await sleep(1000);
  if (child.exitCode === null) {
    child.kill("SIGKILL");
  }
}

function readPackageVersion() {
  const props = fs.readFileSync(path.join(repoRoot, "Directory.Build.props"), "utf8");
  const match = props.match(/<Version>([^<]+)<\/Version>/);
  if (!match) {
    throw new Error("Unable to read <Version> from Directory.Build.props.");
  }

  return match[1];
}

function escapeXml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
