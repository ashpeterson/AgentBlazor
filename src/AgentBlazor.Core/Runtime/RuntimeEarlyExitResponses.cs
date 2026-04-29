using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Licensing;

namespace AgentBlazor.Core.Runtime;

internal static class RuntimeEarlyExitResponses
{
    public const string NoProviderConfiguredTraceMessage = "No AI provider configured";

    public static AgentTurnResponse BuildNoAgentResponse(
        int registeredCount,
        string? requestedAgentName,
        IDictionary<string, string>? context)
        => new(
            "none",
            RuntimeTurnPreflight.BuildNoAgentResponseText(registeredCount, requestedAgentName, context),
            [],
            []);

    public static AgentTurnResponse BuildNoAllowedActionsResponse(
        string agentName,
        IReadOnlyList<string> blockedByAgentPolicy,
        IReadOnlyList<string> blockedByTier,
        AgentBlazorTier effectiveTier,
        string actionLabel)
        => new(
            agentName,
            BuildNoAllowedActionsResponseText(
                blockedByAgentPolicy,
                blockedByTier,
                effectiveTier,
                actionLabel),
            [],
            []);

    public static string BuildNoAllowedActionsResponseText(
        IReadOnlyList<string> blockedByAgentPolicy,
        IReadOnlyList<string> blockedByTier,
        AgentBlazorTier effectiveTier,
        string actionLabel)
        => RuntimeTurnPreflight.BuildNoAllowedActionsResponseText(
            blockedByAgentPolicy,
            blockedByTier,
            effectiveTier,
            actionLabel);

    public static AgentTurnResponse BuildProviderMissingResponse(string agentName)
        => new(
            agentName,
            "**No AI provider configured.** " +
            "Add one of the following to your `Program.cs`:\n\n" +
            "```csharp\n" +
            "// OpenAI\n" +
            "options.UseOpenAI(apiKey: \"sk-...\", model: \"gpt-4o-mini\");\n\n" +
            "// Azure OpenAI\n" +
            "options.UseAzureOpenAI(endpoint: \"https://...\", deploymentName: \"...\", apiKey: \"...\");\n" +
            "// Or pass a TokenCredential such as DefaultAzureCredential for managed identity.\n\n" +
            "// Ollama (free, local)\n" +
            "options.UseOllama(model: \"llama3.2\");\n" +
            "```\n\n" +
            "Set your provider credentials via environment variables or app configuration.",
            [],
            []);
}
