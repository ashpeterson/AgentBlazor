const { test, expect } = require("@playwright/test");

test.describe("Complex workflow page", () => {
  test("renders workflow surface with queue, suppliers, and chat", async ({ page }) => {
    await page.goto("/demo/workflow", { waitUntil: "networkidle" });

    await expect(page.getByText("Complex Workflow")).toBeVisible();
    await expect(page.getByRole("tab", { name: "Onboarding Queue" })).toBeVisible();
    await expect(page.getByRole("tab", { name: "Supplier Risk" })).toBeVisible();
    await expect(page.getByText("Workflow Agent")).toBeVisible();
  });

  test("submitting onboarding updates workflow queue with real data", async ({ page }) => {
    const uniqueSupplierName = `E2E Supplier ${Date.now()}`;

    await page.goto("/demo/onboarding", { waitUntil: "networkidle" });
    await page.getByRole("button", { name: "Open Onboarding Dialog" }).click();

    await page.getByLabel("Supplier name").fill(uniqueSupplierName);
    await page.getByLabel("Contact email").fill(`ops-${Date.now()}@example.com`);
    await page.getByLabel("Contact phone").fill("555-123-4567");
    await page.getByLabel("Risk tier").fill("High");
    await page.getByLabel("Country").fill("United States");
    await page.getByLabel("Category").fill("Critical Components");
    await page.getByLabel("Payment terms").fill("Net 45");
    await page.getByLabel("Requested budget").fill("125000");
    await page.getByLabel("Expected monthly spend").fill("23000");
    await page.getByLabel("Priority (1-5)").fill("2");

    await page.getByRole("button", { name: "Submit Onboarding" }).click();
    await expect(page.getByText(/Submitted REQ-/).first()).toBeVisible();

    await page.goto("/demo/workflow", { waitUntil: "networkidle" });
    await page.getByRole("button", { name: "Refresh" }).click();

    await expect(page.getByText(uniqueSupplierName)).toBeVisible();
  });
});
