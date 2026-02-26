const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface } = require("./chat-helpers.cjs");

test.describe("Generative UI page", () => {
  test("renders chat surface and onboarding copy", async ({ page }) => {
    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });

    await expect(page.getByText("Generative UI Playground")).toBeVisible();
    const chatSurface = await openAssistantChatSurface(page);
    await expect(chatSurface).toBeVisible();
    await expect(chatSurface.getByText("Assistant")).toBeVisible();
    await expect(chatSurface.getByText("Generated UI mode is enabled for this chat.")).toBeVisible();
  });

  test("supports basic message input interactions", async ({ page }) => {
    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });

    const chatSurface = await openAssistantChatSurface(page);
    const prompt = chatSurface.getByLabel("Message input");
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
