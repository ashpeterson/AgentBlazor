const { test, expect } = require("@playwright/test");

test.describe("Documentation site", () => {
  test("renders the docs overview and exposes the expanded navigation", async ({ page }) => {
    await page.goto("/docs", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /install it\. run one route\. prove it works\./i }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Getting Started", exact: true }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Advanced CLI", exact: true }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Verification", exact: true }).first()).toBeVisible();
    await expect(page.getByText("Default path")).toBeVisible();
    await expect(page.getByText("Verification first")).toBeVisible();
  });

  test("covers the main onboarding pages developers will follow", async ({ page }) => {
    await page.goto("/docs/getting-started", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /from package source to one working workflow/i })).toBeVisible();
    await expect(page.getByText("Install the package")).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly client path")).toBeVisible();

    await page.goto("/docs/cli", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /use the cli only when manual setup is not enough/i })).toBeVisible();
    await expect(page.getByText("What to pass as the target")).toBeVisible();
    await expect(page.getByText("What scaffold --diff should tell you")).toBeVisible();

    await page.goto("/docs/verification", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /prove the install works/i })).toBeVisible();
    await expect(page.getByText("Canonical verification order")).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly verification")).toBeVisible();

    await page.goto("/docs/hosting-models", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /install behavior depends on the host shape/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Hosted WebAssembly server + client", exact: true })).toBeVisible();

    await page.goto("/docs/troubleshooting", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /check the runtime path before guessing/i })).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly remote chat issues")).toBeVisible();
  });

  test("renders the remaining docs reference pages", async ({ page }) => {
    await page.goto("/docs/workflows", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /workflows are the main authoring model/i })).toBeVisible();
    await expect(page.getByText("Authoring checklist")).toBeVisible();

    await page.goto("/docs/components", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /start with chat surfaces\. add wrappers only when needed/i })).toBeVisible();
    await expect(page.getByText("Hosted WebAssembly client surfaces")).toBeVisible();
  });

  test("holds together at a mobile width", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/docs", { waitUntil: "networkidle" });

    await expect(page.getByRole("heading", { name: /install it\. run one route\. prove it works\./i }).first()).toBeVisible();
    await expect(page.getByRole("link", { name: "Verification", exact: true }).first()).toBeVisible();

    await page.goto("/docs/verification", { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /prove the install works/i })).toBeVisible();
    await expect(page.getByText("Canonical verification order")).toBeVisible();
  });
});
