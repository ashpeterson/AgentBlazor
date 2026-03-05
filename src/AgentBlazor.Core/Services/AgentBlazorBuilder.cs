using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.State;
using AgentBlazor.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor.Services;

public sealed class AgentBlazorBuilder
{
    private readonly AgentBlazorConfigurationStore _store;

    internal AgentBlazorBuilder(IServiceCollection services, AgentBlazorConfigurationStore store)
    {
        Services = services;
        _store = store;
    }

    public IServiceCollection Services { get; }

    public AgentBlazorBuilder AddAgent(string name, Action<AgentRegistrationBuilder>? configure = null)
    {
        var builder = new AgentRegistrationBuilder(name);
        configure?.Invoke(builder);
        var registration = builder.Build();

        _store.AgentRegistrations.RemoveAll(r => string.Equals(r.Name, registration.Name, StringComparison.OrdinalIgnoreCase));
        _store.AgentRegistrations.Add(registration);
        return this;
    }

    public AgentBlazorBuilder ConfigureComponentCatalog(Action<ComponentCapabilityCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _store.ComponentCatalogConfigurators.Add(configure);
        return this;
    }

    public AgentBlazorBuilder UseAgentCapabilityPreset(AgentCapabilityPreset preset)
    {
        _store.ComponentCatalogConfigurators.Add(builder =>
            AgentCapabilityPresets.Apply(builder, preset));
        return this;
    }

    /// <summary>
    /// Enables prompt tracing for observability and debugging.
    /// When enabled, all prompt requests are traced through the pipeline with timing and results.
    /// </summary>
    /// <param name="configure">Optional configuration for tracing options.</param>
    /// <returns>The builder for chaining.</returns>
    public AgentBlazorBuilder EnablePromptTracing(Action<PromptTracingOptions>? configure = null)
    {
        Services.Configure<PromptTracingOptions>(options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });
        return this;
    }

    /// <summary>
    /// Registers a runtime lifecycle event subscriber.
    /// Multiple subscribers can be registered and are invoked in registration order.
    /// </summary>
    public AgentBlazorBuilder AddRuntimeEventSubscriber<TSubscriber>()
        where TSubscriber : class, IAgentRuntimeEventSubscriber
    {
        Services.AddSingleton<IAgentRuntimeEventSubscriber, TSubscriber>();
        return this;
    }

    /// <summary>
    /// Registers a runtime lifecycle event subscriber instance.
    /// </summary>
    public AgentBlazorBuilder AddRuntimeEventSubscriber(IAgentRuntimeEventSubscriber subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        Services.AddSingleton(subscriber);
        Services.AddSingleton<IAgentRuntimeEventSubscriber>(subscriber);
        return this;
    }

    /// <summary>
    /// Replaces the default conversation store with a custom implementation.
    /// </summary>
    public AgentBlazorBuilder UseConversationStore<TStore>()
        where TStore : class, IConversationStore
    {
        Services.RemoveAll<IConversationStore>();
        Services.AddSingleton<IConversationStore, TStore>();
        return this;
    }

    /// <summary>
    /// Replaces the default conversation store with a factory-backed implementation.
    /// </summary>
    public AgentBlazorBuilder UseConversationStore(Func<IServiceProvider, IConversationStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.RemoveAll<IConversationStore>();
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>
    /// Uses a JSON file-backed conversation store for persistence across process restarts.
    /// </summary>
    public AgentBlazorBuilder UseJsonFileConversationStore(
        string filePath,
        Action<ConversationOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Services.Configure<ConversationOptions>(options =>
        {
            options.PersistAcrossRestarts = true;
            configure?.Invoke(options);
        });

        return UseConversationStore(sp => new JsonFileConversationStore(
            filePath,
            sp.GetService<Microsoft.Extensions.Options.IOptions<ConversationOptions>>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<JsonFileConversationStore>>()));
    }

    /// <summary>
    /// Replaces the default shared-state store with a custom implementation.
    /// </summary>
    public AgentBlazorBuilder UseSharedStateStore<TStore>()
        where TStore : class, IAgentSharedStateStore
    {
        Services.RemoveAll<IAgentSharedStateStore>();
        Services.AddSingleton<IAgentSharedStateStore, TStore>();
        return this;
    }

    /// <summary>
    /// Replaces the default shared-state store with a factory-backed implementation.
    /// </summary>
    public AgentBlazorBuilder UseSharedStateStore(Func<IServiceProvider, IAgentSharedStateStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.RemoveAll<IAgentSharedStateStore>();
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>
    /// Uses a JSON file-backed shared-state store for persistence across process restarts.
    /// </summary>
    public AgentBlazorBuilder UseJsonFileSharedStateStore(
        string filePath,
        Action<SharedStateOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Services.Configure<SharedStateOptions>(options =>
        {
            configure?.Invoke(options);
        });

        return UseSharedStateStore(sp => new JsonFileAgentSharedStateStore(
            filePath,
            sp.GetService<Microsoft.Extensions.Options.IOptions<SharedStateOptions>>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<JsonFileAgentSharedStateStore>>()));
    }
}
