const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface, openFloatingChatWidget } = require("./chat-helpers.cjs");

test.describe("Components explorer", () => {
  test("renders the docs-style overview with catalog and contents rails", async ({ page }) => {
    await page.goto("/demo/components", { waitUntil: "networkidle" });

    await expect(page.getByRole("link", { name: "Docs", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Components", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Workflows", exact: true })).toBeVisible();
    await expect(page.locator(".components-page__catalog-card").first()).toContainText("Component Catalog");
    await expect(page.locator("#catalog").getByRole("heading", { name: "Pick a component" })).toBeVisible();
    await expect(page.locator(".components-page__catalog-nav")).toContainText("AgentDataGrid");
    await expect(page.locator(".components-page__catalog-nav")).toContainText("AgentFileUpload");
    await expect(page.locator(".components-page__contents-card")).toContainText("Contents");
    await expect(page.locator("#catalog")).toContainText("Pick a component");
    const linkPositions = await page.locator(".components-page__catalog-link").evaluateAll((elements) =>
      elements.slice(0, 5).map((element) => {
        const rect = element.getBoundingClientRect();
        return { top: rect.top, bottom: rect.bottom };
      }));
    for (let i = 1; i < linkPositions.length; i += 1) {
      expect(linkPositions[i].top).toBeGreaterThanOrEqual(linkPositions[i - 1].bottom);
    }

    const chatSurface = await openAssistantChatSurface(page);
    await expect(chatSurface).toBeVisible();
    await expect(chatSurface.getByLabel("Message input")).toBeVisible();
    await expect(page.locator(".ab-chat-widget__window").first()).toBeVisible();
    await expect(page.getByRole("button", { name: /open agent chat/i }).first()).toBeHidden();
  });

  test("floating chat widget supports prompt input and minimization", async ({ page }) => {
    await page.goto("/demo/components", { waitUntil: "networkidle" });

    const { widgetWindow, widgetSurface, minimizeButton, openButton } = await openFloatingChatWidget(page);
    await expect(widgetSurface.getByLabel("Message input")).toBeVisible();

    await widgetSurface.getByLabel("Message input").fill("Can you explain this components page?");
    await expect(widgetSurface.getByRole("button", { name: /send message/i })).toBeEnabled();

    await minimizeButton.click();
    await expect(widgetWindow).toBeHidden();
    await expect(openButton).toBeVisible();

    await openButton.click();
    await expect(widgetWindow).toBeVisible();

    await widgetWindow.press("Escape");
    await expect(widgetWindow).toBeHidden();
    await expect(openButton).toBeVisible();
  });

  test("supports focused component routes", async ({ page }) => {
    await page.goto("/demo/components/nav-menu", { waitUntil: "networkidle" });

    await expect(page.locator("#overview").getByRole("heading", { name: "AgentNavMenu", exact: true })).toBeVisible();
    await expect(page.locator(".components-page__contents-card")).toContainText("Live Example");
    await expect(page.locator("#example").getByRole("link", { name: "Workflow Hub" })).toBeVisible();

    await page.goto("/demo/components/file-upload", { waitUntil: "networkidle" });

    await expect(page.locator("#overview").getByRole("heading", { name: "AgentFileUpload", exact: true })).toBeVisible();
    await expect(page.locator(".components-page__contents-card")).toContainText("Component Contract");
    await expect(page.getByRole("button", { name: "Sync Remote Handoff" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Validate Tokens" })).toBeVisible();
  });
});
