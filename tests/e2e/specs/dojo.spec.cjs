const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface } = require("./chat-helpers.cjs");

async function clickExample(page, name) {
  const button = page.getByRole("button", { name: new RegExp(name, "i") }).first();
  await button.click();
  await expect(button).toHaveClass(/is-active/);
}

test.describe("Dojo page", () => {
  test("renders the minimal three-pillar dojo with controlled UI selected by default", async ({ page }, testInfo) => {
    await page.goto("/demo/dojo", { waitUntil: "networkidle" });

    await expect(page.locator(".dojo-workspace")).toBeVisible();
    await expect(page.getByText("AgentBlazor Dojo")).toBeVisible();
    await expect(page.getByRole("button", { name: "Preview" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Code" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Docs" })).toBeVisible();
    await expect(page.getByRole("button", { name: /Controlled Generative UI/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /Declarative Generative UI/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /Open-ended Generative UI/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Prebuilt UI, agent-selected actions" })).toBeVisible();
    await expect(page.getByText("Incident review draft")).toBeVisible();
    await expect(page.getByRole("button", { name: "Queue Review", exact: true })).toBeVisible();
    await expect(page.getByText("Payment API latency spike")).toBeVisible();
    await expect(page.getByText("Ash", { exact: true })).toBeVisible();

    const railPrompt = page.locator(".dojo-workspace__prompt-chip").first();
    const chatSurface = await openAssistantChatSurface(page);
    const input = chatSurface.getByLabel("Message input");
    await railPrompt.click();
    await expect(input).toHaveValue(/checkout incident/i);

    const inspectorToggle = page.getByRole("button", { name: /open agent inspector/i }).first();
    await expect(inspectorToggle).toBeVisible();

    const widthsBefore = await page.evaluate(() => {
      const panel = document.querySelector(".dojo-stage--chat-panel");
      const input = document.querySelector(".dojo-stage--chat-panel textarea[aria-label='Message input']");
      const inspector = document.querySelector(".dojo-stage--chat-panel .ab-inspector--inline");
      const rect = el => el ? el.getBoundingClientRect() : null;
      return {
        panelWidth: rect(panel)?.width ?? 0,
        inputWidth: rect(input)?.width ?? 0,
        inspectorWidth: rect(inspector)?.width ?? 0
      };
    });

    expect(widthsBefore.panelWidth).toBeGreaterThan(320);
    expect(widthsBefore.inputWidth).toBeGreaterThan(220);
    expect(widthsBefore.inspectorWidth).toBe(0);

    await inspectorToggle.click();
    await expect(page.locator(".dojo-stage--chat-panel .ab-inspector--inline")).toBeVisible();

    const widthsAfter = await page.evaluate(() => {
      const input = document.querySelector(".dojo-stage--chat-panel textarea[aria-label='Message input']");
      const inspector = document.querySelector(".dojo-stage--chat-panel .ab-inspector--inline");
      const rect = el => el ? el.getBoundingClientRect() : null;
      return {
        inputWidth: rect(input)?.width ?? 0,
        inspectorWidth: rect(inspector)?.width ?? 0
      };
    });

    expect(widthsAfter.inputWidth).toBeGreaterThan(220);
    expect(widthsAfter.inspectorWidth).toBeGreaterThan(200);

    await page.getByRole("button", { name: "Queue Review", exact: true }).click();
    await expect(page.getByText("Review queued", { exact: true })).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath("dojo-controlled.png"),
      fullPage: true
    });
  });

  test("switches the pillar previews cleanly", async ({ page }, testInfo) => {
    await page.goto("/demo/dojo", { waitUntil: "networkidle" });

    await clickExample(page, "Controlled Generative UI");
    await expect(page.getByText("Incident review draft")).toBeVisible();

    await clickExample(page, "Declarative Generative UI");
    await expect(page.getByText("Blazor-Rendered Generative UI")).toBeVisible();
    await expect(page.getByRole("button", { name: "Native", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "A2UI", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Imported from A2UI" })).toBeVisible();
    await expect(page.locator(".dojo-generated-surface")).toContainText("Restock Draft");
    await page.getByRole("button", { name: "Open-JSON-UI", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Imported from Open-JSON-UI" })).toBeVisible();
    await expect(page.locator(".dojo-generated-surface")).toContainText("Release Summary");

    await clickExample(page, "Open-ended Generative UI");
    await expect(page.locator(".dojo-open-ended__frame")).toBeVisible();
    await expect(page.getByText("Sandboxed iframe")).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath("dojo-pillars.png"),
      fullPage: true
    });
  });

  test("keeps code and docs focused on the current pillar", async ({ page }, testInfo) => {
    await page.goto("/demo/dojo", { waitUntil: "networkidle" });
    await clickExample(page, "Controlled Generative UI");

    await page.getByRole("button", { name: "Code" }).click();
    await expect(page.locator("pre.docs-code").first()).toContainText("AgentControllableComponentBase");
    await expect(page.locator("pre.docs-code").filter({ hasText: "update_controlled_incident_draft" })).toContainText("update_controlled_incident_draft");
    await expect(page.locator("pre.docs-code").filter({ hasText: "queue_controlled_incident_review" })).toContainText("queue_controlled_incident_review");

    await page.getByRole("button", { name: "Docs" }).click();
    await expect(page.getByText("Validation Coverage")).toBeVisible();
    await expect(page.getByText("Prompt-backed Playwright checks owner changes, severity/title edits, queue review, and combined updates.")).toBeVisible();

    await clickExample(page, "Declarative Generative UI");

    await page.getByRole("button", { name: "Code" }).click();
    await expect(page.locator("pre.docs-code").filter({ hasText: "AgentUiInterchangeAdapters.FromA2UiJsonLines" })).toContainText("AgentUiInterchangeAdapters.FromA2UiJsonLines");
    await expect(page.locator("pre.docs-code").filter({ hasText: "AgentUiInterchangeAdapters.FromOpenJsonUi" })).toContainText("AgentUiInterchangeAdapters.FromOpenJsonUi");

    await page.getByRole("button", { name: "Docs" }).click();
    await expect(page.getByText("Unit tests cover A2UI and Open-JSON-UI imports into AgentUiDocument.")).toBeVisible();

    await clickExample(page, "Open-ended Generative UI");

    await page.getByRole("button", { name: "Code" }).click();
    await expect(page.locator("pre.docs-code").filter({ hasText: "iframe" })).toContainText("iframe");
    await expect(page.locator("pre.docs-code").filter({ hasText: "sandbox=\"allow-scripts\"" })).toContainText("sandbox=\"allow-scripts\"");

    await page.getByRole("button", { name: "Docs" }).click();
    await expect(page.getByText("Open-ended generative UI treats the Blazor app as the host shell")).toBeVisible();
    await expect(page.getByText("Prompt-backed Playwright switches between analytics, planner, and support presets.")).toBeVisible();

    await page.screenshot({
      path: testInfo.outputPath("dojo-docs-code.png"),
      fullPage: true
    });
  });

  test("stays usable on narrower widths", async ({ page }) => {
    await page.setViewportSize({ width: 980, height: 980 });
    await page.goto("/demo/dojo", { waitUntil: "networkidle" });

    const layout = await page.evaluate(() => {
      const workspace = document.querySelector(".dojo-workspace");
      const activeExample = document.querySelector(".dojo-workspace__demo-item.is-active");
      const activeView = document.querySelector(".dojo-workspace__view-button.is-active");
      const rect = el => el ? el.getBoundingClientRect() : null;
      const color = el => el ? getComputedStyle(el).color : "";
      const background = el => el ? getComputedStyle(el).backgroundColor : "";
      return {
        documentScrollWidth: document.documentElement.scrollWidth,
        windowWidth: window.innerWidth,
        workspaceRight: rect(workspace)?.right ?? 0,
        activeExampleColor: color(activeExample),
        activeExampleBackground: background(activeExample),
        activeViewColor: color(activeView),
        activeViewBackground: background(activeView)
      };
    });

    expect(layout.documentScrollWidth).toBeLessThanOrEqual(layout.windowWidth + 1);
    expect(layout.workspaceRight).toBeLessThanOrEqual(layout.windowWidth + 1);
    expect(layout.activeExampleColor).not.toBe(layout.activeExampleBackground);
    expect(layout.activeViewColor).not.toBe(layout.activeViewBackground);
  });
});
