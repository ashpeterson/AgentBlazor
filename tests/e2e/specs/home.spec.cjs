const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the workflow demos", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /One prompt moves the app\./i }).first()).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Docs", exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Live Demo", exact: true })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Open docs" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Live workflow" })).toBeVisible();
    await expect(page.locator(".landing-page__hero-player")).toContainText("Real app result first");
    await expect(page.locator(".landing-page__hero-frame video")).toHaveAttribute("src", "/videos/agentblazor-hero-teaser.mp4");
    await expect(page.locator(".landing-page__support-strip video")).toHaveCount(2);

    await page.locator("#hero").getByRole("link", { name: "Open docs" }).click();
    await expect(page).toHaveURL(/\/docs$/);
    await expect(page.getByRole("heading", { name: /install it\. verify it\. add one workflow\./i }).first()).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Live workflow" }).click();
    await expect(page).toHaveURL(/\/demo\/workflows\/response-orchestration/);
  });
});
