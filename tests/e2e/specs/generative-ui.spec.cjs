const { test, expect } = require("@playwright/test");

test.describe("Generative UI deterministic workflows", () => {
  test("highest-risk flow renders controls and follow-up action", async ({ page }) => {
    test.slow();
    const assertNoFatalErrors = attachErrorCollectors(page);

    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });
    await expect(page.getByText("Generative UI Playground")).toBeVisible();
    await expect(page.getByText("Generated UI mode is enabled for this chat.")).toBeVisible();

    const { chatSurface, sendMessage } = await createChatHarness(page);
    await sendMessage("show highest risk supplier");

    await expect(chatSurface.getByText("Supplier Risk Snapshot")).toBeVisible();
    await expect(chatSurface.getByRole("columnheader", { name: "Supplier" })).toBeVisible();
    await expect(chatSurface.getByRole("columnheader", { name: "Risk Score" })).toBeVisible();
    await expect(chatSurface.getByRole("button", { name: "Run Again" })).toBeVisible();
    await expect(chatSurface.getByRole("button", { name: "Filter High Risk" })).toBeVisible();

    await chatSurface.getByRole("button", { name: "Filter High Risk" }).first().click();
    await expect(chatSurface.getByText("Generated UI action invoked: supplier-risk-table.showOnlyHighRisk.")).toBeVisible();
    await expect(chatSurface.getByText("High Risk Suppliers")).toBeVisible();
    await expect(chatSurface.getByText("Showing suppliers with RiskScore >= 70.")).toBeVisible();

    await assertNoFatalErrors();
  });

  test("onboarding draft prompt renders generated form", async ({ page }) => {
    test.slow();
    const assertNoFatalErrors = attachErrorCollectors(page);

    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });
    const { chatSurface, sendMessage } = await createChatHarness(page);

    await sendMessage("create an onboarding draft for supplier Ash");

    await expect(chatSurface.getByText("Supplier Onboarding Draft")).toBeVisible();
    await expect(chatSurface.getByText("Review details, then apply in chat.")).toBeVisible();
    await expect(chatSurface.getByText("Supplier Name")).toBeVisible();
    await expect(chatSurface.getByRole("textbox", { name: "Supplier Name" })).toHaveValue("Ash");
    await expect(chatSurface.getByRole("button", { name: "Apply form values" })).toBeVisible();

    await assertNoFatalErrors();
  });

  test("applying onboarding draft stays in chat and confirms", async ({ page }) => {
    test.slow();
    const assertNoFatalErrors = attachErrorCollectors(page);

    await page.goto("/demo/generative-ui", { waitUntil: "networkidle" });
    const { chatSurface, sendMessage } = await createChatHarness(page);

    await sendMessage("create an onboarding draft for supplier Ash");
    await chatSurface.getByRole("button", { name: "Apply form values" }).first().click();

    await expect(page).toHaveURL(/\/demo\/generative-ui/i);
    await expect(chatSurface.getByText("Generated UI action invoked: onboarding-draft.applyOnboardingDraft.")).toBeVisible();
    await expect(chatSurface.getByText("Draft Values Applied")).toBeVisible();
    await expect(chatSurface.getByText("Supplier draft values for 'Ash' were applied in chat.")).toBeVisible();

    await assertNoFatalErrors();
  });
});

function attachErrorCollectors(page) {
  const pageErrors = [];
  const consoleErrors = [];

  page.on("pageerror", error => {
    pageErrors.push(String(error && error.message ? error.message : error));
  });

  page.on("console", message => {
    if (message.type() === "error") {
      consoleErrors.push(message.text());
    }
  });

  return async function assertNoFatalErrors() {
    const bodyText = await page.locator("body").innerText();
    expect(bodyText).not.toContain("Unable to focus an invalid element");

    const fatalPageErrors = pageErrors.filter(error =>
      !/ResizeObserver loop limit exceeded/i.test(error));
    expect(fatalPageErrors).toEqual([]);

    const fatalConsoleErrors = consoleErrors.filter(error =>
      /Unable to focus an invalid element|Unhandled|TypeError|ReferenceError|blazor\.web/i.test(error));
    expect(fatalConsoleErrors).toEqual([]);
  };
}

async function createChatHarness(page) {
  const chatSurface = page.locator(".ab-chat-surface").first();
  const prompt = chatSurface.locator('textarea[placeholder*="Ask for a component"]');
  const sendButton = chatSurface.locator('button[aria-label="Send message"]');

  await expect(prompt).toBeVisible();
  await expect(sendButton).toBeVisible();
  await expect(sendButton).toBeDisabled();

  async function sendMessage(text) {
    const beforeCount = await chatSurface.locator(".ab-chat-surface__item").count();
    await prompt.fill(text);
    await expect(sendButton).toBeEnabled({ timeout: 120000 });
    await sendButton.click();

    await expect.poll(async () =>
      chatSurface.locator(".ab-chat-surface__item").count(), {
      timeout: 120000
    }).toBeGreaterThan(beforeCount + 1);
    await expect(prompt).toHaveValue("");
  }

  return { chatSurface, sendMessage };
}
