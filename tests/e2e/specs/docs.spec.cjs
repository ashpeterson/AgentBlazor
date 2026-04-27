const { test, expect } = require("@playwright/test");

test.describe("Documentation site", () => {
  test("renders the docs overview and exposes the expanded navigation", async ({ page }) => {
    await page.goto("/docs", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /complete install, verification, and authoring guide/i }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Getting Started", exact: true }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "CLI", exact: true }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Verification", exact: true }).first()).toBeVisible();
    await expect(page.getByText("Canonical flow")).toBeVisible();
    await expect(page.getByText("Verification first")).toBeVisible();
  });

  test("covers the main onboarding pages developers will follow", async ({ page }) => {
    await page.goto("/docs/getting-started", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /package source to a working route-scoped workflow/i })).toBeVisible();
    await expect(page.getByText("Current preview source")).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly client path")).toBeVisible();

    await page.goto("/docs/cli", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /run the cli in a predictable order/i })).toBeVisible();
    await expect(page.getByText("What to pass as the target")).toBeVisible();
    await expect(page.getByText("What scaffold --diff should tell you")).toBeVisible();

    await page.goto("/docs/verification", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /test the install path like a real app owner would/i })).toBeVisible();
    await expect(page.getByText("Canonical verification order")).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly verification")).toBeVisible();

    await page.goto("/docs/hosting-models", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /install behavior depends on the host shape/i })).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly server + client")).toBeVisible();

    await page.goto("/docs/troubleshooting", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /check the runtime path before guessing/i })).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly remote chat issues")).toBeVisible();
  });

  test("renders the remaining docs reference pages", async ({ page }) => {
    await page.goto("/docs/workflows", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /workflows are the primary authoring surface/i })).toBeVisible();
    await expect(page.getByText("Authoring checklist")).toBeVisible();

    await page.goto("/docs/components", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /chat surfaces first, deterministic wrappers where they help/i })).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly client surfaces")).toBeVisible();

    await page.goto("/docs/demo-tour", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /the demo is a funnel, not a route zoo/i })).toBeVisible();

    await page.goto("/docs/pricing", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /free ships the workflow\. pro makes the app smarter with use/i })).toBeVisible();
  });

  test("holds together at a mobile width", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/docs", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /complete install, verification, and authoring guide/i }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Verification", exact: true }).first()).toBeVisible();

    await page.goto("/docs/verification", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /test the install path like a real app owner would/i })).toBeVisible();
    await expect(page.getByText("Canonical verification order")).toBeVisible();
  });
});
