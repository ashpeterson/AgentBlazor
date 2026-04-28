const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the workflow demos", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /Install it\. Mount one workflow\. Watch the UI move\./i }).first()).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Watch reel" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Open docs" })).toBeVisible();
    await expect(page.locator(".landing-page__hero-player")).toContainText("CLI, code, real app result");
    await expect(page.locator(".landing-page__hero-frame video")).toHaveAttribute("src", "/videos/agentblazor-capability-reel.mp4");

    await page.locator("#hero").getByRole("link", { name: "Open docs" }).click();
    await expect(page).toHaveURL(/\/docs$/);
    await expect(page.getByRole("heading", { name: /install it\. verify it\. add one workflow\./i }).first()).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Watch reel" }).click();
    await expect(page).toHaveURL(/\/demo$/);
    await expect(page.getByRole("heading", { name: /Watch the real-app path first\./i })).toBeVisible();
    await expect(page.locator("video")).toHaveCount(4);
  });
});
