const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the workflow demos", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /Talk to the app/i }).first()).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Start Live Demo" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Open Workflow Hub" })).toBeVisible();
    await expect(page.getByText("Ship free first")).toBeVisible();

    await page.locator("#hero").getByRole("link", { name: "Open Workflow Hub" }).click();
    await expect(page).toHaveURL(/\/demo$/);
    await expect(page.getByRole("heading", { name: "Just talk to the agent." })).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Start Live Demo" }).click();
    await expect(page).toHaveURL(/\/demo\/workflows\/response-orchestration/);
    await expect(page.locator(".demo-app__title")).toContainText("Response Orchestration");
  });
});
