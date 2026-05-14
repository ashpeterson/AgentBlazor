namespace AgentBlazor.Demo.Services;

internal enum DemoScenarioScale
{
    Quick,
    Full,
    Advanced
}

internal sealed record DemoScenario(
    string Id,
    DemoScenarioScale Scale,
    string Title,
    string Summary,
    string Route,
    string CtaLabel,
    string AssistantTitle,
    string AssistantDescription,
    string AssistantPlaceholder,
    string AgentName,
    IReadOnlyList<string> Prompts,
    IReadOnlyList<string> ProofPoints)
{
    public bool ShowOnLaunchpad { get; init; } = true;
}

internal static class DemoScenarioCatalog
{
    public static IReadOnlyList<DemoScenario> Scenarios { get; } =
    [
        new(
            "support-quick",
            DemoScenarioScale.Quick,
            "Quick: draft one safe reply",
            "A one-page support queue flow that shows discovery, explanation, approval, and a structured customer reply.",
            "/demo/workflows/support-inbox",
            "Open quick demo",
            "Support assistant",
            "Find tickets, explain the queue, draft a reply.",
            "Show open tickets. Explain the queue. Draft a reply.",
            "Support Inbox Agent",
            [
                "Show open tickets from this week",
                "Explain why they need attention",
                "Draft a reply for ticket TCK-1042"
            ],
            [
                "One Blazor route",
                "One approval boundary",
                "Structured draft appears in the page"
            ]),
        new(
            "support-full",
            DemoScenarioScale.Full,
            "Full: handle blockers and handoff",
            "The same support queue scales up to multiple tickets, an evidence blocker, escalation, and a safe reply handoff.",
            "/demo/workflows/support-inbox?mode=full",
            "Open full support flow",
            "Support assistant",
            "Find tickets, explain blockers, draft replies, escalate blocked cases.",
            "Show open tickets. Explain blockers. Draft replies. Escalate if blocked.",
            "Support Inbox Agent",
            [
                "Show open tickets from this week",
                "Draft a reply for the highlighted tickets",
                "Escalate the blocked tickets"
            ],
            [
                "Same route, larger workflow",
                "Blocked ticket recovery",
                "Approval-safe customer response"
            ]),
        new(
            "response-orchestration",
            DemoScenarioScale.Advanced,
            "Advanced: response orchestration",
            "A multi-surface flow that coordinates supplier, file, and incident work into one response packet.",
            "/demo/workflows/response-orchestration?reset=true",
            "Open response flow",
            "Response assistant",
            "Assess, advance, recover, prepare.",
            "Assess readiness. Advance next stage. Prepare packet.",
            "Response Orchestration Agent",
            [
                "Assess cross-system response readiness",
                "Advance the next guided subsystem stage",
                "Prepare the response packet"
            ],
            [
                "Multiple workflow surfaces",
                "Guided recovery",
                "Cross-page packet preparation"
            ])
        {
            ShowOnLaunchpad = false
        },
        new(
            "release-dossier",
            DemoScenarioScale.Advanced,
            "Advanced: release dossier",
            "A second multi-surface flow that pulls recipe and evidence checks into a release approval package.",
            "/demo/workflows/release-dossier?reset=true",
            "Open release flow",
            "Release assistant",
            "Assess, advance, recover, prepare.",
            "Assess readiness. Advance next stage. Prepare dossier.",
            "Release Dossier Agent",
            [
                "Assess release readiness",
                "Advance the next guided stage",
                "Prepare the release dossier"
            ],
            [
                "Recipe readiness",
                "Evidence bundle checks",
                "Release approval package"
            ])
        {
            ShowOnLaunchpad = false
        },
        new(
            "supplier-compliance",
            DemoScenarioScale.Advanced,
            "Supplier compliance",
            "A focused supplier-risk workflow used by the larger response orchestration flow.",
            "/demo/workflows/supplier-compliance",
            "Open supplier flow",
            "Supplier assistant",
            "Review risk, clear blockers, prepare.",
            "Explain risk. Recover. Prepare.",
            "Supplier Compliance Agent",
            [
                "Explain why the current suppliers are at risk",
                "Recover the blocked suppliers",
                "Prepare the remediation draft"
            ],
            [
                "Component-level focus",
                "Risk blockers",
                "Draft remediation"
            ])
        {
            ShowOnLaunchpad = false
        },
        new(
            "file-audit-bundle",
            DemoScenarioScale.Advanced,
            "File audit bundle",
            "A focused evidence workflow used by the larger orchestration flows.",
            "/demo/workflows/file-audit-bundle",
            "Open evidence flow",
            "Evidence assistant",
            "Prepare, recover, retry.",
            "Explain blockers. Recover. Prepare.",
            "File Workflow Agent",
            [
                "Explain the evidence blockers",
                "Recover the missing file",
                "Prepare the audit bundle"
            ],
            [
                "File workflow",
                "Recoverable blockers",
                "Audit-ready output"
            ])
        {
            ShowOnLaunchpad = false
        },
        new(
            "recipe-release",
            DemoScenarioScale.Advanced,
            "Recipe release",
            "A focused recipe readiness workflow used by the release dossier flow.",
            "/demo/workflows/recipe-release",
            "Open recipe flow",
            "Recipe assistant",
            "Assess, recover, stage.",
            "Assess readiness. Recover. Prepare.",
            "Recipe Release Agent",
            [
                "Assess recipe readiness",
                "Recover missing evidence",
                "Prepare the release draft"
            ],
            [
                "Recipe readiness",
                "Recovery",
                "Release staging"
            ])
        {
            ShowOnLaunchpad = false
        },
        new(
            "incident-escalation",
            DemoScenarioScale.Advanced,
            "Incident escalation",
            "A focused incident workflow used by the response orchestration flow.",
            "/demo/workflows/incident-escalation",
            "Open incident flow",
            "Incident assistant",
            "Triage, recover, hand off.",
            "Summarize triage. Recover. Submit.",
            "Incident Escalation Agent",
            [
                "Summarize incident triage",
                "Recover missing evidence",
                "Submit the handoff"
            ],
            [
                "Incident triage",
                "Evidence recovery",
                "Safe handoff"
            ])
        {
            ShowOnLaunchpad = false
        },
        new(
            "runtime-probe",
            DemoScenarioScale.Advanced,
            "Runtime probe",
            "A narrow runtime behavior probe for structured errors, cancellation, and long-running action handling.",
            "/demo/workflows/runtime-probe",
            "Open runtime probe",
            "Runtime probe assistant",
            "Run a structured error probe, then inspect the recovery hint.",
            "Run the structured error date range probe.",
            "Runtime Probe Agent",
            [
                "Run the structured error date range probe",
                "Run the runtime cancellation probe",
                "Stop the probe"
            ],
            [
                "Structured errors",
                "Runtime behavior",
                "Cancellation",
                "Long-running action visibility"
            ])
        {
            ShowOnLaunchpad = false
        }
    ];

    public static IReadOnlyList<DemoScenario> LaunchpadScenarios { get; }
        = Scenarios.Where(static scenario => scenario.ShowOnLaunchpad).ToArray();

    public static IEnumerable<DemoScenario> ForScale(DemoScenarioScale scale)
        => Scenarios.Where(scenario => scenario.Scale == scale);

    public static DemoScenario? FindByPath(string path)
    {
        var normalizedPath = NormalizePath(path);

        return Scenarios
            .Where(scenario => NormalizePath(scenario.Route) == normalizedPath)
            .OrderBy(static scenario => scenario.Scale)
            .FirstOrDefault();
    }

    private static string NormalizePath(string route)
    {
        var path = route.Split('?', 2, StringSplitOptions.TrimEntries)[0].TrimEnd('/');
        return string.IsNullOrWhiteSpace(path) ? "/" : path.ToLowerInvariant();
    }
}
