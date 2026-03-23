#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");
const { chromium } = require("@playwright/test");
const { openAssistantChatSurface } = require("../specs/chat-helpers.cjs");

const repoRoot = path.resolve(__dirname, "..", "..", "..");
const promptsPath = path.join(repoRoot, "tests", "e2e", "real-usability.prompts.json");
const baselinePath = path.join(repoRoot, "tests", "e2e", "real-usability-baseline.json");

const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outputDir = path.join(repoRoot, "tests", "e2e", "artifacts", "real-usability", stamp);
const serverLogPath = path.join(outputDir, "demo-server.log");
const baseUrl = process.env.REAL_USABILITY_BASE_URL || "http://127.0.0.1:5190";
const defaultScenarioRoute = process.env.REAL_USABILITY_DEFAULT_ROUTE || "/demo/workflows/response-orchestration?reset=true";
const scenarioTimeoutMs = Number.parseInt(process.env.REAL_USABILITY_SCENARIO_TIMEOUT_MS || "90000", 10);
const serverReadyTimeoutMs = Number.parseInt(process.env.REAL_USABILITY_SERVER_TIMEOUT_MS || "180000", 10);

const providerConfigured = isProviderConfigured();

if (!providerConfigured) {
  console.error(
    "Real usability run requires a live provider. Configure OpenAI or Ollama via environment variables or demo appsettings."
  );
  process.exit(1);
}

fs.mkdirSync(outputDir, { recursive: true });

const scenarios = JSON.parse(fs.readFileSync(promptsPath, "utf8"));
const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));

let serverProcess;

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function run() {
  serverProcess = startDemoServer();
  await waitForServerReady(baseUrl, serverReadyTimeoutMs);

  const browser = await chromium.launch({ headless: true });
  const results = [];

  try {
    for (const scenario of scenarios) {
      const result = await runScenario(browser, scenario);
      results.push(result);
    }
  } finally {
    await browser.close();
    await stopDemoServer(serverProcess);
  }

  const summary = summarizeResults(results);
  const regression = evaluateRegression(summary, baseline, results);
  const report = {
    generatedAtUtc: new Date().toISOString(),
    baseUrl,
    outputDir,
    baseline,
    summary,
    regression,
    results
  };

  const reportJsonPath = path.join(outputDir, "report.json");
  fs.writeFileSync(reportJsonPath, JSON.stringify(report, null, 2));

  const reportMarkdownPath = path.join(outputDir, "report.md");
  fs.writeFileSync(reportMarkdownPath, buildMarkdownReport(report));

  console.log(`Real usability report: ${reportMarkdownPath}`);
  console.log(
    `Pass rate: ${(summary.passRate * 100).toFixed(1)}% (${summary.passed}/${summary.total}), failures: ${summary.failed}`
  );
  if (summary.topFailureClasses.length > 0) {
    console.log(
      `Top failure classes: ${summary.topFailureClasses
        .map((entry) => `${entry.cause} (${entry.count})`)
        .join(", ")}`
    );
  }

  if (regression.isRegression) {
    console.error("Regression detected:");
    for (const reason of regression.reasons) {
      console.error(`- ${reason}`);
    }
    process.exitCode = 1;
    return;
  }

  process.exitCode = 0;
}

function startDemoServer() {
  const projectPath = path.join(repoRoot, "demo", "AgentBlazor.Demo", "AgentBlazor.Demo.csproj");
  const args = ["run", "--project", projectPath, "--urls", baseUrl];
  const child = spawn("dotnet", args, {
    cwd: repoRoot,
    env: process.env,
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
  const endpoint = `${url}${defaultScenarioRoute}`;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(endpoint, { method: "GET" });
      if (response.ok || response.status === 404) {
        return;
      }
    } catch {
      // Retry until timeout.
    }

    await sleep(1000);
  }

  throw new Error(`Demo server did not become ready within ${timeoutMs}ms.`);
}

async function runScenario(browser, scenario) {
  const scenarioDir = path.join(outputDir, scenario.id);
  fs.mkdirSync(scenarioDir, { recursive: true });
  const route = scenario.route || defaultScenarioRoute;

  const beforeDb = await snapshotDatabase();
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  const transcript = [];
  const interactions = {
    approvalsClicked: 0,
    clarificationsSubmitted: 0
  };

  let evidence;
  let evaluation;
  let afterDb;

  try {
    await page.goto(`${baseUrl}${route}`, { waitUntil: "networkidle", timeout: scenarioTimeoutMs });

    const chatSurface = await openAssistantChatSurface(page, 30000);
    const promptInput = chatSurface.getByLabel("Message input");
    await promptInput.waitFor({ state: "visible", timeout: 30000 });

    await promptInput.fill(scenario.prompt);
    await clickSend(chatSurface);

    await waitForSettled(chatSurface, scenarioTimeoutMs);
    await resolveHumanInLoop(chatSurface, interactions, scenarioTimeoutMs);
    await waitForSettled(chatSurface, scenarioTimeoutMs);

    evidence = await extractChatEvidence(chatSurface);
    transcript.push(...evidence.transcript);

    afterDb = await snapshotDatabase();
    const dbDiff = computeDatabaseDiff(beforeDb, afterDb);
    evaluation = evaluateScenario(scenario, evidence, dbDiff, interactions);

    await page.screenshot({ path: path.join(scenarioDir, "final.png"), fullPage: true });

    const scenarioReport = {
      scenario,
      interactions,
      evidence,
      database: {
        before: beforeDb,
        after: afterDb,
        diff: dbDiff
      },
      evaluation
    };
    fs.writeFileSync(path.join(scenarioDir, "evidence.json"), JSON.stringify(scenarioReport, null, 2));

    return {
      scenarioId: scenario.id,
      scenarioName: scenario.name,
      route,
      prompt: scenario.prompt,
      pass: evaluation.pass,
      failures: evaluation.failures,
      rootCauses: evaluation.rootCauses,
      interactions,
      metrics: {
        generatedBlockCount: evidence.generated.blockCount,
        generatedBlockTypes: evidence.generated.uniqueTypes,
        plannedActionsCount: evidence.plannedActions.length,
        executionOutcomeCount: evidence.executionOutcomes.length,
        clarificationCount: evidence.clarificationCount,
        approvalCount: evidence.approvalCount,
        maxDeferredRepeat: evidence.maxDeferredRepeat
      },
      databaseDiff: computeDatabaseDiff(beforeDb, afterDb),
      evidencePath: path.relative(repoRoot, path.join(scenarioDir, "evidence.json")),
      screenshotPath: path.relative(repoRoot, path.join(scenarioDir, "final.png")),
      transcript
    };
  } catch (error) {
    await page.screenshot({ path: path.join(scenarioDir, "error.png"), fullPage: true }).catch(() => {});
    const failure = {
      scenarioId: scenario.id,
      scenarioName: scenario.name,
      route,
      prompt: scenario.prompt,
      pass: false,
      failures: [{ code: "scenario_runtime_failure", message: error.message }],
      rootCauses: ["scenario_runtime_failure"],
      interactions,
      metrics: {
        generatedBlockCount: 0,
        generatedBlockTypes: [],
        plannedActionsCount: 0,
        executionOutcomeCount: 0,
        clarificationCount: 0,
        approvalCount: 0,
        maxDeferredRepeat: 0
      },
      databaseDiff: {
        countDeltas: {}
      },
      evidencePath: path.relative(repoRoot, path.join(scenarioDir, "error.json")),
      screenshotPath: path.relative(repoRoot, path.join(scenarioDir, "error.png")),
      transcript
    };
    fs.writeFileSync(path.join(scenarioDir, "error.json"), JSON.stringify({ error: error.message }, null, 2));
    return failure;
  } finally {
    await context.close();
  }
}

async function clickSend(chatSurface) {
  const sendButton = chatSurface.locator("button[aria-label*='Send']").first();
  await sendButton.waitFor({ state: "visible", timeout: 30000 });
  await sendButton.click();
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

  throw new Error("Scenario did not settle before timeout.");
}

async function resolveHumanInLoop(chatSurface, interactions, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const approveButton = chatSurface.getByRole("button", { name: "Approve" }).first();
    if (await approveButton.isVisible().catch(() => false)) {
      await approveButton.click();
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
      interactions.clarificationsSubmitted++;
      await waitForSettled(chatSurface, timeoutMs);
      continue;
    }

    return;
  }
}

async function extractChatEvidence(chatSurface) {
  return await chatSurface.evaluate((surface) => {
    if (!surface) {
      return {
        transcript: [],
        plannedActions: [],
        executionOutcomes: [],
        generated: { blockCount: 0, uniqueTypes: [], countsByType: {} },
        clarificationCount: 0,
        approvalCount: 0,
        maxDeferredRepeat: 0,
        componentUnavailableCount: 0,
        runtimeErrorCount: 0
      };
    }

    const items = [...surface.querySelectorAll(".ab-chat-surface__item")].map((item) => {
      let role = "assistant";
      if (item.classList.contains("ab-chat-surface__item--user")) {
        role = "user";
      } else if (item.classList.contains("ab-chat-surface__item--activity")) {
        role = "activity";
      } else if (item.classList.contains("ab-chat-surface__item--clarification")) {
        role = "clarification";
      } else if (item.classList.contains("ab-chat-surface__item--approval")) {
        role = "approval";
      }

      const roleLabel = item.querySelector(".ab-chat-surface__item-role")?.textContent?.trim() ?? role;
      const text = item.querySelector(".ab-chat-surface__item-text")?.textContent?.trim() ?? "";
      const plannedActions = [...item.querySelectorAll(".ab-chat-surface__actions li")]
        .map((entry) => entry.textContent?.trim())
        .filter(Boolean);
      const resultLines = [...item.querySelectorAll(".ab-chat-surface__results li")]
        .map((entry) => entry.textContent?.trim())
        .filter(Boolean);
      const generatedBlockTypes = [...item.querySelectorAll(".ab-generative-surface__block")].map((block) => {
        if (block.querySelector(".ab-generated-card")) {
          return "card";
        }
        if (block.querySelector(".ab-generated-form")) {
          return "form";
        }
        if (block.querySelector(".ab-generated-chart")) {
          return "chart";
        }
        if (block.querySelector(".ab-generated-table")) {
          return "table";
        }
        return "unknown";
      });

      return {
        role,
        roleLabel,
        text,
        plannedActions,
        resultLines,
        generatedBlockTypes
      };
    });

    const transcript = items
      .filter((entry) => entry.role === "user" || entry.role === "assistant")
      .map((entry) => ({
        role: entry.role,
        roleLabel: entry.roleLabel,
        text: entry.text
      }));

    const plannedActions = items.flatMap((entry) => entry.plannedActions);
    const executionOutcomes = items.flatMap((entry) => entry.resultLines);
    const generatedBlockTypes = items.flatMap((entry) => entry.generatedBlockTypes);
    const countsByType = {};
    for (const type of generatedBlockTypes) {
      countsByType[type] = (countsByType[type] ?? 0) + 1;
    }

    const deferredMessages = executionOutcomes.filter((line) =>
      line.toLowerCase().startsWith("deferred action applied:")
    );
    const deferredKeyCounts = {};
    for (const line of deferredMessages) {
      const match = line.match(/Deferred action applied:\s*([^.]+\.[^.]+)\./i);
      const key = match?.[1]?.toLowerCase() ?? line.toLowerCase();
      deferredKeyCounts[key] = (deferredKeyCounts[key] ?? 0) + 1;
    }
    const maxDeferredRepeat = Object.values(deferredKeyCounts).length
      ? Math.max(...Object.values(deferredKeyCounts))
      : 0;

    const clarificationCount =
      items.filter((entry) => entry.role === "clarification").length +
      items.filter(
        (entry) =>
          entry.role === "assistant" &&
          /\b(which|what|could you|please specify|clarify)\b/i.test(entry.text)
      ).length;

    const approvalCount =
      items.filter((entry) => entry.role === "approval").length +
      items.filter(
        (entry) => entry.role === "assistant" && /\bapproval required\b/i.test(entry.text)
      ).length;

    const componentUnavailableCount = executionOutcomes.filter((line) =>
      /not available or not allowed/i.test(line)
    ).length;

    const runtimeErrorCount = executionOutcomes.filter((line) =>
      /(generated ui action failed|unable to|exception|runtime error)/i.test(line)
    ).length;

    return {
      transcript,
      plannedActions,
      executionOutcomes,
      generated: {
        blockCount: generatedBlockTypes.length,
        uniqueTypes: [...new Set(generatedBlockTypes)],
        countsByType
      },
      clarificationCount,
      approvalCount,
      maxDeferredRepeat,
      componentUnavailableCount,
      runtimeErrorCount
    };
  });
}

function evaluateScenario(scenario, evidence, dbDiff, interactions) {
  const failures = [];
  const rootCauses = new Set();

  const minGeneratedBlocks = scenario.minGeneratedBlocks ?? 1;
  if (evidence.generated.blockCount < minGeneratedBlocks) {
    failures.push({
      code: "missing_generated_ui",
      message: `Expected at least ${minGeneratedBlocks} generated blocks, got ${evidence.generated.blockCount}.`
    });
    rootCauses.add("missing_generated_ui");
  }

  const requiredBlockTypes = scenario.requiredBlockTypes ?? [];
  const missingBlockTypes = requiredBlockTypes.filter(
    (type) => !evidence.generated.uniqueTypes.includes(type)
  );
  if (missingBlockTypes.length > 0) {
    failures.push({
      code: "missing_required_block_types",
      message: `Missing required block types: ${missingBlockTypes.join(", ")}.`
    });
    rootCauses.add("missing_required_block_types");
  }

  if (scenario.requiresActions && evidence.plannedActions.length === 0) {
    failures.push({
      code: "no_component_actions",
      message: "No planned component actions were rendered."
    });
    rootCauses.add("no_component_actions");
  }

  const maxClarifications = scenario.maxClarifications ?? 1;
  if (evidence.clarificationCount > maxClarifications) {
    failures.push({
      code: "excessive_clarifications",
      message: `Clarification count ${evidence.clarificationCount} exceeded max ${maxClarifications}.`
    });
    rootCauses.add("excessive_clarifications");
  }

  const maxDeferredRepeats = scenario.maxDeferredRepeats ?? 2;
  if (evidence.maxDeferredRepeat > maxDeferredRepeats) {
    failures.push({
      code: "repeated_deferred_loop",
      message: `Deferred action repeat ${evidence.maxDeferredRepeat} exceeded max ${maxDeferredRepeats}.`
    });
    rootCauses.add("repeated_deferred_loop");
  }

  if (evidence.componentUnavailableCount > 0) {
    failures.push({
      code: "component_unavailable",
      message: `Found ${evidence.componentUnavailableCount} component availability errors.`
    });
    rootCauses.add("component_unavailable");
  }

  if (evidence.runtimeErrorCount > 0) {
    failures.push({
      code: "runtime_error",
      message: `Found ${evidence.runtimeErrorCount} runtime error messages in outcomes.`
    });
    rootCauses.add("runtime_error");
  }

  if ((scenario.maxApprovalInteractions ?? 3) < interactions.approvalsClicked) {
    failures.push({
      code: "approval_loop",
      message: `Approval interactions ${interactions.approvalsClicked} exceeded configured cap.`
    });
    rootCauses.add("approval_loop");
  }

  if (scenario.expectedDbDelta) {
    for (const [table, minDelta] of Object.entries(scenario.expectedDbDelta)) {
      const actual = dbDiff.countDeltas[table] ?? 0;
      if (actual < minDelta) {
        failures.push({
          code: "missing_db_effect",
          message: `Expected DB delta for ${table} >= ${minDelta}, got ${actual}.`
        });
        rootCauses.add("missing_db_effect");
      }
    }
  }

  return {
    pass: failures.length === 0,
    failures,
    rootCauses: [...rootCauses]
  };
}

function summarizeResults(results) {
  const passed = results.filter((entry) => entry.pass).length;
  const failed = results.length - passed;
  const passRate = results.length === 0 ? 0 : passed / results.length;

  const rootCauseCounts = {};
  for (const result of results) {
    for (const cause of result.rootCauses ?? []) {
      rootCauseCounts[cause] = (rootCauseCounts[cause] ?? 0) + 1;
    }
  }

  const topFailureClasses = Object.entries(rootCauseCounts)
    .map(([cause, count]) => ({ cause, count }))
    .sort((left, right) => right.count - left.count);

  return {
    total: results.length,
    passed,
    failed,
    passRate,
    rootCauseCounts,
    topFailureClasses
  };
}

function evaluateRegression(summary, baseline, results) {
  const reasons = [];

  if (summary.passRate < baseline.minPassRate) {
    reasons.push(
      `Pass rate ${(summary.passRate * 100).toFixed(1)}% is below baseline ${(baseline.minPassRate * 100).toFixed(
        1
      )}%.`
    );
  }

  if (summary.failed > baseline.maxFailures) {
    reasons.push(`Failure count ${summary.failed} is above baseline maxFailures ${baseline.maxFailures}.`);
  }

  for (const scenarioId of baseline.requiredScenarioPasses ?? []) {
    const scenario = results.find((entry) => entry.scenarioId === scenarioId);
    if (!scenario || !scenario.pass) {
      reasons.push(`Required scenario '${scenarioId}' did not pass.`);
    }
  }

  for (const [cause, maxCount] of Object.entries(baseline.maxRootCauseCounts ?? {})) {
    const actual = summary.rootCauseCounts[cause] ?? 0;
    if (actual > maxCount) {
      reasons.push(`Root cause '${cause}' count ${actual} exceeded baseline ${maxCount}.`);
    }
  }

  return {
    isRegression: reasons.length > 0,
    reasons
  };
}

function buildMarkdownReport(report) {
  const lines = [];
  lines.push("# Real Usability Report");
  lines.push("");
  lines.push(`Generated: ${report.generatedAtUtc}`);
  lines.push(`Base URL: ${report.baseUrl}`);
  lines.push("");
  lines.push("## Summary");
  lines.push("");
  lines.push(`- Total scenarios: ${report.summary.total}`);
  lines.push(`- Passed: ${report.summary.passed}`);
  lines.push(`- Failed: ${report.summary.failed}`);
  lines.push(`- Pass rate: ${(report.summary.passRate * 100).toFixed(1)}%`);
  lines.push(
    `- Regression gate: ${report.regression.isRegression ? "FAILED" : "PASSED"}`
  );
  lines.push("");

  lines.push("## Scenario Results");
  lines.push("");
  lines.push("| Scenario | Route | Pass | Generated Blocks | Block Types | Clarifications | Deferred Repeat | Root Causes |");
  lines.push("|---|---|---:|---:|---|---:|---:|---|");
  for (const result of report.results) {
    lines.push(
      `| ${result.scenarioName} | ${result.route} | ${result.pass ? "yes" : "no"} | ${result.metrics.generatedBlockCount} | ${result.metrics.generatedBlockTypes.join(", ") || "-"} | ${result.metrics.clarificationCount} | ${result.metrics.maxDeferredRepeat} | ${result.rootCauses.join(", ") || "-"} |`
    );
  }
  lines.push("");

  lines.push("## Ranked Failure Classes");
  lines.push("");
  if (report.summary.topFailureClasses.length === 0) {
    lines.push("- none");
  } else {
    for (const entry of report.summary.topFailureClasses) {
      lines.push(`- ${entry.cause}: ${entry.count}`);
    }
  }
  lines.push("");

  if (report.regression.reasons.length > 0) {
    lines.push("## Regression Reasons");
    lines.push("");
    for (const reason of report.regression.reasons) {
      lines.push(`- ${reason}`);
    }
    lines.push("");
  }

  return `${lines.join("\n")}\n`;
}

async function snapshotDatabase() {
  const dbPath = resolveDatabasePath();
  const script = `
import json
import os
import sqlite3
import sys

db_path = sys.argv[1]
payload = {"dbPath": db_path, "exists": os.path.exists(db_path), "counts": {}, "latestRows": {}, "errors": {}}

if payload["exists"]:
    conn = sqlite3.connect(db_path)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()
    tables = [
        "dojo_workspaces",
        "dojo_ingredients",
        "dojo_steps",
        "dojo_run_notes",
        "demo_file_workflow_files",
        "demo_file_workflow_events",
        "demo_file_workflow_jobs"
    ]
    for table in tables:
        try:
            cur.execute(f"SELECT COUNT(*) AS count FROM {table}")
            payload["counts"][table] = int(cur.fetchone()["count"])
            cur.execute(f"SELECT * FROM {table} ORDER BY rowid DESC LIMIT 3")
            rows = []
            for row in cur.fetchall():
                rows.append({k: row[k] for k in row.keys()})
            payload["latestRows"][table] = rows
        except Exception as ex:
            payload["errors"][table] = str(ex)
    conn.close()

print(json.dumps(payload))
`;

  const output = await runPython(script, [dbPath]);
  if (!output.ok) {
    return {
      dbPath,
      exists: false,
      counts: {},
      latestRows: {},
      errors: { snapshot: output.stderr || "failed to snapshot database" }
    };
  }

  return JSON.parse(output.stdout);
}

function resolveDatabasePath() {
  const explicit = process.env.AGENTBLAZOR_DEMO_DB_PATH;
  if (explicit) {
    return explicit;
  }

  const candidates = [
    path.join(repoRoot, "agentblazor-demo.db"),
    path.join(repoRoot, "demo", "AgentBlazor.Demo", "agentblazor-demo.db")
  ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  return candidates[0];
}

function computeDatabaseDiff(before, after) {
  const tables = new Set([
    ...Object.keys(before?.counts ?? {}),
    ...Object.keys(after?.counts ?? {})
  ]);
  const countDeltas = {};
  for (const table of tables) {
    const beforeCount = Number(before?.counts?.[table] ?? 0);
    const afterCount = Number(after?.counts?.[table] ?? 0);
    countDeltas[table] = afterCount - beforeCount;
  }

  return {
    beforeCounts: before?.counts ?? {},
    afterCounts: after?.counts ?? {},
    countDeltas
  };
}

async function runPython(script, args = []) {
  const candidates = process.platform === "win32"
    ? [
        { cmd: "python", args: ["-c", script, ...args] },
        { cmd: "py", args: ["-3", "-c", script, ...args] },
        { cmd: "python3", args: ["-c", script, ...args] }
      ]
    : [
        { cmd: "python3", args: ["-c", script, ...args] },
        { cmd: "python", args: ["-c", script, ...args] }
      ];

  for (const candidate of candidates) {
    const result = await runCommand(candidate.cmd, candidate.args);
    if (result.ok) {
      return result;
    }
  }

  return {
    ok: false,
    stdout: "",
    stderr: "Python executable was not available for DB snapshot."
  };
}

async function runCommand(command, args) {
  return new Promise((resolve) => {
    const child = spawn(command, args, {
      cwd: repoRoot,
      env: process.env
    });

    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString();
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });

    child.on("error", (error) => {
      resolve({
        ok: false,
        stdout,
        stderr: stderr + error.message
      });
    });

    child.on("close", (code) => {
      resolve({
        ok: code === 0,
        stdout: stdout.trim(),
        stderr: stderr.trim()
      });
    });
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function isProviderConfigured() {
  if (
    process.env.OPENAI_API_KEY ||
    process.env.OpenAI__ApiKey ||
    process.env.OLLAMA_MODEL ||
    process.env.Ollama__Model
  ) {
    return true;
  }

  try {
    const appSettingsPath = path.join(repoRoot, "demo", "AgentBlazor.Demo", "appsettings.json");
    const appSettings = JSON.parse(fs.readFileSync(appSettingsPath, "utf8"));
    const configuredOpenAiKey = appSettings?.OpenAI?.ApiKey;
    const configuredOllamaModel = appSettings?.Ollama?.Model;
    return Boolean(configuredOpenAiKey || configuredOllamaModel);
  } catch {
    return false;
  }
}
