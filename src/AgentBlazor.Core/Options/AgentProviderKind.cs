namespace AgentBlazor.Options;

public enum AgentProviderKind
{
    None = 0,
    OpenAI = 1,
    AzureOpenAI = 2,
    Anthropic = 3,
    Ollama = 4,
    Custom = 100
}
