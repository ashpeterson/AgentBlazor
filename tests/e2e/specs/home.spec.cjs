const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the live support demo", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /Install it\. Open a support queue\. Draft the next safe reply\./i }).first()).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Docs", exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Live Demo", exact: true })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Docs" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Live demo" })).toBeVisible();
    await expect(page.locator(".landing-page__hero-meta")).toContainText("CLI install");
    await expect(page.locator(".landing-page__hero-meta")).toContainText("Support workflow code");
    await expect(page.locator(".landing-page__hero-meta")).toContainText("Reply draft with approval");
    await expect(page.locator(".landing-page__hero-frame video")).toHaveAttribute("src", "/videos/agentblazor-capability-reel.mp4");
    await expect(page.locator(".landing-page__support-strip")).toHaveCount(0);

    await page.locator("#hero").getByRole("link", { name: "Docs" }).click();
    await expect(page).toHaveURL(/\/docs$/);
    await expect(page.getByRole("heading", { name: /install it\. run one route\. prove it works\./i }).first()).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Live demo" }).click();
    await expect(page).toHaveURL(/\/demo\/workflows\/support-inbox/);
  });
});
