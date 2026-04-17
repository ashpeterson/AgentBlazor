#!/usr/bin/env node

const fs = require("fs");
const os = require("os");
const path = require("path");
const crypto = require("crypto");
const { spawn } = require("child_process");
const { chromium, expect } = require("@playwright/test");
const { openFloatingChatWidget } = require("../specs/chat-helpers.cjs");

const repoRoot = path.resolve(__dirname, "..", "..", "..");
const packageVersion = process.env.AGENTBLAZOR_PACKAGE_VERSION || readPackageVersion();
const externalTemplate = process.env.AGENTBLAZOR_EXTERNAL_TEMPLATE || "";
const externalRepoUrl = externalTemplate
  ? ""
  : process.env.AGENTBLAZOR_EXTERNAL_REPO || "https://github.com/damienbod/BlazorSecurityNet10.git";
const externalRepoRef = process.env.AGENTBLAZOR_EXTERNAL_REF || "";
const externalProjectRelativePath = process.env.AGENTBLAZOR_EXTERNAL_PROJECT
  || (externalTemplate ? "AgentBlazorExternalTemplate.csproj" : "BlazorApp/BlazorApp.csproj");
const baseUrl = process.env.AGENTBLAZOR_EXTERNAL_BASE_URL || "http://127.0.0.1:5295";
const appPath = process.env.AGENTBLAZOR_EXTERNAL_APP_PATH || "/";
const promptText = process.env.AGENTBLAZOR_EXTERNAL_PROMPT || "Can you explain what this Blazor app does?";
const providerMode = normalizeProviderMode(process.env.AGENTBLAZOR_EXTERNAL_PROVIDER_MODE || "none");
const deterministicResponseText = `Deterministic external test response: ${promptText}`;
const loginPath = process.env.AGENTBLAZOR_EXTERNAL_LOGIN_PATH || "";
const loginUsername = process.env.AGENTBLAZOR_EXTERNAL_LOGIN_USERNAME || "";
const loginPassword = process.env.AGENTBLAZOR_EXTERNAL_LOGIN_PASSWORD || "";
const timeoutMs = Number.parseInt(process.env.AGENTBLAZOR_EXTERNAL_TIMEOUT_MS || "180000", 10);
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outputRoot = process.env.AGENTBLAZOR_EXTERNAL_OUTPUT_DIR
  || path.join(repoRoot, "tests", "e2e", "artifacts", "external-chat-widget", stamp);
const workspaceRoot = process.env.AGENTBLAZOR_EXTERNAL_WORKSPACE_DIR
  ? path.resolve(process.env.AGENTBLAZOR_EXTERNAL_WORKSPACE_DIR)
  : fs.mkdtempSync(path.join(os.tmpdir(), `agentblazor-external-chat-${stamp}-`));
const externalRoot = path.join(workspaceRoot, "external-app");
const localFeed = path.join(workspaceRoot, "local-feed");
const toolPath = path.join(workspaceRoot, "tools");
const serverLogPath = path.join(outputRoot, "external-app-server.log");
const reportPath = path.join(outputRoot, "report.json");
const screenshotPath = path.join(outputRoot, "chat-widget.png");
const diagnosticsPath = path.join(outputRoot, "diagnostics.json");

const diagnostics = {
  browserConsole: [],
  pageErrors: [],
  failedRequests: [],
  widgetStates: [],
  screenshots: []
};

let serverProcess;

run().catch(async (error) => {
  console.error(error);
  writeDiagnosticsFile({ failure: { message: error.message, stack: error.stack || "" } });
  await stopServer(serverProcess).catch((stopError) => console.error(stopError));
  process.exit(1);
});

async function run() {
  fs.mkdirSync(outputRoot, { recursive: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });

  console.log(`External chat widget validation output: ${outputRoot}`);
  if (externalTemplate) {
    console.log(`Template: ${externalTemplate}`);
  } else {
    console.log(`Repository: ${externalRepoUrl}${externalRepoRef ? ` @ ${externalRepoRef}` : ""}`);
  }
  console.log(`Project: ${externalProjectRelativePath}`);
  console.log(`AgentBlazor package version: ${packageVersion}`);
  console.log(`Provider mode: ${providerMode}`);

  await restoreRepo();
  await packLocalPackages();
  await prepareExternalApp();

  const projectPath = path.join(externalRoot, externalProjectRelativePath);
  const projectDirectory = path.dirname(projectPath);
  const env = buildExternalEnv();

  await writeNuGetConfig(projectDirectory);
  await runCommand("dotnet", ["restore", projectPath, "--force-evaluate"], { cwd: externalRoot, env });
  await runCommand("dotnet", ["build", projectPath, "--no-restore", "-nologo"], { cwd: externalRoot, env });
  await runCommand("dotnet", ["add", projectPath, "package", "AgentBlazor", "--version", packageVersion], { cwd: externalRoot, env });
  await runCommand(
    "dotnet",
    ["tool", "install", "AgentBlazor.Cli", "--version", packageVersion, "--tool-path", toolPath, "--add-source", localFeed],
    { cwd: externalRoot, env });

  const agentblazor = path.join(toolPath, process.platform === "win32" ? "agentblazor.exe" : "agentblazor");
  await runCommand(agentblazor, ["--version"], { cwd: externalRoot, env });
  await runCommand(agentblazor, ["init", projectPath, "--non-interactive"], { cwd: externalRoot, env });
  await runCommand(agentblazor, ["scaffold", projectPath, "--diff", "--non-interactive"], { cwd: externalRoot, env });
  await runCommand(agentblazor, ["scaffold", projectPath, "--approve", "--non-interactive"], { cwd: externalRoot, env });
  await assertScaffoldIdempotent(agentblazor, projectPath, env);
  if (providerMode === "deterministic") {
    installDeterministicRuntimeAdapter(projectPath);
  }
  await runCommand("dotnet", ["restore", projectPath, "--force-evaluate"], { cwd: externalRoot, env });
  await runCommand("dotnet", ["build", projectPath, "--no-restore", "-nologo"], { cwd: externalRoot, env });
  await runCommand(agentblazor, ["doctor", projectPath, "--non-interactive"], { cwd: externalRoot, env });
  await runCommand(agentblazor, ["validate", projectPath, "--non-interactive"], { cwd: externalRoot, env });

  serverProcess = startExternalServer(projectPath, env);
  await waitForServerReady(baseUrl, timeoutMs);
  await runBrowserAssertions();
  await stopServer(serverProcess);
  serverProcess = undefined;

  const report = {
    generatedAtUtc: new Date().toISOString(),
    externalTemplate,
    externalRepoUrl,
    externalRepoRef,
    externalProjectRelativePath,
    packageVersion,
    baseUrl,
    appPath,
    promptText,
    providerMode,
    outputRoot,
    workspaceRoot,
    screenshotPath,
    diagnosticsPath,
    diagnosticsSummary: {
      browserConsoleCount: diagnostics.browserConsole.length,
      pageErrorCount: diagnostics.pageErrors.length,
      failedRequestCount: diagnostics.failedRequests.length,
      widgetStateCount: diagnostics.widgetStates.length,
      screenshotCount: diagnostics.screenshots.length
    },
    assertions: {
      packageInstalled: true,
      cliScaffolded: true,
      scaffoldIdempotent: true,
      doctorPassed: true,
      validatePassed: true,
      loginSubmitted: Boolean(loginPath && loginUsername && loginPassword),
      promptSubmitted: true,
      providerGuidanceRendered: providerMode === "none",
      providerResponseRendered: providerMode === "deterministic",
      deterministicRuntimeAdapterRegistered: providerMode === "deterministic",
      minimizeButtonWorks: true,
      escapeMinimizes: true,
      repeatedOpenCloseWorks: true,
      reloadReopenWorks: true,
      agentAssetsLoaded: true
    }
  };
  writeDiagnosticsFile();
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(`External chat widget report: ${reportPath}`);
  console.log(`External chat widget diagnostics: ${diagnosticsPath}`);
  console.log(`External chat widget screenshot: ${screenshotPath}`);
}

function writeDiagnosticsFile(extra = {}) {
  fs.mkdirSync(outputRoot, { recursive: true });
  fs.writeFileSync(diagnosticsPath, JSON.stringify({ ...diagnostics, ...extra }, null, 2));
}

function readPackageVersion() {
  const props = fs.readFileSync(path.join(repoRoot, "Directory.Build.props"), "utf8");
  const match = props.match(/<Version>([^<]+)<\/Version>/);
  if (!match) {
    throw new Error("Unable to read <Version> from Directory.Build.props.");
  }

  return match[1];
}

function normalizeProviderMode(value) {
  const normalized = value.trim().toLowerCase();

  if (normalized === "" || normalized === "none") {
    return "none";
  }

  if (normalized === "deterministic") {
    return "deterministic";
  }

  throw new Error(`Unsupported AGENTBLAZOR_EXTERNAL_PROVIDER_MODE '${value}'. Supported values: none, deterministic.`);
}

async function restoreRepo() {
  await runCommand("dotnet", ["restore", "AgentBlazor.slnx", "--force-evaluate"], { cwd: repoRoot });
}

async function packLocalPackages() {
  fs.rmSync(localFeed, { recursive: true, force: true });
  fs.mkdirSync(localFeed, { recursive: true });

  const projects = [
    "src/AgentBlazor.Licensing/AgentBlazor.Licensing.csproj",
    "src/AgentBlazor.Core/AgentBlazor.Core.csproj",
    "src/AgentBlazor.ProviderAdapters/AgentBlazor.ProviderAdapters.csproj",
    "src/AgentBlazor.Hosting/AgentBlazor.Hosting.csproj",
    "src/AgentBlazor.Components/AgentBlazor.Components.csproj",
    "src/AgentBlazor.Cli/AgentBlazor.Cli.csproj"
  ];

  for (const project of projects) {
    await runCommand(
      "dotnet",
      ["pack", project, "-c", "Release", "--no-restore", "-o", localFeed, `-p:PackageVersion=${packageVersion}`, "-nologo"],
      { cwd: repoRoot });
  }
}

async function prepareExternalApp() {
  if (externalTemplate) {
    await createExternalAppFromTemplate();
    return;
  }

  await cloneExternalApp();
}

async function createExternalAppFromTemplate() {
  fs.rmSync(externalRoot, { recursive: true, force: true });

  if (!["blazor"].includes(externalTemplate.toLowerCase())) {
    throw new Error(`Unsupported external template '${externalTemplate}'. Supported values: blazor.`);
  }

  await runCommand(
    "dotnet",
    [
      "new",
      "blazor",
      "--name",
      "AgentBlazorExternalTemplate",
      "--output",
      externalRoot,
      "--interactivity",
      "Server",
      "--no-restore"
    ],
    { cwd: workspaceRoot });

  await runCommand("git", ["init"], { cwd: externalRoot });
  await runCommand("git", ["config", "user.email", "agentblazor@example.local"], { cwd: externalRoot });
  await runCommand("git", ["config", "user.name", "AgentBlazor External Test"], { cwd: externalRoot });
  await runCommand("git", ["add", "."], { cwd: externalRoot });
  await runCommand("git", ["commit", "-m", "Initial external template"], { cwd: externalRoot });
}

async function cloneExternalApp() {
  fs.rmSync(externalRoot, { recursive: true, force: true });
  await runCommand("git", ["clone", "--depth", "1", externalRepoUrl, externalRoot], { cwd: workspaceRoot });

  if (externalRepoRef) {
    await runCommand("git", ["fetch", "--depth", "1", "origin", externalRepoRef], { cwd: externalRoot });
    await runCommand("git", ["checkout", "FETCH_HEAD"], { cwd: externalRoot });
  }
}

async function writeNuGetConfig(projectDirectory) {
  const content = `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="agentblazor-local" value="${escapeXml(localFeed)}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
`;
  fs.writeFileSync(path.join(projectDirectory, "nuget.config"), content);
}

async function assertScaffoldIdempotent(agentblazor, projectPath, env) {
  const before = snapshotRelevantFiles(externalRoot);
  const output = await runCommand(agentblazor, ["scaffold", projectPath, "--diff", "--non-interactive"], { cwd: externalRoot, env });
  const after = snapshotRelevantFiles(externalRoot);

  const noChangesReported = output.includes("No file changes were needed.")
    || output.includes("No scaffold changes proposed.");

  if (JSON.stringify(before) === JSON.stringify(after) && noChangesReported) {
    return;
  }

  const diffPath = path.join(outputRoot, "scaffold-idempotency-diff.json");
  fs.writeFileSync(diffPath, JSON.stringify({
    output,
    fileChanges: diffSnapshots(before, after)
  }, null, 2));
  throw new Error(`Scaffold was not idempotent. See ${diffPath}.`);
}

function installDeterministicRuntimeAdapter(projectPath) {
  const projectDirectory = path.dirname(projectPath);
  const adapterDirectory = path.join(projectDirectory, "AgentBlazorExternalTestProvider");
  const adapterPath = path.join(adapterDirectory, "DeterministicTestRuntimeAdapter.cs");
  const programPath = path.join(projectDirectory, "Program.cs");

  if (!fs.existsSync(programPath)) {
    throw new Error(`Unable to register deterministic runtime adapter because Program.cs was not found at ${programPath}.`);
  }

  fs.mkdirSync(adapterDirectory, { recursive: true });
  fs.writeFileSync(adapterPath, deterministicRuntimeAdapterSource(), "utf8");
  registerDeterministicRuntimeAdapter(programPath);
}

function deterministicRuntimeAdapterSource() {
  return `using System.Runtime.CompilerServices;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazorExternalTestProvider;

public sealed class DeterministicTestRuntimeAdapter : IAgentRuntimeAdapter
{
    private const string AgentName = "External Test Assistant";

    public bool SupportsStreaming => true;

    public bool SupportsReconnect => false;

    public bool SupportsCancellation => false;

    public Task<AgentTurnResponse> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildResponse(request));
    }

    public async IAsyncEnumerable<AgentTurnStreamEvent> RunTurnStreamingAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runId = $"external-test-{Guid.NewGuid():N}";
        var sequence = 0L;
        var response = BuildResponse(request);

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

    private static AgentTurnResponse BuildResponse(AgentTurnRequest request) =>
        new(AgentName, $"Deterministic external test response: {request.UserMessage}", [], []);
}
`;
}

function registerDeterministicRuntimeAdapter(programPath) {
  const registration = "UseRuntimeAdapter<AgentBlazorExternalTestProvider.DeterministicTestRuntimeAdapter>";
  const content = fs.readFileSync(programPath, "utf8");

  if (content.includes(registration)) {
    return;
  }

  const registrationPattern = /(builder\.Services\.AddAgentBlazor\(\s*([A-Za-z_]\w*)\s*=>\s*\r?\n\s*\{\r?\n?)/m;
  const match = content.match(registrationPattern);

  if (!match) {
    throw new Error(`Unable to find builder.Services.AddAgentBlazor(...) registration in ${programPath}.`);
  }

  const optionsVariable = match[2];
  const patched = content.replace(
    registrationPattern,
    `$1    ${optionsVariable}.${registration}();\n`);

  fs.writeFileSync(programPath, patched, "utf8");
}

function snapshotRelevantFiles(root) {
  const files = [];
  collectRelevantFiles(root, files);

  return files
    .sort((left, right) => left.localeCompare(right))
    .map((file) => {
      const content = fs.readFileSync(path.join(root, file));
      return {
        path: file,
        sha256: crypto.createHash("sha256").update(content).digest("hex")
      };
    });
}

function collectRelevantFiles(directory, files) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (shouldSkipSnapshotEntry(entry.name)) {
      continue;
    }

    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      collectRelevantFiles(absolutePath, files);
      continue;
    }

    if (!entry.isFile() || !isRelevantSnapshotFile(entry.name)) {
      continue;
    }

    files.push(path.relative(externalRoot, absolutePath).split(path.sep).join("/"));
  }
}

function shouldSkipSnapshotEntry(name) {
  return [
    ".agentblazor",
    ".git",
    ".vs",
    "bin",
    "obj",
    "node_modules",
    "TestResults"
  ].includes(name);
}

function isRelevantSnapshotFile(name) {
  return [
    ".cs",
    ".cshtml",
    ".csproj",
    ".css",
    ".html",
    ".js",
    ".json",
    ".props",
    ".razor",
    ".targets",
    ".xml",
    ".config"
  ].some((extension) => name.endsWith(extension));
}

function diffSnapshots(before, after) {
  const beforeMap = new Map(before.map((entry) => [entry.path, entry.sha256]));
  const afterMap = new Map(after.map((entry) => [entry.path, entry.sha256]));
  const paths = [...new Set([...beforeMap.keys(), ...afterMap.keys()])].sort((left, right) => left.localeCompare(right));

  return paths
    .map((filePath) => ({
      path: filePath,
      before: beforeMap.get(filePath) || null,
      after: afterMap.get(filePath) || null
    }))
    .filter((entry) => entry.before !== entry.after);
}

function buildExternalEnv() {
  return {
    ...process.env,
    DOTNET_CLI_HOME: path.join(workspaceRoot, ".dotnet-home"),
    NUGET_PACKAGES: path.join(workspaceRoot, ".nuget-packages"),
    ASPNETCORE_ENVIRONMENT: "Development"
  };
}

function startExternalServer(projectPath, env) {
  const child = spawn(
    "dotnet",
    ["run", "--project", projectPath, "--no-build", "--no-launch-profile", "--urls", baseUrl],
    {
      cwd: externalRoot,
      env,
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
      throw new Error(`External app server exited early. See ${serverLogPath}.`);
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

  throw new Error(`External app server did not become ready within ${timeout}ms. See ${serverLogPath}.`);
}

async function runBrowserAssertions() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();
  attachBrowserDiagnostics(page);

  try {
    await performOptionalLogin(page);
    await page.goto(getAppUrl(), { waitUntil: "networkidle", timeout: timeoutMs });
    let controls = await openFloatingChatWidget(page);
    await assertWidgetOpen(controls.widgetWindow, controls.openButton, "initial-open");
    await captureWidgetState(page, "initial-open", controls.widgetWindow, controls.openButton);

    const input = controls.widgetSurface.getByLabel("Message input").first();
    const sendButton = controls.widgetSurface.getByRole("button", { name: /send message/i }).first();
    await expect(input).toBeVisible();
    await input.fill(promptText);
    await expect(sendButton).toBeEnabled();
    await sendButton.click();

    await expect(controls.widgetSurface.getByText(promptText, { exact: true }).first()).toBeVisible({ timeout: 30000 });
    await assertProviderOutcome(controls.widgetSurface);
    await captureWidgetState(page, "prompt-submitted", controls.widgetWindow, controls.openButton);

    await controls.minimizeButton.click();
    await assertWidgetClosed(controls.widgetWindow, controls.openButton, "minimize-button");
    await captureWidgetState(page, "minimize-button", controls.widgetWindow, controls.openButton);

    await controls.openButton.click();
    await assertWidgetOpen(controls.widgetWindow, controls.openButton, "reopened-after-minimize");
    await captureWidgetState(page, "reopened-after-minimize", controls.widgetWindow, controls.openButton);

    await controls.widgetWindow.press("Escape");
    await assertWidgetClosed(controls.widgetWindow, controls.openButton, "escape");
    await captureWidgetState(page, "escape", controls.widgetWindow, controls.openButton);

    for (let index = 1; index <= 3; index++) {
      await controls.openButton.click();
      await assertWidgetOpen(controls.widgetWindow, controls.openButton, `cycle-${index}-open`);
      await controls.minimizeButton.click();
      await assertWidgetClosed(controls.widgetWindow, controls.openButton, `cycle-${index}-closed`);
    }
    await captureWidgetState(page, "repeated-cycle-final", controls.widgetWindow, controls.openButton);

    await page.reload({ waitUntil: "networkidle", timeout: timeoutMs });
    controls = await openFloatingChatWidget(page);
    await assertWidgetOpen(controls.widgetWindow, controls.openButton, "reload-reopen");
    await captureWidgetState(page, "reload-reopen", controls.widgetWindow, controls.openButton);

    assertNoAgentBlazorAssetFailures();

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

async function assertProviderOutcome(widgetSurface) {
  if (providerMode === "deterministic") {
    await expect(widgetSurface.getByText(deterministicResponseText, { exact: true }).first()).toBeVisible({ timeout: 30000 });

    const noProviderGuidance = widgetSurface.getByText(/No AI provider configured/i).first();
    if (await noProviderGuidance.isVisible().catch(() => false)) {
      throw new Error("Deterministic provider response rendered alongside no-provider guidance.");
    }

    return;
  }

  await expect(widgetSurface.getByText(/No AI provider configured/i).first()).toBeVisible({ timeout: 30000 });
}

function attachBrowserDiagnostics(page) {
  page.on("console", (message) => {
    diagnostics.browserConsole.push({
      type: message.type(),
      text: message.text(),
      location: message.location()
    });
  });

  page.on("pageerror", (error) => {
    diagnostics.pageErrors.push({
      name: error.name,
      message: error.message,
      stack: error.stack || ""
    });
  });

  page.on("requestfailed", (request) => {
    diagnostics.failedRequests.push({
      url: request.url(),
      method: request.method(),
      resourceType: request.resourceType(),
      failure: request.failure()?.errorText || "unknown"
    });
  });
}

async function assertWidgetOpen(widgetWindow, openButton, stateName) {
  await expect(widgetWindow).toBeVisible({ timeout: 30000 });
  await waitForWidgetStyle(
    widgetWindow,
    stateName,
    (styles) =>
      styles.visibility === "visible"
      && styles.pointerEvents !== "none"
      && Number.parseFloat(styles.opacity) >= 0.95);
  await expect(openButton).toBeHidden({ timeout: 30000 });
}

async function assertWidgetClosed(widgetWindow, openButton, stateName) {
  await expect(widgetWindow).toBeHidden({ timeout: 30000 });
  await waitForWidgetStyle(
    widgetWindow,
    stateName,
    (styles) =>
      styles.visibility === "hidden"
      && styles.pointerEvents === "none"
      && Number.parseFloat(styles.opacity) <= 0.05);
  await expect(openButton).toBeVisible({ timeout: 30000 });
}

async function waitForWidgetStyle(locator, stateName, predicate) {
  const deadline = Date.now() + 30000;
  let lastStyles;

  while (Date.now() < deadline) {
    lastStyles = await readWidgetWindowState(locator);
    if (predicate(lastStyles)) {
      return lastStyles;
    }

    await sleep(100);
  }

  throw new Error(`Widget state '${stateName}' did not reach expected computed styles. Last styles: ${JSON.stringify(lastStyles)}`);
}

async function captureWidgetState(page, stateName, widgetWindow, openButton) {
  const screenshot = path.join(outputRoot, `state-${stateName}.png`);
  const state = {
    name: stateName,
    url: page.url(),
    widgetWindowVisible: await widgetWindow.isVisible().catch(() => false),
    openButtonVisible: await openButton.isVisible().catch(() => false),
    widgetWindow: await readWidgetWindowState(widgetWindow).catch((error) => ({ error: error.message })),
    openButton: await readElementState(openButton).catch((error) => ({ error: error.message })),
    screenshot
  };

  diagnostics.widgetStates.push(state);
  diagnostics.screenshots.push(screenshot);
  await page.screenshot({ path: screenshot, fullPage: true });
}

async function readWidgetWindowState(locator) {
  return locator.evaluate((element) => {
    const styles = window.getComputedStyle(element);
    const rect = element.getBoundingClientRect();

    return {
      className: element.className,
      ariaHidden: element.getAttribute("aria-hidden"),
      display: styles.display,
      opacity: styles.opacity,
      pointerEvents: styles.pointerEvents,
      transform: styles.transform,
      visibility: styles.visibility,
      zIndex: styles.zIndex,
      rect: {
        x: rect.x,
        y: rect.y,
        width: rect.width,
        height: rect.height
      }
    };
  });
}

async function readElementState(locator) {
  return locator.evaluate((element) => {
    const styles = window.getComputedStyle(element);
    const rect = element.getBoundingClientRect();

    return {
      className: element.className,
      ariaLabel: element.getAttribute("aria-label"),
      display: styles.display,
      opacity: styles.opacity,
      pointerEvents: styles.pointerEvents,
      visibility: styles.visibility,
      rect: {
        x: rect.x,
        y: rect.y,
        width: rect.width,
        height: rect.height
      }
    };
  });
}

function assertNoAgentBlazorAssetFailures() {
  const failures = diagnostics.failedRequests.filter((request) =>
    request.url.includes("_content/AgentBlazor/")
    || request.url.includes("/agentblazor")
    || request.url.includes("/_agentblazor"));

  if (failures.length === 0) {
    return;
  }

  throw new Error(`AgentBlazor browser requests failed: ${JSON.stringify(failures, null, 2)}`);
}

async function performOptionalLogin(page) {
  if (!loginPath || !loginUsername || !loginPassword) {
    return;
  }

  const loginUrl = new URL(loginPath, `${baseUrl}/`).toString();
  await page.goto(loginUrl, { waitUntil: "networkidle", timeout: timeoutMs });

  const usernameInput = page
    .locator("input[autocomplete='username'], input[name$='.UserName'], input[placeholder*='user' i]")
    .first();
  const passwordInput = page
    .locator("input[autocomplete='current-password'], input[type='password'], input[name$='.Password']")
    .first();
  const loginForm = usernameInput.locator("xpath=ancestor::form[1]");

  await usernameInput.waitFor({ state: "visible", timeout: 30000 });
  await usernameInput.fill(loginUsername);
  await passwordInput.fill(loginPassword);
  await loginForm.waitFor({ state: "attached", timeout: 30000 });

  // Blazor enhanced forms can authenticate successfully without driving a full
  // document navigation in headless tests. A native post ensures auth cookies
  // are committed before we navigate into the authenticated app layout.
  const postResponse = page.waitForResponse(
    (response) => response.url().startsWith(loginUrl) && response.request().method() === "POST",
    { timeout: 30000 }).catch(() => null);

  await loginForm.evaluate((form) => {
    if (!(form instanceof HTMLFormElement)) {
      throw new Error("Login form is not an HTML form.");
    }

    HTMLFormElement.prototype.submit.call(form);
  });

  await postResponse;
  await page.waitForLoadState("networkidle", { timeout: 30000 }).catch(() => {});
  await page.goto(getAppUrl(), { waitUntil: "networkidle", timeout: timeoutMs });

  const currentUrl = new URL(page.url());
  const expectedLoginUrl = new URL(loginUrl);
  if (currentUrl.pathname.toLowerCase() === expectedLoginUrl.pathname.toLowerCase()) {
    throw new Error(`Login did not reach the authenticated app. Current URL: ${page.url()}`);
  }
}

function getAppUrl() {
  return new URL(appPath, `${baseUrl}/`).toString();
}

async function stopServer(child) {
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

function waitForExit(child, timeout) {
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

    const timer = setTimeout(() => done(false), timeout);
    child.once("exit", () => done(true));
  });
}

function runCommand(command, args, options = {}) {
  return new Promise((resolve, reject) => {
    console.log(`$ ${command} ${args.join(" ")}`);
    const child = spawn(command, args, {
      cwd: options.cwd || repoRoot,
      env: options.env || process.env,
      stdio: ["ignore", "pipe", "pipe"]
    });

    let output = "";
    child.stdout.on("data", (chunk) => {
      process.stdout.write(chunk);
      output += chunk;
    });
    child.stderr.on("data", (chunk) => {
      process.stderr.write(chunk);
      output += chunk;
    });
    child.on("error", reject);
    child.on("exit", (code, signal) => {
      if (code === 0) {
        resolve(output);
        return;
      }

      reject(new Error(`${command} ${args.join(" ")} failed with code=${code ?? "null"} signal=${signal ?? "null"}.`));
    });
  });
}

function escapeXml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("\"", "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

process.on("exit", () => {
  if (serverProcess && serverProcess.exitCode === null) {
    serverProcess.kill("SIGKILL");
  }
});
