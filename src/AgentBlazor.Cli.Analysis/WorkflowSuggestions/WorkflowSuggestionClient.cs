using System.ClientModel;
using AgentBlazor.Cli.Analysis.Models;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AgentBlazor.Cli.Analysis.WorkflowSuggestions;

public interface IWorkflowSuggestionClient
{
    Task<WorkflowSuggestionSet> GenerateAsync(ProjectModel model, CancellationToken ct = default);
}

public sealed class WorkflowSuggestionClient(
    WorkflowSuggestionPromptBuilder promptBuilder,
    WorkflowSuggestionParser parser,
    ChatClient chatClient,
    string modelName) : IWorkflowSuggestionClient
{
    public async Task<WorkflowSuggestionSet> GenerateAsync(ProjectModel model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var prompt = promptBuilder.Build(model);
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a precise software-analysis assistant. Return valid JSON only."),
            new UserChatMessage(prompt)
        };

        var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            Temperature = 0.1f,
            MaxOutputTokenCount = 2500
        }, ct).ConfigureAwait(false);

        var text = string.Concat(response.Value.Content.Select(part => part.Text));
        return parser.ParseAndValidate(text, model, modelName);
    }
}

public sealed record WorkflowSuggestionClientOptions
{
    public string Provider { get; init; } = "openai";

    public string Model { get; init; } = "gpt-4o-mini";

    public string? OpenAIApiKey { get; init; }

    public string? AzureOpenAIEndpoint { get; init; }

    public string? AzureOpenAIApiKey { get; init; }

    public string? AzureOpenAIDeployment { get; init; }
}

public static class WorkflowSuggestionClientFactory
{
    public static IWorkflowSuggestionClient Create(WorkflowSuggestionClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var promptBuilder = new WorkflowSuggestionPromptBuilder();
        var parser = new WorkflowSuggestionParser();

        if (options.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.OpenAIApiKey))
            {
                throw new InvalidOperationException(
                    "No OpenAI API key configured for `agentblazor analyze`. Set OPENAI_API_KEY or OpenAI__ApiKey, and optionally set AGENTBLAZOR_ANALYZE_MODEL.");
            }

            var client = new OpenAI.OpenAIClient(options.OpenAIApiKey);
            var chatClient = client.GetChatClient(options.Model);
            return new WorkflowSuggestionClient(promptBuilder, parser, chatClient, options.Model);
        }

        if (options.Provider.Equals("azure-openai", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.AzureOpenAIEndpoint) ||
                string.IsNullOrWhiteSpace(options.AzureOpenAIDeployment) ||
                string.IsNullOrWhiteSpace(options.AzureOpenAIApiKey))
            {
                throw new InvalidOperationException(
                    "Azure OpenAI is configured for `agentblazor analyze`, but configuration is incomplete. Set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT, and AZURE_OPENAI_API_KEY.");
            }

            var client = new AzureOpenAIClient(
                new Uri(options.AzureOpenAIEndpoint, UriKind.Absolute),
                new ApiKeyCredential(options.AzureOpenAIApiKey));
            var chatClient = client.GetChatClient(options.AzureOpenAIDeployment);
            return new WorkflowSuggestionClient(promptBuilder, parser, chatClient, options.AzureOpenAIDeployment);
        }

        throw new InvalidOperationException(
            $"Unsupported analyze provider '{options.Provider}'. Supported v1 providers are 'openai' and 'azure-openai'.");
    }

    public static WorkflowSuggestionClientOptions FromEnvironment(AgentBlazorConfig? config)
    {
        var provider = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AGENTBLAZOR_ANALYZE_PROVIDER"),
            config?.AnalyzeProvider,
            "openai")!;

        var model = FirstNonEmpty(
            Environment.GetEnvironmentVariable("AGENTBLAZOR_ANALYZE_MODEL"),
            Environment.GetEnvironmentVariable("OPENAI_MODEL"),
            Environment.GetEnvironmentVariable("OpenAI__Model"),
            config?.AnalyzeModel,
            "gpt-4o-mini")!;

        return new WorkflowSuggestionClientOptions
        {
            Provider = provider,
            Model = model,
            OpenAIApiKey = FirstNonEmpty(
                Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                Environment.GetEnvironmentVariable("OpenAI__ApiKey")),
            AzureOpenAIEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"),
            AzureOpenAIDeployment = FirstNonEmpty(
                Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT"),
                Environment.GetEnvironmentVariable("AzureOpenAI__DeploymentName"),
                model),
            AzureOpenAIApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
