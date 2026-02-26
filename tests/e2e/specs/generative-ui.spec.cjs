const { test, expect } = require("@playwright/test");

test.describe("Generative UI page", () => {
  test("renders chat surface and onboarding copy", async ({ page }) => {
    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });

    await expect(page.getByText("Generative UI Playground")).toBeVisible();
    await expect(page.getByText("Generated UI mode is enabled for this chat.")).toBeVisible();

    const chatSurface = page.locator(".ab-chat-surface").first();
    await expect(chatSurface).toBeVisible();
    await expect(chatSurface.getByText("Generative UI Agent")).toBeVisible();
  });

  test("supports basic message input interactions", async ({ page }) => {
    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });

    const chatSurface = page.locator(".ab-chat-surface").first();
    const prompt = chatSurface.locator('textarea[placeholder*="Ask for a generated summary"]');
    const sendButton = chatSurface.locator('button[aria-label="Send message"]');

    await expect(prompt).toBeVisible();
    await expect(sendButton).toBeVisible();
    await expect(sendButton).toBeDisabled();

    await prompt.fill("show supplier risk by region as a chart");
    await expect(sendButton).toBeEnabled();

    await sendButton.click();
    await expect(prompt).toHaveValue("");
  });
});
