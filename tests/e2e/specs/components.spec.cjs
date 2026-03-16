const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface } = require("./chat-helpers.cjs");

test.describe("Components explorer", () => {
  test("renders the docs-style overview with catalog and contents rails", async ({ page }) => {
    await page.goto("/demo/components", { waitUntil: "networkidle" });

    await expect(page.getByRole("link", { name: "Home", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Capability Dojo", exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "Agentic Components", exact: true })).toBeVisible();
    await expect(page.locator(".components-page__catalog-card").first()).toContainText("Component Catalog");
    await expect(page.locator("#catalog").getByRole("heading", { name: "Current drop-in components" })).toBeVisible();
    await expect(page.locator(".components-page__catalog-nav")).toContainText("AgentDataGrid");
    await expect(page.locator(".components-page__catalog-nav")).toContainText("AgentFileUpload");
    await expect(page.locator(".components-page__contents-card")).toContainText("Contents");
    await expect(page.locator("#catalog")).toContainText("Current drop-in components");
    await expect(page.locator(".components-page__catalog-nav")).toContainText("Custom Attribute-Based Component");
    const navScrollMetrics = await page.locator(".components-page__catalog-nav").evaluate((element) => ({
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
      overflowY: window.getComputedStyle(element).overflowY
    }));
    expect(navScrollMetrics.overflowY).toBe("auto");
    expect(navScrollMetrics.scrollHeight).toBeGreaterThan(navScrollMetrics.clientHeight);
    const linkPositions = await page.locator(".components-page__catalog-link").evaluateAll((elements) =>
      elements.slice(0, 5).map((element) => {
        const rect = element.getBoundingClientRect();
        return { top: rect.top, bottom: rect.bottom };
      }));
    for (let i = 1; i < linkPositions.length; i += 1) {
      expect(linkPositions[i].top).toBeGreaterThanOrEqual(linkPositions[i - 1].bottom);
    }

    const chatSurface = await openAssistantChatSurface(page);
    await expect(chatSurface).toBeVisible();
    await expect(chatSurface.getByLabel("Message input")).toBeVisible();
    await expect(page.locator(".ab-chat-widget__window").first()).toBeVisible();
    await expect(page.getByRole("button", { name: /open agent chat/i }).first()).toBeHidden();
  });

  test("supports focused component routes and the attribute-based example", async ({ page }) => {
    await page.goto("/demo/components/nav-menu", { waitUntil: "networkidle" });

    await expect(page.locator("#overview").getByRole("heading", { name: "AgentNavMenu", exact: true })).toBeVisible();
    await expect(page.locator(".components-page__contents-card")).toContainText("Live Example");
    await expect(page.locator("#example").getByRole("link", { name: "Capability Dojo" })).toBeVisible();

    await page.goto("/demo/components/file-upload", { waitUntil: "networkidle" });

    await expect(page.locator("#overview").getByRole("heading", { name: "AgentFileUpload", exact: true })).toBeVisible();
    await expect(page.locator(".components-page__contents-card")).toContainText("Component Contract");
    await expect(page.getByRole("button", { name: "Sync Remote Handoff" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Validate Tokens" })).toBeVisible();

    await page.goto("/demo/components/attribute-based", { waitUntil: "networkidle" });

    await expect(page.getByText("Convention-First (No Built-in Wrapper Required)")).toBeVisible();
    await expect(page.getByText("Live Example Component State")).toBeVisible();
    await expect(page.getByText("Selected Supplier:")).toBeVisible();

    await page.goto("/demo/components/parity/form", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudForm and AgentForm side by side" })).toBeVisible();
    await expect(page.getByText("MudForm baseline")).toBeVisible();
    await expect(page.getByText("AgentForm drop-in")).toBeVisible();
    await expect(page.getByText("Shared host model and shared child content")).toBeVisible();

    await page.goto("/demo/components/parity/datagrid", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudDataGrid and AgentDataGrid side by side" })).toBeVisible();
    await expect(page.getByText("MudDataGrid baseline")).toBeVisible();
    await expect(page.getByText("AgentDataGrid drop-in")).toBeVisible();
    await expect(page.locator("text=Shared supplier toolbar")).toHaveCount(2);
    await expect(page.getByRole("cell", { name: "Alpine Components" })).toHaveCount(2);

    await page.goto("/demo/components/parity/dialog", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudDialog and AgentDialog side by side" })).toBeVisible();
    await expect(page.getByText("MudDialog baseline")).toBeVisible();
    await expect(page.getByText("AgentDialog drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Open both dialogs" }).click();
    await expect(page.getByText("Supplier Approval Review")).toHaveCount(2);

    await page.goto("/demo/components/parity/choice-inputs", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudSelect and MudAutocomplete side by side with Agent equivalents" })).toBeVisible();
    await expect(page.getByText("MudSelect baseline")).toBeVisible();
    await expect(page.getByText("AgentSelect drop-in")).toBeVisible();
    await expect(page.getByText("MudAutocomplete baseline")).toBeVisible();
    await expect(page.getByText("AgentAutocomplete drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Set both selects to Germany" }).click();
    await expect(page.getByText("Selected: Germany", { exact: true })).toHaveCount(2);
    await page.getByRole("button", { name: "Seed both searches with Apex" }).click();
    await expect(page.getByText("Selected supplier: Apex Components", { exact: true })).toHaveCount(2);

    await page.goto("/demo/components/parity/file-upload", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudFileUpload and AgentFileUpload side by side" })).toBeVisible();
    await expect(page.getByText("MudFileUpload baseline")).toBeVisible();
    await expect(page.getByText("AgentFileUpload drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Load both evidence bundles" }).click();
    await expect(page.getByText("q1-risk-summary.pdf", { exact: true })).toHaveCount(2);
    await expect(page.getByText("vendor-checklist.csv", { exact: true })).toHaveCount(2);

    await page.goto("/demo/components/parity/date-pickers", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudDatePicker and MudDateRangePicker side by side with Agent equivalents" })).toBeVisible();
    await expect(page.getByText("MudDatePicker baseline")).toBeVisible();
    await expect(page.getByText("AgentDatePicker drop-in")).toBeVisible();
    await expect(page.getByText("MudDateRangePicker baseline")).toBeVisible();
    await expect(page.getByText("AgentDateRangePicker drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Set both review dates" }).click();
    await expect(page.getByText("Review date: 2026-03-18", { exact: true })).toHaveCount(2);
    await page.getByRole("button", { name: "Set both review ranges" }).click();
    await expect(page.getByText("Range: 2026-03-20 to 2026-03-24", { exact: true })).toHaveCount(2);

    await page.goto("/demo/components/parity/workflow-navigation", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudTabs and MudStepper side by side with Agent equivalents" })).toBeVisible();
    await expect(page.getByText("MudTabs baseline")).toBeVisible();
    await expect(page.getByText("AgentTabs drop-in")).toBeVisible();
    await expect(page.getByText("MudStepper baseline")).toBeVisible();
    await expect(page.getByText("AgentStepper drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Set both tabs to Policy" }).click();
    await expect(page.getByText("Active tab: Policy", { exact: true })).toHaveCount(2);
    await page.getByRole("button", { name: "Move both steppers to Review" }).click();
    await expect(page.getByText("Current step: Review", { exact: true })).toHaveCount(2);

    await page.goto("/demo/components/parity/hierarchy-navigation", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "MudNavMenu and MudTreeView side by side with Agent equivalents" })).toBeVisible();
    await expect(page.getByText("MudNavMenu baseline")).toBeVisible();
    await expect(page.getByText("AgentNavMenu drop-in")).toBeVisible();
    await expect(page.getByText("MudTreeView baseline")).toBeVisible();
    await expect(page.getByText("AgentTreeView drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Select both tree views on Audit" }).click();
    await expect(page.getByText("Selected node: Audit", { exact: true })).toHaveCount(2);
    await page.getByRole("link", { name: "Jump to policy section" }).click();
    await expect(page).toHaveURL(/#policy$/);

    await page.goto("/demo/components/parity/composed-workflow", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: "A composed workflow screen with Mud and Agent components side by side" })).toBeVisible();
    await expect(page.getByText("Mud workflow baseline")).toBeVisible();
    await expect(page.getByText("Agent workflow drop-in")).toBeVisible();
    await page.getByRole("button", { name: "Seed both workflow screens" }).click();
    await expect(page.getByText("Supplier: Northwind Components", { exact: true })).toHaveCount(2);
    await expect(page.getByText("Risk tier: High", { exact: true })).toHaveCount(2);
    await expect(page.getByText("Review date: 2026-03-26", { exact: true })).toHaveCount(2);
    await expect(page.getByText("Files count: 2", { exact: true })).toHaveCount(2);
    await page.getByRole("button", { name: "Switch both tabs to Documents" }).click();
    await expect(page.getByText("Active tab: Documents", { exact: true })).toHaveCount(2);
    await page.getByRole("button", { name: "Move both steppers to Review" }).click();
    await expect(page.getByText("Current step: Review", { exact: true })).toHaveCount(2);
  });
});
