const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the workflow demos", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /Watch it install\. Watch it run\./i }).first()).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Watch examples" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Docs site" })).toBeVisible();
    await expect(page.locator(".landing-page__video-card").first()).toContainText("Install with CLI");

    await page.locator("#hero").getByRole("link", { name: "Docs site" }).click();
    await expect(page).toHaveURL(/\/docs$/);
    await expect(page.getByRole("heading", { name: /install it\. verify it\. add one workflow\./i }).first()).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Watch examples" }).click();
    await expect(page).toHaveURL(/\/demo$/);
    await expect(page.getByRole("heading", { name: /Watch the 3 shortest examples first\./i })).toBeVisible();
    await expect(page.locator("video")).toHaveCount(3);
  });
});
