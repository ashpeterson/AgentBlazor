using AgentBlazor.Options;
using AgentBlazor.ProviderAdapters;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor;

public sealed class AgentBlazorRegistrationOptions
{
    private Action<IServiceCollection>? _providerRegistration;
    private Action<AgentBlazorOptions>? _optionsConfiguration;
    private Action<AgentBlazorBuilder>? _builderConfiguration;

    public string? AgentName { get; set; }

    public string? AgentDescription { get; set; }

    public string? AgentInstructions { get; set; }

    public void UseOpenAI(string apiKey, string model = "gpt-4o-mini")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _providerRegistration = services => services.AddOpenAIProvider(model, apiKey);
    }

    public void UseAzureOpenAI(string endpoint, string deploymentName, string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        _providerRegistration = string.IsNullOrWhiteSpace(apiKey)
            ? services => services.AddAzureOpenAIProvider(endpoint, deploymentName)
            : services => services.AddAzureOpenAIProvider(endpoint, deploymentName, apiKey);
    }

    public void UseOllama(
        string model,
        string endpoint = "http://127.0.0.1:11434/v1",
        string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        _providerRegistration = services => services.AddOllamaProvider(model, endpoint, apiKey);
    }

    public void Configure(Action<AgentBlazorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _optionsConfiguration += configure;
    }

    public void ConfigureBuilder(Action<AgentBlazorBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _builderConfiguration += configure;
    }

    internal void ApplyProvider(IServiceCollection services) => _providerRegistration?.Invoke(services);

    internal void ApplyOptions(AgentBlazorOptions options)
    {
        options.DefaultAgent.Enabled = true;

        if (!string.IsNullOrWhiteSpace(AgentName))
        {
            options.DefaultAgent.Name = AgentName;
        }

        if (!string.IsNullOrWhiteSpace(AgentDescription))
        {
            options.DefaultAgent.Description = AgentDescription;
        }

        if (!string.IsNullOrWhiteSpace(AgentInstructions))
        {
            options.DefaultAgent.Instructions = AgentInstructions;
        }

        _optionsConfiguration?.Invoke(options);
    }

    internal void ApplyBuilder(AgentBlazorBuilder builder) => _builderConfiguration?.Invoke(builder);
}
