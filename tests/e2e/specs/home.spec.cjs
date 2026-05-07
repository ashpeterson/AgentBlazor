const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the live support demo", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /AgentBlazor adds a chat sidebar that controls your existing components/i }).first()).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Docs", exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Live Demo", exact: true })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Docs" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Live demo" })).toBeVisible();
    await expect(page.locator(".landing-page__quickstart")).toContainText("Quickstart");
    await expect(page.locator(".landing-page__quickstart")).toContainText("dotnet add package AgentBlazor --prerelease");
    await expect(page.locator(".landing-page__quickstart")).toContainText("AgentChatWidget");
    await expect(page.locator(".landing-page__hero-meta").getByRole("link", { name: "Read quickstart" })).toBeVisible();
    await expect(page.locator(".landing-page__hero-meta").getByRole("link", { name: "Try live demo" })).toBeVisible();
    await expect(page.locator(".landing-page__quickstart video")).toHaveCount(0);
    await expect(page.locator(".landing-page__support-strip")).toHaveCount(0);

    await page.locator("#hero").getByRole("link", { name: "Docs" }).click();
    await expect(page).toHaveURL(/\/docs$/);
    await expect(page.getByRole("heading", { name: /install it\. run one route\. prove it works\./i }).first()).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Live demo" }).click();
    await expect(page).toHaveURL(/\/demo$/);
    await expect(page.getByRole("heading", { name: /pick the workflow size you want to see/i }).first()).toBeVisible();
    await expect(page.getByRole("heading", { name: "Quick: draft one safe reply" })).toBeVisible();

    await page.getByRole("link", { name: "Start with quick demo" }).click();
    await expect(page).toHaveURL(/\/demo\/workflows\/support-inbox/);
  });
});
