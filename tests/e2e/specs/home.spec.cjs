const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into dojo and components", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /Explore AgentBlazor/i }).first()).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Open Capability Dojo" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Browse Agentic Components" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Explore AgentBlazor in 3 Stops" })).toBeVisible();
    await expect(page.getByText("The landing page gives the product overview, the Dojo explains the patterns")).toBeVisible();

    await page.locator("#hero").getByRole("link", { name: "Browse Agentic Components" }).click();
    await expect(page).toHaveURL(/\/demo\/components$/);
    await expect(page.locator("#catalog").getByRole("heading", { name: "Current drop-in components" })).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Open Capability Dojo" }).click();
    await expect(page).toHaveURL(/\/demo\/dojo$/);
    await expect(page.getByRole("button", { name: /Controlled Generative UI/i })).toBeVisible();
  });
});
