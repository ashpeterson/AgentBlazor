const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface } = require("./chat-helpers.cjs");

test.describe("Dojo page", () => {
  test("renders the dojo shell with the embedded assistant", async ({ page }) => {
    await page.goto("/demo/dojo", { waitUntil: "networkidle" });

    await expect(page.getByText("AG-UI Interactive Dojo")).toBeVisible();
    await expect(page.getByRole("button", { name: "Preview" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Code" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Docs" })).toBeVisible();
    await expect(page.getByText("AI Recipe Assistant")).toBeVisible();
    await expect(page.getByRole("button", { name: /Shared State between agent and UI/i })).toBeVisible();

    const chatSurface = await openAssistantChatSurface(page);
    await expect(chatSurface).toBeVisible();
    await expect(chatSurface.getByLabel("Message input")).toBeVisible();
  });

  test("switches dojo examples and mode toggles in page", async ({ page }) => {
    await page.goto("/demo/dojo", { waitUntil: "networkidle" });

    await page.getByRole("button", { name: /Backend Tool Rendering/i }).click();
    await expect(page.getByRole("button", { name: "Submit Tool" })).toBeVisible();

    await page.getByRole("button", { name: "Code" }).click();
    await expect(page.locator("pre.docs-code").first()).toBeVisible();

    await page.getByRole("button", { name: "Docs" }).click();
    await expect(
      page.getByText("Backend tool rendering shows tool lifecycle updates in the chat and the canvas itself.")
    ).toBeVisible();

    await page.getByRole("button", { name: /Predictive State Updates/i }).click();
    await expect(page.locator(".dojo-document__label")).toContainText("Predictive State Updates Document Editor");
  });
});
