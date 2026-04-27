const { test, expect } = require("@playwright/test");

test.describe("Landing page", () => {
  test("guides users from home into the workflow demos", async ({ page }) => {
    await page.goto("/", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /One prompt moves the app/i }).first()).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Try live workflow" })).toBeVisible();
    await expect(page.locator("#hero").getByRole("link", { name: "Docs site" })).toBeVisible();
    await expect(page.getByText("Ship one workflow first")).toBeVisible();

    await page.locator("#hero").getByRole("link", { name: "Docs site" }).click();
    await expect(page).toHaveURL(/\/docs$/);
    await expect(page.getByRole("heading", { name: /install it\. verify it\. add one workflow\./i }).first()).toBeVisible();

    await page.goto("/", { waitUntil: "networkidle" });
    await page.locator("#hero").getByRole("link", { name: "Try live workflow" }).click();
    await expect(page).toHaveURL(/\/demo\/workflows\/response-orchestration/);
    await expect(page.locator(".workflow-board__hero")).toContainText("Response orchestration");
  });
});
