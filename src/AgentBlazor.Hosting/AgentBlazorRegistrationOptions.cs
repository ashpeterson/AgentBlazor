using System.Reflection;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.ProviderAdapters;
using AgentBlazor.Services;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Middleware;
using AgentBlazor.Core.Runtime.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentBlazor;

public sealed class AgentBlazorRegistrationOptions
{
    private Action<IServiceCollection>? _providerRegistration;
    private Action<IServiceCollection>? _runtimeAdapterRegistration;
    private Action<IServiceCollection>? _serviceRegistration;
    private Action<AgentBlazorOptions>? _optionsConfiguration;
    private Action<AgentBlazorBuilder>? _builderConfiguration;
    private readonly List<Assembly> _agentPageAssemblies = [];
    private readonly List<AgentServiceTool> _serviceTools = [];
    private readonly List<Func<IServiceCollection, IServiceCollection>> _mcpRegistrations = [];
    private readonly List<Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task>> _middlewares = [];
    private bool _legacyDefaultAgentCompatibilityEnabled;

    [Obsolete("Default-agent naming on AgentBlazorRegistrationOptions is a legacy compatibility path. Prefer ConfigureBuilder(builder => builder.AddAgent(...)) for explicit agent registration.", false)]
    public string? AgentName { get; set; }

    [Obsolete("Default-agent description on AgentBlazorRegistrationOptions is a legacy compatibility path. Prefer ConfigureBuilder(builder => builder.AddAgent(...)) for explicit agent registration.", false)]
    public string? AgentDescription { get; set; }

    /// <summary>
    /// Optional domain-specific hints for the agent.
    /// You do NOT need to describe your components here — active components, actions, state, and
    /// routes are all discovered automatically and included in every prompt.
    /// Use this only for context the agent cannot infer, such as named chart data sources
    /// or a one-line description of the app's purpose.
    /// </summary>
    [Obsolete("Default-agent instructions on AgentBlazorRegistrationOptions are a legacy compatibility path. Prefer ConfigureBuilder(builder => builder.AddAgent(...)) for explicit agent registration.", false)]
    public string? AgentInstructions { get; set; }

    /// <summary>
    /// Reads agent instructions from a file instead of inlining them in Program.cs.
    /// Path is resolved relative to the current directory (the app content root).
    /// </summary>
    [Obsolete("UseInstructionsFile configures the legacy default-agent path. Prefer ConfigureBuilder(builder => builder.AddAgent(...)) and load instructions explicitly.", false)]
    public void UseInstructionsFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _legacyDefaultAgentCompatibilityEnabled = true;
        AgentInstructions = File.ReadAllText(path);
    }

    [Obsolete("Legacy default-agent compatibility should only be used for migration. Prefer ConfigureBuilder(builder => builder.AddAgent(...)) for explicit agent registration.", false)]
    public void UseLegacyDefaultAgentCompatibility()
    {
        _legacyDefaultAgentCompatibilityEnabled = true;
    }

    public void UseOpenAI(string apiKey, string model = "gpt-4o-mini")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _providerRegistration = services => services.AddOpenAIProvider(model, apiKey);
    }

    public void UseOpenAI(string apiKey, string model, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        _providerRegistration = services => services.AddOpenAIProvider(model, apiKey, endpoint);
    }

    public void UseAzureOpenAI(string endpoint, string deploymentName, string? apiKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        _providerRegistration = string.IsNullOrWhiteSpace(apiKey)
            ? services => services.AddAzureOpenAIProvider(endpoint, deploymentName)
            : services => services.AddAzureOpenAIProvider(endpoint, deploymentName, apiKey);
    }

    public void UseOriginAI(
        string endpoint,
        string apiKey,
        string tenantInfo = "origin-ai",
        string chatSource = "agentblazor",
        string? systemPrompt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantInfo);

        _providerRegistration = services => services.AddOriginAIProvider(
            endpoint,
            apiKey,
            tenantInfo,
            chatSource,
            systemPrompt);
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
    /// Registers a custom runtime adapter. This is the preferred long-term integration seam.
    /// If supplied, AgentBlazor UI/hosting surfaces will use this adapter instead of depending
    /// directly on the legacy built-in runtime implementation.
    /// </summary>
    public void UseRuntimeAdapter<TAdapter>()
        where TAdapter : class, IAgentRuntimeAdapter
    {
        _runtimeAdapterRegistration = services => services.AddSingleton<IAgentRuntimeAdapter, TAdapter>();
    }

    /// <summary>
    /// Registers a custom runtime adapter from a factory.
    /// </summary>
    public void UseRuntimeAdapter(Func<IServiceProvider, IAgentRuntimeAdapter> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _runtimeAdapterRegistration = services => services.AddSingleton(factory);
    }

    /// <summary>
    /// Registers the lightweight chat-client-backed runtime adapter. This is the first adapter-first
    /// integration path and avoids depending on the legacy built-in planner/runtime stack.
    /// </summary>
    public void UseChatClientRuntimeAdapter()
    {
        UseRuntimeAdapter<AgentBlazor.Core.Runtime.Adapters.ChatClientRuntimeAdapter>();
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
    [Obsolete("AddAssemblyToScan feeds the legacy default-agent/planner path. Prefer explicit agent registration and capability projection.", false)]
    public void AddAssemblyToScan(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _legacyDefaultAgentCompatibilityEnabled = true;
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
    /// Registers a named service tool the agent can invoke. The handler receives the tool arguments,
    /// the application's <see cref="IServiceProvider"/>, and a cancellation token, and must return
    /// a string result that is surfaced to the agent.
    /// </summary>
    public AgentBlazorRegistrationOptions AddTool(
        string name,
        string description,
        IReadOnlyList<AgentToolParameter> parameters,
        Func<IReadOnlyDictionary<string, object?>, IServiceProvider, CancellationToken, Task<string>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _serviceTools.Add(new AgentServiceTool(name, description, parameters, handler));
        return this;
    }

    /// <summary>Convenience overload for synchronous tool handlers.</summary>
    public AgentBlazorRegistrationOptions AddTool(
        string name,
        string description,
        IReadOnlyList<AgentToolParameter> parameters,
        Func<IReadOnlyDictionary<string, object?>, IServiceProvider, string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddTool(name, description, parameters,
            (args, sp, _) => Task.FromResult(handler(args, sp)));
    }

    /// <summary>
    /// Registers an inline middleware delegate that runs around every agent turn.
    /// Middlewares execute in registration order (first registered = outermost).
    /// </summary>
    public AgentBlazorRegistrationOptions UseMiddleware(
        Func<AgentTurnContext, Func<CancellationToken, Task>, CancellationToken, Task> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// Registers a type-based middleware resolved from DI.
    /// The type must implement <see cref="IAgentTurnMiddleware"/> and be registered in DI.
    /// </summary>
    public AgentBlazorRegistrationOptions UseMiddleware<TMiddleware>()
        where TMiddleware : class, IAgentTurnMiddleware
    {
        _serviceRegistration += services => services.TryAddTransient<TMiddleware>();
        _middlewares.Add((ctx, next, ct) =>
        {
            // Resolved lazily via IServiceProvider captured in the pipeline factory
            // We store a marker so ApplyProvider can inject it properly
            throw new NotSupportedException(
                "Type-based middleware must be resolved via the pipeline factory — use UseMiddleware(delegate) or register via UseMiddleware(Func<...>).");
        });
        return this;
    }

    /// <summary>
    /// Connects an MCP server. Tools exposed by the server are automatically registered and
    /// made available to the agent.
    /// </summary>
    public AgentBlazorRegistrationOptions UseMcpServer(string url, Action<McpServerOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var options = new McpServerOptions();
        configure?.Invoke(options);
        _mcpRegistrations.Add(services =>
        {
            services.AddSingleton<IMcpToolProvider>(
                new HttpMcpToolProvider(url, options.IncludeTools, options.ExcludeTools));
            return services;
        });
        return this;
    }

    internal void ApplyProvider(IServiceCollection services)
    {
        if (_providerRegistration is null && _runtimeAdapterRegistration is null)
        {
            // Emit console warning at startup — easier to spot than buried logs
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  AgentBlazor Warning: No AI provider or runtime adapter configured.          ║");
            Console.WriteLine("║                                                                               ║");
            Console.WriteLine("║  Add one of the following to your AddAgentBlazor() call:                     ║");
            Console.WriteLine("║    options.UseOpenAI(apiKey, \"gpt-4o-mini\");                                  ║");
            Console.WriteLine("║    options.UseAzureOpenAI(endpoint, deploymentName);                         ║");
            Console.WriteLine("║    options.UseOllama(\"llama3.2\");  // Free, runs locally                      ║");
            Console.WriteLine("║    options.UseRuntimeAdapter<TAdapter>();                                    ║");
            Console.WriteLine("║                                                                               ║");
            Console.WriteLine("║  The chat will show an error until a provider is configured.                 ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        _providerRegistration?.Invoke(services);
        _runtimeAdapterRegistration?.Invoke(services);
        _serviceRegistration?.Invoke(services);

        // Register accumulated service tools
        if (_serviceTools.Count > 0)
        {
            var registry = new InMemoryAgentServiceToolRegistry();
            foreach (var tool in _serviceTools)
                registry.Register(tool);
            services.Replace(ServiceDescriptor.Singleton<IAgentServiceToolRegistry>(registry));
        }

        // Register MCP providers
        foreach (var reg in _mcpRegistrations)
            reg(services);

        // Register middleware pipeline
        if (_middlewares.Count > 0)
        {
            var captured = _middlewares.ToList();
            services.Replace(ServiceDescriptor.Singleton(new AgentMiddlewarePipeline(captured)));
        }
    }

    private AgentBlazorTier? _licensedTier;

    /// <summary>
    /// Enables in-app developer tooling (inspector panel) without requiring a paid license.
    /// </summary>
    /// <param name="autoShow">Whether to auto-open the inspector panel.</param>
    public AgentBlazorRegistrationOptions UseDevTools(bool autoShow = false)
    {
        _serviceRegistration += services =>
        {
            services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
                AgentBlazor.Core.Paid.IAgentInspectorStore,
                AgentBlazor.Core.Paid.InMemoryAgentInspectorStore>());
        };

        _optionsConfiguration += options =>
        {
            options.EnableDevTools = true;
            options.AutoShowDevTools = autoShow;
        };

        return this;
    }

    /// <summary>
    /// Activates paid or enterprise features using a license key.
    /// Keys must start with "AB-PRO-" (Paid tier) or "AB-ENT-" (Premium tier)
    /// and be at least 24 characters long.
    /// </summary>
    public AgentBlazorRegistrationOptions UseProLicense(string licenseKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseKey);

        if (!licenseKey.StartsWith("AB-PRO-", StringComparison.Ordinal) &&
            !licenseKey.StartsWith("AB-ENT-", StringComparison.Ordinal))
            throw new ArgumentException("Invalid AgentBlazor license key format. Key must start with 'AB-PRO-' or 'AB-ENT-'.", nameof(licenseKey));

        if (licenseKey.Length < 24)
            throw new ArgumentException("License key too short.", nameof(licenseKey));

        _licensedTier = licenseKey.StartsWith("AB-ENT-", StringComparison.Ordinal)
            ? AgentBlazor.Licensing.AgentBlazorTier.Premium
            : AgentBlazor.Licensing.AgentBlazorTier.Paid;

        // Override free-tier no-op services with paid implementations
        _serviceRegistration += services =>
        {
            services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
                AgentBlazor.Core.Paid.IActionHistoryStore,
                AgentBlazor.Core.Paid.InMemoryActionHistoryStore>());
            services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
                AgentBlazor.Core.Paid.IAdaptiveSuggestionService,
                AgentBlazor.Core.Paid.LlmAdaptiveSuggestionService>());
            services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
                AgentBlazor.Core.Paid.IProactiveInsightService,
                AgentBlazor.Core.Paid.LlmProactiveInsightService>());
            services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<
                AgentBlazor.Core.Paid.IAgentInspectorStore,
                AgentBlazor.Core.Paid.InMemoryAgentInspectorStore>());
        };

        return this;
    }

    internal void ApplyOptions(AgentBlazorOptions options)
    {
#pragma warning disable CS0618 // Legacy default-agent surface remains wired here for compatibility.
        var useLegacyDefaultAgentCompatibility =
            _legacyDefaultAgentCompatibilityEnabled ||
            !string.IsNullOrWhiteSpace(AgentName) ||
            !string.IsNullOrWhiteSpace(AgentDescription) ||
            !string.IsNullOrWhiteSpace(AgentInstructions) ||
            _agentPageAssemblies.Count > 0;
        if (useLegacyDefaultAgentCompatibility)
        {
            options.DefaultAgent.Enabled = true;
            options.DefaultAgent.PreferAsImplicitFallback = true;

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
        }

        if (_licensedTier.HasValue)
        {
            options.LicensedTier = _licensedTier.Value;
        }
#pragma warning restore CS0618

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
