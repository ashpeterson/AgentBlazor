async function openAssistantChatSurface(page, timeoutMs = 30000) {
  const inlineSurface = page.locator(".ab-chat-surface").first();
  if (await inlineSurface.isVisible().catch(() => false)) {
    return inlineSurface;
  }

  const widgetWindow = page.locator(".ab-chat-widget__window").first();
  if (!(await widgetWindow.isVisible().catch(() => false))) {
    const openButton = page.getByRole("button", { name: /open agent chat/i }).first();
    await openButton.waitFor({ state: "visible", timeout: timeoutMs });
    await openButton.click();
  }

  await widgetWindow.waitFor({ state: "visible", timeout: timeoutMs });
  const widgetSurface = widgetWindow.locator(".ab-chat-surface").first();
  await widgetSurface.waitFor({ state: "visible", timeout: timeoutMs });
  return widgetSurface;
}

module.exports = {
  openAssistantChatSurface
};
