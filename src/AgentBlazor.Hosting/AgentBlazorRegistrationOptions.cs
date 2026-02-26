using System.Reflection;
using AgentBlazor.Options;
using AgentBlazor.ProviderAdapters;
using AgentBlazor.Services;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor;

public sealed class AgentBlazorRegistrationOptions
{
    private Action<IServiceCollection>? _providerRegistration;
    private Action<IServiceCollection>? _serviceRegistration;
    private Action<AgentBlazorOptions>? _optionsConfiguration;
    private Action<AgentBlazorBuilder>? _builderConfiguration;
    private readonly List<Assembly> _agentPageAssemblies = [];

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

    /// <summary>
    /// Registers a default chart data resolver used by generated chart blocks
    /// when component-level resolver parameters are not explicitly provided.
    /// </summary>
    public void UseChartDataResolver(AgentChartDataResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _serviceRegistration += services => services.Replace(ServiceDescriptor.Singleton(resolver));
    }

    /// <summary>
    /// Registers a default chart data resolver using dependency injection.
    /// Useful when chart data comes from app services such as repositories or DbContexts.
    /// </summary>
    public void UseChartDataResolver(Func<IServiceProvider, AgentChartDataResolver> resolverFactory)
    {
        ArgumentNullException.ThrowIfNull(resolverFactory);
        _serviceRegistration += services => services.Replace(
            ServiceDescriptor.Singleton<AgentChartDataResolver>(sp => resolverFactory(sp)));
    }

    /// <summary>
    /// Adds an assembly to the list scanned at startup for [Route] pages (used for intent→route and planner).
    /// If never called, the entry assembly is scanned by default.
    /// </summary>
    public void AddAssemblyToScan(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _agentPageAssemblies.Add(assembly);
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

    /// <summary>
    /// Enables or disables paid persistent memory routing.
    /// When disabled, conversation and preference stores remain in-memory.
    /// </summary>
    public void EnablePaidPersistentMemory(bool enabled = true)
    {
        _optionsConfiguration += options => options.PaidFeatures.EnablePersistentMemory = enabled;
    }

    /// <summary>
    /// Requires paid persistent providers when persistent memory is enabled.
    /// If providers are missing and this is true, runtime calls throw instead of falling back.
    /// </summary>
    public void RequirePaidPersistentProviders(bool required = true)
    {
        _optionsConfiguration += options => options.PaidFeatures.RequirePersistentProviders = required;
    }

    /// <summary>
    /// Registers a paid persistent conversation provider.
    /// </summary>
    public void UsePersistentConversationStore<TStore>()
        where TStore : class, IPersistentConversationStore
    {
        _optionsConfiguration += options => options.PaidFeatures.EnablePersistentMemory = true;
        _serviceRegistration += services => services.TryAddSingleton<IPersistentConversationStore, TStore>();
    }

    /// <summary>
    /// Registers a paid persistent conversation provider using a factory.
    /// </summary>
    public void UsePersistentConversationStore(Func<IServiceProvider, IPersistentConversationStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _optionsConfiguration += options => options.PaidFeatures.EnablePersistentMemory = true;
        _serviceRegistration += services =>
            services.TryAddSingleton<IPersistentConversationStore>(sp => factory(sp));
    }

    /// <summary>
    /// Registers a paid persistent user preference provider.
    /// </summary>
    public void UsePersistentUserPreferenceService<TService>()
        where TService : class, IPersistentUserPreferenceService
    {
        _optionsConfiguration += options => options.PaidFeatures.EnablePersistentMemory = true;
        _serviceRegistration += services => services.TryAddSingleton<IPersistentUserPreferenceService, TService>();
    }

    /// <summary>
    /// Registers a paid persistent user preference provider using a factory.
    /// </summary>
    public void UsePersistentUserPreferenceService(Func<IServiceProvider, IPersistentUserPreferenceService> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _optionsConfiguration += options => options.PaidFeatures.EnablePersistentMemory = true;
        _serviceRegistration += services =>
            services.TryAddSingleton<IPersistentUserPreferenceService>(sp => factory(sp));
    }

    internal void ApplyProvider(IServiceCollection services)
    {
        _providerRegistration?.Invoke(services);
        _serviceRegistration?.Invoke(services);
    }

    internal void ApplyOptions(AgentBlazorOptions options)
    {
        options.DefaultAgent.Enabled = true;

        var assembliesToScan = _agentPageAssemblies.Count > 0
            ? _agentPageAssemblies
            : GetDefaultAgentPageAssemblies();
        foreach (var assembly in assembliesToScan)
        {
            options.AssembliesToScan.Add(assembly);
        }

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

    private static List<Assembly> GetDefaultAgentPageAssemblies()
    {
        var list = new List<Assembly>();
        var entry = Assembly.GetEntryAssembly();
        if (entry is not null)
            list.Add(entry);
        return list;
    }
}
