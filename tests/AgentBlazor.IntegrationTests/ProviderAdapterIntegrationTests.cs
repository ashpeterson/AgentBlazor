using AgentBlazor;
using AgentBlazor.Options;
using AgentBlazor.ProviderAdapters;
using AgentBlazor.Runtime;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentBlazor.IntegrationTests;

public class ProviderAdapterIntegrationTests
{
    [Fact]
    public void AddAgentBlazor_RegistersRuntimeAndHosting_WithSensibleDefaults()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var runtime = provider.GetRequiredService<IAgentRuntime>();

        Assert.True(options.DefaultAgent.Enabled);
        Assert.Equal("AgentBlazor UI Agent", options.DefaultAgent.Name);
        Assert.NotNull(runtime);
    }

    [Fact]
    public void AddAgentBlazor_WithUseOpenAI_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(
            services,
            options => options.UseOpenAI("demo-api-key", "gpt-4o-mini"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal("gpt-4o-mini", options.Provider.Model);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void OpenAiProvider_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        services.AddOpenAIProvider("gpt-4o-mini", "demo-api-key");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal("gpt-4o-mini", options.Provider.Model);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void OllamaProvider_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        services.AddOllamaProvider("qwen2.5-coder:7b");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.Ollama, options.Provider.Kind);
        Assert.Equal("qwen2.5-coder:7b", options.Provider.Model);
        Assert.Equal("http://127.0.0.1:11434/v1", options.Provider.Endpoint);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public async Task OllamaProvider_RunTurnAsync_WorksWhenLocalOllamaAvailable()
    {
        if (!await IsOllamaAvailableAsync())
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddOllamaProvider("qwen2.5-coder:7b");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntime>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var response = await runtime.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only."),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));
        Assert.DoesNotContain("No provider is configured", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> IsOllamaAvailableAsync()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:11434")
        };

        try
        {
            using var response = await httpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
