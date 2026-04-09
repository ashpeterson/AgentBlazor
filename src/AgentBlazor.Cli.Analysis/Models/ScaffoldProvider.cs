namespace AgentBlazor.Cli.Analysis.Models;

public enum ScaffoldProvider
{
    OpenAI,
    AzureOpenAI,
    Ollama
}

public static class ScaffoldProviders
{
    public const string SupportedValues = "openai, azure-openai, ollama";

    public static ScaffoldProvider? ParseOrThrow(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "openai" => ScaffoldProvider.OpenAI,
            "azure-openai" => ScaffoldProvider.AzureOpenAI,
            "ollama" => ScaffoldProvider.Ollama,
            _ => throw new InvalidOperationException(
                $"Unsupported provider '{value}'. Supported values: {SupportedValues}.")
        };
    }

    public static string ToOptionValue(this ScaffoldProvider provider) =>
        provider switch
        {
            ScaffoldProvider.OpenAI => "openai",
            ScaffoldProvider.AzureOpenAI => "azure-openai",
            ScaffoldProvider.Ollama => "ollama",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    public static string ToDisplayName(this ScaffoldProvider provider) =>
        provider switch
        {
            ScaffoldProvider.OpenAI => "OpenAI",
            ScaffoldProvider.AzureOpenAI => "Azure OpenAI",
            ScaffoldProvider.Ollama => "Ollama",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    public static string GetConfigurationHint(this ScaffoldProvider provider) =>
        provider switch
        {
            ScaffoldProvider.OpenAI =>
                "Set OpenAI:ApiKey and optionally OpenAI:Model in configuration or environment variables.",
            ScaffoldProvider.AzureOpenAI =>
                "Set AzureOpenAI:Endpoint, AzureOpenAI:DeploymentName, and optionally AzureOpenAI:ApiKey in configuration.",
            ScaffoldProvider.Ollama =>
                "Set Ollama:Model and optionally Ollama:Endpoint and Ollama:ApiKey in configuration.",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
}
