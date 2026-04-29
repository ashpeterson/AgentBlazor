const { test, expect } = require("@playwright/test");
const { openAssistantChatSurface } = require("./chat-helpers.cjs");

const workflowScenarios = [
  {
    name: "support inbox",
    route: "/demo/workflows/support-inbox",
    heading: /Support inbox/i,
    marker: "Ticket queue",
    button: "Show this week's tickets"
  },
  {
    name: "response orchestration",
    route: "/demo/workflows/response-orchestration?reset=true",
    heading: /Response orchestration/i,
    marker: "Packet preview",
    button: "Reset"
  },
  {
    name: "release dossier",
    route: "/demo/workflows/release-dossier?reset=true",
    heading: /Release dossier/i,
    marker: "Dossier preview",
    button: "Reset"
  },
  {
    name: "supplier compliance",
    route: "/demo/workflows/supplier-compliance",
    heading: /Supplier compliance/i,
    marker: "Focused supplier grid",
    button: "Highlight 30-day risk"
  },
  {
    name: "file audit bundle",
    route: "/demo/workflows/file-audit-bundle",
    heading: /File audit bundle/i,
    marker: "Workflow commands",
    button: "Switch to Remote"
  },
  {
    name: "incident escalation",
    route: "/demo/workflows/incident-escalation",
    heading: /Incident escalation/i,
    marker: "Workflow navigation state",
    button: "Focus Evidence Review"
  },
  {
    name: "recipe release",
    route: "/demo/workflows/recipe-release",
    heading: /Recipe release/i,
    marker: "Reset Workflow State",
    button: "Assess Readiness"
  },
  {
    name: "runtime probe",
    route: "/demo/workflows/runtime-probe",
    heading: /Runtime probe/i,
    marker: "run the runtime cancellation probe"
  }
];

test.describe("Workflow demos", () => {
  for (const scenario of workflowScenarios) {
    test(`renders ${scenario.name} with its assistant surface`, async ({ page }) => {
      await page.goto(scenario.route, { waitUntil: "networkidle" });

      await expect(page.getByRole("heading", { name: scenario.heading }).first()).toBeVisible();
      await expect(page.getByText(scenario.marker, { exact: false }).first()).toBeVisible();

      if (scenario.button) {
        await expect(page.getByRole("button", { name: scenario.button, exact: true })).toBeVisible();
      }

      const chatSurface = await openAssistantChatSurface(page);
      await expect(chatSurface.getByLabel("Message input")).toBeVisible();
    });
  }
});
