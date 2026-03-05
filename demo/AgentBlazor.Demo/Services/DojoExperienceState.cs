namespace AgentBlazor.Demo.Services;

internal sealed class DojoExperienceState
{
    private const string DefaultIntegration = "Microsoft Agent Framework (.NET)";

    private static readonly IReadOnlyDictionary<string, DojoAssistantProfile> Profiles =
        new Dictionary<string, DojoAssistantProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentic-chat"] = new(
                ExampleKey: "agentic-chat",
                AssistantDescription: "Agentic chat mode for mounted wrappers on the dojo route.",
                Placeholder: "Try: summarize what is mounted on this route, list components I can control here.",
                RequireHandoffApproval: false,
                EnableGeneratedUi: false),
            ["backend-tool-rendering"] = new(
                ExampleKey: "backend-tool-rendering",
                AssistantDescription: "Tool rendering mode. Ask for submit flows and inspect lifecycle output.",
                Placeholder: "Try: set action label to policy review and submit backend demo form.",
                RequireHandoffApproval: false,
                EnableGeneratedUi: false),
            ["human-loop"] = new(
                ExampleKey: "human-loop",
                AssistantDescription: "Human-in-the-loop mode. Approvals are required before handoffs execute.",
                Placeholder: "Try: request approval before submitting and then approve the pending action.",
                RequireHandoffApproval: true,
                EnableGeneratedUi: false),
            ["agentic-generative-ui"] = new(
                ExampleKey: "agentic-generative-ui",
                AssistantDescription: "Agentic generative UI mode with generated surface output in chat.",
                Placeholder: "Try: generate a dashboard card from this recipe and include a summary table.",
                RequireHandoffApproval: false,
                EnableGeneratedUi: true),
            ["tool-based-generative-ui"] = new(
                ExampleKey: "tool-based-generative-ui",
                AssistantDescription: "Tool-based generative UI mode with runtime-forwarded block actions.",
                Placeholder: "Try: click a generated action or ask for a generated form tool.",
                RequireHandoffApproval: false,
                EnableGeneratedUi: true),
            ["shared-state"] = new(
                ExampleKey: "shared-state",
                AssistantDescription: "Shared state mode for collaborative recipe editing across agent and UI.",
                Placeholder: "Try: set recipe title to test, add ingredient chives amount 1 tbsp optional true.",
                RequireHandoffApproval: false,
                EnableGeneratedUi: false),
            ["predictive-state"] = new(
                ExampleKey: "predictive-state",
                AssistantDescription: "Predictive state mode for incremental streaming updates.",
                Placeholder: "Try: draft next three recipe steps and apply suggestions incrementally.",
                RequireHandoffApproval: false,
                EnableGeneratedUi: true)
        };

    public event Action? Changed;

    public string SelectedExampleKey { get; private set; } = "shared-state";

    public string SelectedViewMode { get; private set; } = "preview";

    public string SelectedIntegration { get; private set; } = DefaultIntegration;

    public DojoAssistantProfile CurrentAssistantProfile => ResolveProfile(SelectedExampleKey);

    public void SetExample(string? key)
    {
        var normalized = NormalizeExampleKey(key);
        if (string.Equals(normalized, SelectedExampleKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedExampleKey = normalized;
        Changed?.Invoke();
    }

    public void SetViewMode(string? viewMode)
    {
        var normalized = NormalizeViewMode(viewMode);
        if (string.Equals(normalized, SelectedViewMode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedViewMode = normalized;
        Changed?.Invoke();
    }

    public void SetIntegration(string? integration)
    {
        var normalized = string.IsNullOrWhiteSpace(integration)
            ? DefaultIntegration
            : integration.Trim();

        if (string.Equals(normalized, SelectedIntegration, StringComparison.Ordinal))
        {
            return;
        }

        SelectedIntegration = normalized;
        Changed?.Invoke();
    }

    private static string NormalizeViewMode(string? viewMode)
    {
        if (string.Equals(viewMode, "code", StringComparison.OrdinalIgnoreCase))
        {
            return "code";
        }

        if (string.Equals(viewMode, "docs", StringComparison.OrdinalIgnoreCase))
        {
            return "docs";
        }

        return "preview";
    }

    private static string NormalizeExampleKey(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key) && Profiles.ContainsKey(key.Trim()))
        {
            return key.Trim().ToLowerInvariant();
        }

        return "shared-state";
    }

    private static DojoAssistantProfile ResolveProfile(string key)
        => Profiles.TryGetValue(key, out var profile)
            ? profile
            : Profiles["shared-state"];
}

internal sealed record DojoAssistantProfile(
    string ExampleKey,
    string AssistantDescription,
    string Placeholder,
    bool RequireHandoffApproval,
    bool EnableGeneratedUi);
