window.AgentBlazorDemo = window.AgentBlazorDemo || {};

window.AgentBlazorDemo.getOrCreateAssistantClientId = function () {
  const storageKey = "agentblazor.demo.assistant-client-id";
  const existing = window.sessionStorage.getItem(storageKey);
  if (existing) {
    return existing;
  }

  const value = (window.crypto && typeof window.crypto.randomUUID === "function")
    ? window.crypto.randomUUID()
    : "client-" + Math.random().toString(36).slice(2) + Date.now().toString(36);

  window.sessionStorage.setItem(storageKey, value);
  return value;
};
