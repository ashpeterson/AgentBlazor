const { test, expect } = require("@playwright/test");

test("Generative UI chat flow renders and avoids JS runtime errors", async ({ page }) => {
  test.slow();

  const pageErrors = [];
  const consoleErrors = [];

  page.on("pageerror", error => {
    pageErrors.push(String(error && error.message ? error.message : error));
  });

  page.on("console", message => {
    if (message.type() === "error") {
      consoleErrors.push(message.text());
    }
  });

  await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });
  await expect(page.getByText("Generative UI Playground")).toBeVisible();

  const prompt = page.locator('textarea[placeholder*="Ask for suppliers"]');
  await expect(prompt).toBeVisible();

  const sendButton = page.getByRole("button", { name: "Send message" });
  await expect(sendButton).toBeVisible();

  await prompt.fill("show highest risk supplier");
  await sendButton.click();
  await page.waitForTimeout(5000);

  await prompt.fill("create an onboarding draft for supplier Ash");
  await sendButton.click();
  await page.waitForTimeout(6000);

  const timelineItemCount = await page.locator(".ab-chat-surface__item").count();
  expect(timelineItemCount).toBeGreaterThan(2);

  const bodyText = await page.locator("body").innerText();
  expect(bodyText).not.toContain("Unable to focus an invalid element");

  const fatalPageErrors = pageErrors.filter(error =>
    !/ResizeObserver loop limit exceeded/i.test(error));
  expect(fatalPageErrors).toEqual([]);

  const fatalConsoleErrors = consoleErrors.filter(error =>
    /Unable to focus an invalid element|Unhandled|TypeError|ReferenceError|blazor\.web/i.test(error));
  expect(fatalConsoleErrors).toEqual([]);
});
