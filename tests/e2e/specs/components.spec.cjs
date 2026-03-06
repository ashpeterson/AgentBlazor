const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface } = require("./chat-helpers.cjs");

test.describe("Components explorer", () => {
  test("renders the overview with current wrapper coverage", async ({ page }) => {
    await page.goto("/demo/components", { waitUntil: "networkidle" });

    await expect(page.getByText("AgentBlazor Component Explorer")).toBeVisible();
    await expect(page.getByText("Coverage")).toBeVisible();
    await expect(page.getByRole("heading", { name: "AgentDataGrid" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "AgentFileUpload" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Open Attribute-Based Example" })).toBeVisible();

    const chatSurface = await openAssistantChatSurface(page);
    await expect(chatSurface).toBeVisible();
    await expect(chatSurface.getByLabel("Message input")).toBeVisible();
  });

  test("supports focused component routes and the attribute-based example", async ({ page }) => {
    await page.goto("/demo/components/file-upload", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "AgentFileUpload" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sync Remote Handoff" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Validate Tokens" })).toBeVisible();

    await page.goto("/demo/components/attribute-based", { waitUntil: "networkidle" });

    await expect(page.getByText("Convention-First (No Built-in Wrapper Required)")).toBeVisible();
    await expect(page.getByText("Live Example Component State")).toBeVisible();
    await expect(page.getByText("Selected Supplier:")).toBeVisible();
  });
});
