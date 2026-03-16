const fs = require("fs");
const path = require("path");
const { test, expect } = require("@playwright/test");

function readJsonConfig(fileName) {
  const fullPath = path.resolve(__dirname, "../../../demo/AgentBlazor.Demo", fileName);
  if (!fs.existsSync(fullPath)) {
    return {};
  }

  try {
    return JSON.parse(fs.readFileSync(fullPath, "utf8"));
  } catch {
    return {};
  }
}

function hasConfiguredProvider() {
  if (process.env.OPENAI_API_KEY || process.env.OLLAMA_MODEL) {
    return true;
  }

  const configs = [
    readJsonConfig("appsettings.Development.json"),
    readJsonConfig("appsettings.json")
  ];

  return configs.some(config =>
    Boolean(config?.OpenAI?.ApiKey) ||
    Boolean(config?.Ollama?.Model));
}

async function sendPrompt(page, prompt) {
  const input = page.locator(".dojo-stage--chat-panel textarea[aria-label='Message input']").first();
  await expect(input).toBeVisible();
  await input.fill(prompt);
  await page.getByRole("button", { name: /send message/i }).first().click();
}

async function openInspector(page) {
  const inspector = page.locator(".dojo-stage--chat-panel .ab-inspector--inline");
  if (!(await inspector.isVisible().catch(() => false))) {
    await page.getByRole("button", { name: /open agent inspector/i }).first().click();
  }

  await expect(inspector).toBeVisible();
  await expect(inspector.getByRole("tab", { name: "Runs" })).toBeVisible();
  return inspector;
}

async function refreshInspectorRuns(page) {
  const inspector = await openInspector(page);
  await inspector.getByRole("tab", { name: "Runs" }).click();

  const refreshButton = inspector.getByRole("button", { name: /^refresh$/i });
  for (let attempt = 0; attempt < 5; attempt += 1) {
    if (await inspector.locator(".ab-inspector__run-item").first().isVisible().catch(() => false)) {
      return inspector;
    }

    await refreshButton.click();
    await page.waitForTimeout(1000);
  }

  await expect(inspector.locator(".ab-inspector__run-item").first()).toBeVisible({ timeout: 60000 });
  return inspector;
}

async function getLatestRunEventText(page) {
  const inspector = await refreshInspectorRuns(page);

  const runList = inspector.locator(".ab-inspector__run-item");
  await runList.first().click();

  const eventsTab = inspector.getByRole("tab", { name: "Events" });
  await eventsTab.click();
  await expect(eventsTab).toHaveClass(/ab-inspector__tab--active/, { timeout: 30000 });
  await expect(inspector.locator(".ab-inspector__run-summary")).toBeVisible({ timeout: 30000 });
  await expect(inspector.locator(".ab-inspector__event-list").first()).toBeVisible({ timeout: 30000 });

  return (await inspector.locator(".ab-inspector__body").textContent()) ?? "";
}

async function expectLatestRunEventText(page, contains, excludes = []) {
  const eventText = await getLatestRunEventText(page);

  for (const expected of contains) {
    expect(eventText).toContain(expected);
  }

  for (const blocked of excludes) {
    expect(eventText).not.toContain(blocked);
  }

  return eventText;
}

async function expectLatestRunEvents(page, expectedActionIds) {
  return expectLatestRunEventText(
    page,
    ["PlannedAction", ...expectedActionIds],
    ["ValidationFailed", "PolicyBlocked"]);
}

async function expectOwner(page, owner) {
  await expect(page.locator(".dojo-key-stat").nth(1)).toContainText(new RegExp(owner, "i"), { timeout: 60000 });
}

async function expectSeverity(page, severity) {
  await expect(page.locator(".dojo-key-stat").nth(0)).toContainText(new RegExp(severity, "i"), { timeout: 60000 });
}

async function expectIncident(page, text) {
  await expect(page.locator(".dojo-controlled__card")).toContainText(text, { timeout: 60000 });
}

async function expectHostedSurface(page, expectedTitle, expectedOriginLabel) {
  await expect(page.locator(".dojo-open-ended__shell")).toContainText(expectedOriginLabel, { timeout: 60000 });
  const frame = page.frameLocator("iframe.dojo-open-ended__frame");
  await expect(frame.locator("body")).toContainText(expectedTitle, { timeout: 60000 });
}

const hasProvider = hasConfiguredProvider();

test.describe("Dojo prompt inspector", () => {
  test.skip(!hasProvider, "No model provider configured for prompt-backed inspector tests.");

  test("validates the controlled pillar prompt matrix end to end", async ({ page }) => {
    test.setTimeout(240000);

    await page.goto("/demo/dojo", { waitUntil: "networkidle" });
    await openInspector(page);

    await test.step("changes owner with a direct owner prompt", async () => {
      await sendPrompt(page, "change owner to steve");
      await expectOwner(page, "Steve");
      await expectLatestRunEvents(page, ["fill_owner"]);
    });

    await test.step("updates severity and incident title", async () => {
      await sendPrompt(page, "make this a P1 checkout incident");
      await expectSeverity(page, "P1");
      await expectIncident(page, "Checkout");
      await expectLatestRunEvents(page, ["update_controlled_incident_draft"]);
    });

    await test.step("assigns the draft to a named owner", async () => {
      await sendPrompt(page, "assign the draft to Ash");
      await expectOwner(page, "Ash");
      await expectLatestRunEvents(page, ["assign_draft"]);
    });

    await test.step("queues the review workflow step", async () => {
      await sendPrompt(page, "queue review");
      await expect(page.locator(".dojo-controlled__card")).toContainText("Review queued", { timeout: 60000 });
      await expect(page.locator(".dojo-feed")).toContainText("Review queued for Ash.", { timeout: 60000 });
      await expectLatestRunEvents(page, ["queue_controlled_incident_review"]);
    });

    await test.step("handles a combined edit prompt without losing deterministic execution", async () => {
      await sendPrompt(page, "make this a P1 checkout incident and assign it to Steve");
      await expectSeverity(page, "P1");
      await expectIncident(page, "Checkout");
      await expectOwner(page, "Steve");
      await expectLatestRunEvents(page, ["update_controlled_incident_draft"]);
    });
  });

  test("validates the declarative pillar prompt flow across native and imported protocols", async ({ page }) => {
    test.setTimeout(240000);

    await page.goto("/demo/dojo", { waitUntil: "networkidle" });
    await openInspector(page);

    await test.step("switches into the declarative pillar", async () => {
      await sendPrompt(page, "switch to declarative generative ui");
      await expect(page.getByRole("heading", { name: "Blazor-Rendered Generative UI" })).toBeVisible({ timeout: 60000 });
      await expect(page.locator(".dojo-generated-surface")).toContainText("Release Summary", { timeout: 60000 });
      await expectLatestRunEvents(page, ["select_dojo_pillar"]);
    });

    await test.step("switches to a2ui import by prompt", async () => {
      await sendPrompt(page, "switch to a2ui import");
      await expect(page.getByRole("heading", { name: "Imported from A2UI" })).toBeVisible({ timeout: 60000 });
      await expect(page.locator(".dojo-generated-surface")).toContainText("Restock Draft", { timeout: 60000 });
      await expectLatestRunEvents(page, ["switch_declarative_protocol"]);
    });

    await test.step("switches to open-json-ui import by prompt", async () => {
      await sendPrompt(page, "switch to open-json-ui import");
      await expect(page.getByRole("heading", { name: "Imported from Open-JSON-UI" })).toBeVisible({ timeout: 60000 });
      await expect(page.locator(".dojo-generated-surface")).toContainText("Release Summary", { timeout: 60000 });
      await expectLatestRunEvents(page, ["switch_declarative_protocol"]);
    });

    await test.step("switches back to native declarative rendering", async () => {
      await sendPrompt(page, "switch back to native");
      await expect(page.getByRole("heading", { name: "Blazor-Rendered Generative UI" })).toBeVisible({ timeout: 60000 });
      await expect(page.locator(".dojo-generated-surface")).toContainText("Launch Draft", { timeout: 60000 });
      await expectLatestRunEvents(page, ["switch_declarative_protocol"]);
    });
  });

  test("validates the open-ended pillar prompt flow across hosted surfaces", async ({ page }) => {
    test.setTimeout(240000);

    await page.goto("/demo/dojo", { waitUntil: "networkidle" });
    await openInspector(page);

    await test.step("switches into the open-ended pillar", async () => {
      await sendPrompt(page, "switch to open-ended generative ui");
      await expect(page.locator(".dojo-open-ended__frame")).toBeVisible({ timeout: 60000 });
      await expectHostedSurface(page, "Checkout Health", "analytics-cockpit");
      await expectLatestRunEvents(page, ["select_dojo_pillar"]);
    });

    await test.step("switches to planner workspace by prompt", async () => {
      await sendPrompt(page, "switch to planner workspace");
      await expectHostedSurface(page, "Launch Board", "planner-workspace");
      await expectLatestRunEvents(page, ["switch_open_ended_surface"]);
    });

    await test.step("switches to support workspace by prompt", async () => {
      await sendPrompt(page, "switch to support workspace");
      await expectHostedSurface(page, "Escalation Queue", "support-workspace");
      await expectLatestRunEvents(page, ["switch_open_ended_surface"]);
    });

    await test.step("switches back to analytics cockpit by prompt", async () => {
      await sendPrompt(page, "open the analytics cockpit");
      await expectHostedSurface(page, "Checkout Health", "analytics-cockpit");
      await expectLatestRunEvents(page, ["switch_open_ended_surface"]);
    });
  });
  test("keeps prompt execution stable while switching across pillars", async ({ page }) => {
    test.setTimeout(300000);

    await page.goto("/demo/dojo", { waitUntil: "networkidle" });
    await openInspector(page);

    await test.step("executes a controlled prompt before any pillar switching", async () => {
      await sendPrompt(page, "change owner to steve");
      await expectOwner(page, "Steve");
      await expectLatestRunEvents(page, ["fill_owner"]);
    });

    await test.step("switches to declarative and uses an imported protocol", async () => {
      await sendPrompt(page, "switch to declarative generative ui");
      await expect(page.getByRole("heading", { name: "Blazor-Rendered Generative UI" })).toBeVisible({ timeout: 60000 });
      await expectLatestRunEvents(page, ["select_dojo_pillar"]);

      await sendPrompt(page, "switch to open-json-ui import");
      await expect(page.getByRole("heading", { name: "Imported from Open-JSON-UI" })).toBeVisible({ timeout: 60000 });
      await expect(page.locator(".dojo-generated-surface")).toContainText("Release Summary", { timeout: 60000 });
      await expectLatestRunEvents(page, ["switch_declarative_protocol"]);
    });

    await test.step("switches to open-ended and changes the hosted surface", async () => {
      await sendPrompt(page, "switch to open-ended generative ui");
      await expect(page.locator(".dojo-open-ended__frame")).toBeVisible({ timeout: 60000 });
      await expectHostedSurface(page, "Checkout Health", "analytics-cockpit");
      await expectLatestRunEvents(page, ["select_dojo_pillar"]);

      await sendPrompt(page, "switch to support workspace");
      await expectHostedSurface(page, "Escalation Queue", "support-workspace");
      await expectLatestRunEvents(page, ["switch_open_ended_surface"]);
    });

    await test.step("returns to controlled and still executes deterministic actions", async () => {
      await sendPrompt(page, "switch to controlled generative ui");
      await expect(page.locator(".dojo-controlled__label")).toContainText("Incident review draft", { timeout: 60000 });
      await expect(page.locator(".dojo-controlled__card")).not.toContainText("Release Summary");
      await expect(page.locator(".dojo-open-ended__shell")).toHaveCount(0);
      await expectLatestRunEvents(page, ["select_dojo_pillar"]);

      await sendPrompt(page, "assign the draft to Ash");
      await expectOwner(page, "Ash");
      await expectLatestRunEvents(page, ["assign_draft"]);
    });
  });
  test("handles unsupported and clarification prompts without validation loops", async ({ page }) => {
    test.setTimeout(300000);

    await page.goto("/demo/dojo", { waitUntil: "networkidle" });
    await openInspector(page);

    await test.step("returns a clean refusal for unsupported requests", async () => {
      await sendPrompt(page, "book me a flight to Paris");
      await expect(page.locator(".dojo-stage--chat-panel [role='log']")).toContainText("I can't assist with booking flights.", { timeout: 60000 });
      await expectLatestRunEventText(
        page,
        ["PlanEmpty", "PlanningFinished"],
        ["PlannedAction", "ValidationFailed", "PolicyBlocked", "Component '"]);
    });

    await test.step("asks for more detail, then recovers into a deterministic action", async () => {
      await sendPrompt(page, "do something else");
      await expect(page.locator(".dojo-stage--chat-panel [role='log']")).toContainText(/What specific action would you like to take\?|Please specify what you would like to do next\./i, { timeout: 60000 });

      const eventText = await getLatestRunEventText(page);
      expect(eventText).toContain("PlanningFinished");
      expect(eventText).not.toContain("ValidationFailed");
      expect(eventText).not.toContain("PolicyBlocked");
      expect(eventText).not.toContain("Component '");
      expect(eventText.includes("ClarificationRequired") || eventText.includes("PlanEmpty")).toBeTruthy();

      const clarificationInput = page.getByRole("textbox", { name: "Your answer:" });
      if (await clarificationInput.isVisible().catch(() => false)) {
        await clarificationInput.fill("queue review");
        await page.getByRole("button", { name: /^submit$/i }).click();
      } else {
        await sendPrompt(page, "queue review");
      }

      await expect(page.locator(".dojo-controlled__card")).toContainText("Review queued", { timeout: 60000 });
      await expect(page.locator(".dojo-feed")).toContainText("Review queued for Ash.", { timeout: 60000 });
      await expectLatestRunEvents(page, ["queue_controlled_incident_review"]);
    });
  });
});


