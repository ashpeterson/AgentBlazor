async function openAssistantChatSurface(page, timeoutMs = 30000) {
  const inlineSurface = await findInteractiveSurface(page.locator(".ab-chat-surface"));
  if (inlineSurface) {
    return inlineSurface;
  }

  const widgetWindow = page.locator(".ab-chat-widget__window").first();
  if (!(await widgetWindow.isVisible().catch(() => false))) {
    const openButton = page.getByRole("button", { name: /open agent chat/i }).first();
    await openButton.waitFor({ state: "visible", timeout: timeoutMs });
    await openButton.click();
  }

  await widgetWindow.waitFor({ state: "visible", timeout: timeoutMs });
  const widgetSurface = await findInteractiveSurface(widgetWindow.locator(".ab-chat-surface"));
  if (!widgetSurface) {
    throw new Error("Unable to locate an interactive chat surface.");
  }

  return widgetSurface;
}

async function findInteractiveSurface(locator) {
  const count = await locator.count();
  for (let index = 0; index < count; index++) {
    const surface = locator.nth(index);
    if (!(await surface.isVisible().catch(() => false))) {
      continue;
    }

    const promptInput = surface.getByLabel("Message input").first();
    if (await promptInput.isVisible().catch(() => false)) {
      return surface;
    }
  }

  return null;
}

module.exports = {
  openAssistantChatSurface
};
