#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");
const { chromium, expect } = require("@playwright/test");
const { openFloatingChatWidget } = require("../specs/chat-helpers.cjs");

const repoRoot = path.resolve(__dirname, "..", "..", "..");
const packageVersion = process.env.AGENTBLAZOR_PACKAGE_VERSION || readPackageVersion();
const externalRepoUrl = process.env.AGENTBLAZOR_EXTERNAL_REPO || "https://github.com/damienbod/BlazorSecurityNet10.git";
const externalRepoRef = process.env.AGENTBLAZOR_EXTERNAL_REF || "";
const externalProjectRelativePath = process.env.AGENTBLAZOR_EXTERNAL_PROJECT || "BlazorApp/BlazorApp.csproj";
const baseUrl = process.env.AGENTBLAZOR_EXTERNAL_BASE_URL || "http://127.0.0.1:5295";
const promptText = process.env.AGENTBLAZOR_EXTERNAL_PROMPT || "Can you explain what this Blazor app does?";
const timeoutMs = Number.parseInt(process.env.AGENTBLAZOR_EXTERNAL_TIMEOUT_MS || "180000", 10);
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outputRoot = process.env.AGENTBLAZOR_EXTERNAL_OUTPUT_DIR
  || path.join(repoRoot, "tests", "e2e", "artifacts", "external-chat-widget", stamp);
const workspaceRoot = path.join(outputRoot, "workspace");
const externalRoot = path.join(workspaceRoot, "external-app");
const localFeed = path.join(workspaceRoot, "local-feed");
const toolPath = path.join(workspaceRoot, "tools");
const serverLogPath = path.join(outputRoot, "external-app-server.log");
const reportPath = path.join(outputRoot, "report.json");
const screenshotPath = path.join(outputRoot, "chat-widget.png");

let serverProcess;

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function run() {
  fs.mkdirSync(outputRoot, { recursive: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });

  console.log(`External chat widget validation output: ${outputRoot}`);
  console.log(`Repository: ${externalRepoUrl}${externalRepoRef ? ` @ ${externalRepoRef}` : ""}`);
  console.log(`Project: ${externalProjectRelativePath}`);
  console.log(`AgentBlazor package version: ${packageVersion}`);

  await restoreRepo();
  await packLocalPackages();
  await cloneExternalApp();

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
    externalRepoUrl,
    externalRepoRef,
    externalProjectRelativePath,
    packageVersion,
    baseUrl,
    promptText,
    outputRoot,
    screenshotPath,
    assertions: {
      packageInstalled: true,
      cliScaffolded: true,
      doctorPassed: true,
      validatePassed: true,
      promptSubmitted: true,
      providerGuidanceRendered: true,
      minimizeButtonWorks: true,
      escapeMinimizes: true
    }
  };
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  console.log(`External chat widget report: ${reportPath}`);
  console.log(`External chat widget screenshot: ${screenshotPath}`);
}

function readPackageVersion() {
  const props = fs.readFileSync(path.join(repoRoot, "Directory.Build.props"), "utf8");
  const match = props.match(/<Version>([^<]+)<\/Version>/);
  if (!match) {
    throw new Error("Unable to read <Version> from Directory.Build.props.");
  }

  return match[1];
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

  try {
    await page.goto(baseUrl, { waitUntil: "networkidle", timeout: timeoutMs });
    const { widgetWindow, widgetSurface, minimizeButton, openButton } = await openFloatingChatWidget(page);

    const input = widgetSurface.getByLabel("Message input").first();
    const sendButton = widgetSurface.getByRole("button", { name: /send message/i }).first();
    await expect(input).toBeVisible();
    await input.fill(promptText);
    await expect(sendButton).toBeEnabled();
    await sendButton.click();

    await expect(widgetSurface.getByText(promptText, { exact: true }).first()).toBeVisible({ timeout: 30000 });
    await expect(widgetSurface.getByText(/No AI provider configured/i).first()).toBeVisible({ timeout: 30000 });

    await minimizeButton.click();
    await expect(widgetWindow).toBeHidden();
    await expect(openButton).toBeVisible();

    await openButton.click();
    await expect(widgetWindow).toBeVisible();

    await widgetWindow.press("Escape");
    await expect(widgetWindow).toBeHidden();
    await expect(openButton).toBeVisible();

    await page.screenshot({ path: screenshotPath, fullPage: true });
  } finally {
    await context.close().catch(() => {});
    await browser.close().catch(() => {});
  }
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
