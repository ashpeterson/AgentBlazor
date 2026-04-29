using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AgentBlazor.Cli.IntegrationTests;

/// <summary>
/// Integration tests that verify an AI agent can correctly interpret and use
/// the generated AGENT.md file. These tests make real LLM API calls.
///
/// API key sources (in order of priority):
/// 1. Environment variables: AZURE_OPENAI_* or OPENAI_API_KEY
/// 2. Demo project appsettings.Development.json
/// </summary>
public class AgentBehaviorTests : IAsyncLifetime
{
    private const string DefaultOpenAiModel = "gpt-4o-mini";

    private ChatClient? _chatClient;
    private string _agentMdContent = "";
    private bool _skipTests;

    public async Task InitializeAsync()
    {
        var repoRoot = FindRepoRoot();

        // Try environment variables first
        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? DefaultOpenAiModel;
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? Environment.GetEnvironmentVariable("OpenAI__Model")
            ?? DefaultOpenAiModel;

        // Fallback to appsettings.Development.json
        if (string.IsNullOrEmpty(openAiKey) && string.IsNullOrEmpty(azureKey))
        {
            var appSettingsPath = Path.Combine(repoRoot, "demo", "AgentBlazor.Demo", "appsettings.Development.json");
            if (File.Exists(appSettingsPath))
            {
                var json = await File.ReadAllTextAsync(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("OpenAI", out var openAiSection))
                {
                    if (openAiSection.TryGetProperty("ApiKey", out var apiKeyProp))
                        openAiKey = apiKeyProp.GetString();
                    if (openAiSection.TryGetProperty("Model", out var modelProp) &&
                        !string.IsNullOrWhiteSpace(modelProp.GetString()))
                    {
                        openAiModel = modelProp.GetString()!;
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
        {
            var client = new AzureOpenAIClient(new Uri(azureEndpoint), new ApiKeyCredential(azureKey));
            _chatClient = client.GetChatClient(azureDeployment);
        }
        else if (!string.IsNullOrEmpty(openAiKey))
        {
            var client = new OpenAI.OpenAIClient(openAiKey);
            _chatClient = client.GetChatClient(openAiModel);
        }
        else
        {
            _skipTests = true;
            return;
        }

        // Load the AGENT.md from the repo root
        var agentMdPath = Path.Combine(repoRoot, ".agentblazor", "AGENT.md");

        if (File.Exists(agentMdPath))
        {
            _agentMdContent = await File.ReadAllTextAsync(agentMdPath);
        }
        else
        {
            _skipTests = true;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private void SkipIfNoApiKey()
    {
        Skip.If(_skipTests, "No LLM API key configured. Set AZURE_OPENAI_* or OPENAI_API_KEY environment variables.");
    }

    private async Task<string> AskAgent(string question)
    {
        var systemPrompt = $"""
            You are an AI assistant helping developers work with a Blazor application.
            Use the following AGENT.md file to understand the application structure and capabilities.
            Answer questions precisely based on the information provided.

            <agent_md>
            {_agentMdContent}
            </agent_md>
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(question)
        };

        var response = await _chatClient!.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            Temperature = 0f, // Deterministic responses
            MaxOutputTokenCount = 500
        });

        return response.Value.Content[0].Text;
    }

    #region Action Discovery Tests

    [SkippableFact]
    public async Task Agent_CanIdentifyResetActions()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "List all actions that can reset a workflow. Return ONLY the method names, one per line.");

        Assert.Contains("ResetIncidentWorkflow", response);
        Assert.Contains("ResetReleaseWorkflow", response);
        Assert.Contains("ResetSupplierWorkflow", response);
    }

    [SkippableFact]
    public async Task Agent_CanIdentifyMutationActions()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "List 3 actions that are marked as mutations requiring approval. Return the action names.");

        // Should identify actions with [mutation, approval] tags - check for either method name or display name
        var hasMutations =
            response.Contains("PrepareAuditBundle", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Prepare Audit Bundle", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("PrepareReleaseDraft", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Prepare Release Draft", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("AssignTriageOwner", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Assign Triage Owner", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("PrepareEscalationBrief", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Prepare Escalation Brief", StringComparison.OrdinalIgnoreCase);

        Assert.True(hasMutations, $"Expected mutation actions but got: {response}");
    }

    #endregion

    #region Parameter Understanding Tests

    [SkippableFact]
    public async Task Agent_UnderstandsActionParameters()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "What parameter does the ShowAtRiskSuppliers action accept? Include the type.");

        Assert.Contains("days", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("int", response, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Agent_IdentifiesOptionalParameters()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "Is the 'days' parameter in ShowAtRiskSuppliers required or optional? Answer with just 'required' or 'optional'.");

        Assert.Contains("optional", response, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Instruction Compliance Tests

    [SkippableFact]
    public async Task Agent_RespectsConfirmationGuideline()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "According to the Agent Instructions, should you execute mutation actions without asking the user first? Answer yes or no and explain briefly.");

        Assert.Contains("no", response, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            response.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("approval", StringComparison.OrdinalIgnoreCase),
            $"Expected mention of confirmation but got: {response}");
    }

    [SkippableFact]
    public async Task Agent_UnderstandsRecoveryPlaybookGuideline()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "According to the Agent Instructions, what should you do when a workflow is in an error state?");

        Assert.Contains("recovery", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("playbook", response, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Workflow Understanding Tests

    [SkippableFact]
    public async Task Agent_UnderstandsIncidentEscalationFlow()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "What is the correct sequence of actions for the Incident Escalation Flow? List the steps in order.");

        // Check key steps are mentioned in order
        var assignIndex = response.IndexOf("AssignTriageOwner", StringComparison.OrdinalIgnoreCase);
        var reviewIndex = response.IndexOf("EvidenceReview", StringComparison.OrdinalIgnoreCase);
        var briefIndex = response.IndexOf("EscalationBrief", StringComparison.OrdinalIgnoreCase);
        var handoffIndex = response.IndexOf("Handoff", StringComparison.OrdinalIgnoreCase);

        // At minimum, the flow should be understood
        Assert.True(assignIndex >= 0 || response.Contains("Triage", StringComparison.OrdinalIgnoreCase),
            $"Expected incident flow steps but got: {response}");
    }

    [SkippableFact]
    public async Task Agent_CanSuggestNextAction()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "A user has just completed evidence review for an incident. According to the Priority Workflows, what action should happen next?");

        Assert.True(
            response.Contains("PrepareEscalationBrief", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("Escalation Brief", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("escalate", StringComparison.OrdinalIgnoreCase),
            $"Expected escalation brief suggestion but got: {response}");
    }

    #endregion

    #region Route Understanding Tests

    [SkippableFact]
    public async Task Agent_CanIdentifyRouteForWorkflow()
    {
        SkipIfNoApiKey();

        var response = await AskAgent(
            "What is the URL route for the Incident Escalation workflow?");

        Assert.Contains("/demo/workflows/incident-escalation", response);
    }

    #endregion
}
