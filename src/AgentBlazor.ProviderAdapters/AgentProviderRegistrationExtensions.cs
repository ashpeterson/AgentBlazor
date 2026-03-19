using AgentBlazor.Options;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace AgentBlazor.ProviderAdapters;

public static class AgentProviderRegistrationExtensions
{
    public static IServiceCollection AddOpenAIProvider(
        this IServiceCollection services,
        string model,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<AgentBlazorOptions>(options =>
        {
            options.Provider.Kind = AgentProviderKind.OpenAI;
            options.Provider.Model = model;
            options.Provider.ApiKey = apiKey;
        });
        services.Replace(ServiceDescriptor.Singleton<IChatClient>(_ =>
        {
            var client = new OpenAIClient(new ApiKeyCredential(apiKey));
            return client.GetChatClient(model).AsIChatClient();
        }));

        return services;
    }

    public static IServiceCollection AddOpenAIProvider(
        this IServiceCollection services,
        string model,
        string apiKey,
        string endpoint)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var normalizedEndpoint = endpoint.TrimEnd('/');

        services.Configure<AgentBlazorOptions>(options =>
        {
            options.Provider.Kind = AgentProviderKind.OpenAI;
            options.Provider.Model = model;
            options.Provider.ApiKey = apiKey;
            options.Provider.Endpoint = normalizedEndpoint;
        });
        services.Replace(ServiceDescriptor.Singleton<IChatClient>(_ =>
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(normalizedEndpoint, UriKind.Absolute)
                });
            return client.GetChatClient(model).AsIChatClient();
        }));

        return services;
    }

    public static IServiceCollection AddAzureOpenAIProvider(
        this IServiceCollection services,
        string endpoint,
        string deploymentName) =>
        services.AddAzureOpenAIProvider(
            endpoint,
            deploymentName,
            Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"));

    public static IServiceCollection AddAzureOpenAIProvider(
        this IServiceCollection services,
        string endpoint,
        string deploymentName,
        string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<AgentBlazorOptions>(options =>
        {
            options.Provider.Kind = AgentProviderKind.AzureOpenAI;
            options.Provider.Endpoint = endpoint;
            options.Provider.DeploymentName = deploymentName;
            options.Provider.ApiKey = apiKey;
        });
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            services.Replace(ServiceDescriptor.Singleton<IChatClient>(_ =>
            {
                var client = new AzureOpenAIClient(
                    new Uri(endpoint),
                    new ApiKeyCredential(apiKey));
                return client.GetChatClient(deploymentName).AsIChatClient();
            }));
        }

        return services;
    }

    public static IServiceCollection AddOriginAIProvider(
        this IServiceCollection services,
        string endpoint,
        string apiKey,
        string tenantInfo = "origin-ai",
        string chatSource = "agentblazor",
        string? systemPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantInfo);

        var normalizedEndpoint = endpoint.TrimEnd('/');

        services.Configure<AgentBlazorOptions>(options =>
        {
            options.Provider.Kind = AgentProviderKind.Custom;
            options.Provider.Endpoint = normalizedEndpoint;
            options.Provider.ApiKey = apiKey;
            options.Provider.AdditionalSettings["Provider"] = "OriginAI";
            options.Provider.AdditionalSettings["TenantInfo"] = tenantInfo;
            options.Provider.AdditionalSettings["ChatSource"] = chatSource;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                options.Provider.AdditionalSettings["SystemPrompt"] = systemPrompt;
            }
        });

        services.AddHttpClient("AgentBlazor.OriginAI", client =>
        {
            client.BaseAddress = new Uri(normalizedEndpoint, UriKind.Absolute);
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            client.DefaultRequestHeaders.Add("X-Tenant-Info", tenantInfo);
        });

        services.Replace(ServiceDescriptor.Singleton<IChatClient>(sp =>
            new OriginAIChatClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("AgentBlazor.OriginAI"),
                chatSource,
                systemPrompt)));

        return services;
    }

    public static IServiceCollection AddOllamaProvider(
        this IServiceCollection services,
        string model,
        string endpoint = "http://127.0.0.1:11434/v1",
        string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var normalizedEndpoint = endpoint.TrimEnd('/');
        var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? "ollama" : apiKey;

        services.Configure<AgentBlazorOptions>(options =>
        {
            options.Provider.Kind = AgentProviderKind.Ollama;
            options.Provider.Model = model;
            options.Provider.Endpoint = normalizedEndpoint;
            options.Provider.ApiKey = effectiveApiKey;
        });
        services.Replace(ServiceDescriptor.Singleton<IChatClient>(_ =>
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(effectiveApiKey),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(normalizedEndpoint, UriKind.Absolute)
                });

            return client.GetChatClient(model).AsIChatClient();
        }));

        return services;
    }
}
