using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Agents;
using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Execution;
using AgentBlazor.Options;
using AgentBlazor.ProviderAdapters;
using AgentBlazor.Services;
using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace AgentBlazor.IntegrationTests;

public class ProviderAdapterIntegrationTests
{
    private const string DefaultOpenAiModel = "gpt-4o-mini";

    [Fact]
    public void AddAgentBlazor_RegistersRuntimeAndHosting_WithoutImplicitDefaultAgent()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var registry = provider.GetRequiredService<IAgentRegistry>();

        Assert.Empty(options.AssembliesToScan);
        Assert.NotNull(runtimeAdapter);
        Assert.False(registry.TryGet("AgentBlazor UI Agent", out _));
    }

    [Fact]
    public void AddAgentBlazor_WithExplicitAgentRegistration_RegistersBuiltInUiAgent()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(services, options =>
        {
            options.ConfigureBuilder(builder => builder.AddAgent("AgentBlazor UI Agent"));
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentRegistry>();

        Assert.True(registry.TryGet("AgentBlazor UI Agent", out _));
    }

    [Fact]
    public void AddAgentBlazor_WithUseOpenAI_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(
            services,
            options => options.UseOpenAI("demo-api-key", DefaultOpenAiModel));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal(DefaultOpenAiModel, options.Provider.Model);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AddAgentBlazor_WithUseOpenAIAndMissingApiKey_ThrowsConfigurationException()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<AgentBlazorConfigurationException>(() =>
            AgentBlazorServiceExtensions.AddAgentBlazor(
                services,
                options => options.UseOpenAI("", DefaultOpenAiModel)));

        Assert.Contains("OpenAI:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OPENAI_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAiProvider_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        services.AddOpenAIProvider(DefaultOpenAiModel, "demo-api-key");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal(DefaultOpenAiModel, options.Provider.Model);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void OpenAiProvider_WithCustomEndpoint_RegistersNormalizedEndpoint()
    {
        var services = new ServiceCollection();

        services.AddOpenAIProvider(DefaultOpenAiModel, "demo-api-key", "https://api.openai.com/v1/");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.OpenAI, options.Provider.Kind);
        Assert.Equal("https://api.openai.com/v1", options.Provider.Endpoint);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AddAgentBlazor_WithUseAzureOpenAIApiKey_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(
            services,
            options => options.UseAzureOpenAI(
                "https://example.openai.azure.com/",
                "agentblazor-chat",
                "demo-api-key"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.AzureOpenAI, options.Provider.Kind);
        Assert.Equal("https://example.openai.azure.com", options.Provider.Endpoint);
        Assert.Equal("agentblazor-chat", options.Provider.DeploymentName);
        Assert.Equal("demo-api-key", options.Provider.ApiKey);
        Assert.Equal("ApiKey", options.Provider.AdditionalSettings["Auth"]);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AddAgentBlazor_WithUseAzureOpenAITokenCredential_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        AgentBlazorServiceExtensions.AddAgentBlazor(
            services,
            options => options.UseAzureOpenAI(
                "https://example.openai.azure.com/",
                "agentblazor-chat",
                new StaticTokenCredential()));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.AzureOpenAI, options.Provider.Kind);
        Assert.Equal("https://example.openai.azure.com", options.Provider.Endpoint);
        Assert.Equal("agentblazor-chat", options.Provider.DeploymentName);
        Assert.Null(options.Provider.ApiKey);
        Assert.Equal("TokenCredential", options.Provider.AdditionalSettings["Auth"]);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AzureOpenAiProvider_WithApiKey_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        services.AddAzureOpenAIProvider(
            "https://example.openai.azure.com/",
            "agentblazor-chat",
            "demo-api-key");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.AzureOpenAI, options.Provider.Kind);
        Assert.Equal("https://example.openai.azure.com", options.Provider.Endpoint);
        Assert.Equal("agentblazor-chat", options.Provider.DeploymentName);
        Assert.Equal("demo-api-key", options.Provider.ApiKey);
        Assert.Equal("ApiKey", options.Provider.AdditionalSettings["Auth"]);
        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AzureOpenAiProvider_WithTokenCredential_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        services.AddAzureOpenAIProvider(
            "https://example.openai.azure.com/",
            "agentblazor-chat",
            new StaticTokenCredential());
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.AzureOpenAI, options.Provider.Kind);
        Assert.Equal("https://example.openai.azure.com", options.Provider.Endpoint);
        Assert.Equal("agentblazor-chat", options.Provider.DeploymentName);
        Assert.Null(options.Provider.ApiKey);
        Assert.Equal("TokenCredential", options.Provider.AdditionalSettings["Auth"]);
        Assert.NotNull(chatClient);
    }

    [Theory]
    [InlineData(null, "demo-api-key")]
    [InlineData("", "demo-api-key")]
    [InlineData("   ", "demo-api-key")]
    [InlineData(DefaultOpenAiModel, null)]
    [InlineData(DefaultOpenAiModel, "")]
    [InlineData(DefaultOpenAiModel, "   ")]
    public void OpenAiProvider_ThrowsForBlankModelOrApiKey(string? model, string? apiKey)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddOpenAIProvider(model!, apiKey!);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("/v1")]
    public void OpenAiProvider_WithCustomEndpoint_ThrowsForInvalidEndpoint(string? endpoint)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddOpenAIProvider(DefaultOpenAiModel, "demo-api-key", endpoint!);
        });
    }

    [Theory]
    [InlineData(null, "agentblazor-chat", "demo-api-key")]
    [InlineData("", "agentblazor-chat", "demo-api-key")]
    [InlineData("   ", "agentblazor-chat", "demo-api-key")]
    [InlineData("not-a-uri", "agentblazor-chat", "demo-api-key")]
    [InlineData("/openai", "agentblazor-chat", "demo-api-key")]
    [InlineData("https://example.openai.azure.com", null, "demo-api-key")]
    [InlineData("https://example.openai.azure.com", "", "demo-api-key")]
    [InlineData("https://example.openai.azure.com", "   ", "demo-api-key")]
    public void AzureOpenAiProvider_WithApiKey_ThrowsForInvalidRequiredSettings(
        string? endpoint,
        string? deploymentName,
        string? apiKey)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            services.AddAzureOpenAIProvider(endpoint!, deploymentName!, apiKey);
        });
    }

    [Fact]
    public void AzureOpenAiProvider_WithTokenCredential_ThrowsForNullCredential()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddAzureOpenAIProvider(
                "https://example.openai.azure.com",
                "agentblazor-chat",
                credential: null!);
        });
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
    public void OriginAiProvider_RegistersFrameworkChatClient()
    {
        var services = new ServiceCollection();

        services.AddOriginAIProvider(
            "https://origin-ai.azurewebsites.net/",
            "demo-api-key",
            tenantInfo: "origin-ai");
        services.AddAgentBlazorServices();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;
        var chatClient = provider.GetService<IChatClient>();

        Assert.Equal(AgentProviderKind.Custom, options.Provider.Kind);
        Assert.Equal("https://origin-ai.azurewebsites.net", options.Provider.Endpoint);
        Assert.Equal("demo-api-key", options.Provider.ApiKey);
        Assert.Equal("origin-ai", options.Provider.AdditionalSettings["TenantInfo"]);
        Assert.Equal("OriginAI", options.Provider.AdditionalSettings["Provider"]);
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
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest("Reply with READY only."),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));
        Assert.DoesNotContain("No provider is configured", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnAsync_WorksWhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("live-openai-chat-agent");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Reply with READY only.",
                AgentName: "live-openai-chat-agent",
                SessionId: "live-openai-chat"),
            cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.ResponseText));
        Assert.DoesNotContain("No provider is configured", response.ResponseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No agents are registered", response.ResponseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnAsync_ExecutesSemanticWorkflow_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LiveOpenAiProbeCapabilities>("live-openai-probe");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var response = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Run the live OpenAI probe workflow and return the workflow outcome.",
                AgentName: "live-openai-probe",
                SessionId: "live-openai-probe"),
            cts.Token);

        Assert.False(response.RequiresApproval);
        var step = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Completed, step.Status);
        Assert.Equal("live_openai_probe", step.TargetId);
        Assert.Equal("run_probe", step.ActionId);
        Assert.NotNull(step.Outputs);
        Assert.Equal("LIVE_OPENAI_OK", step.Outputs!["probe"]);
    }

    [Fact]
    public async Task OpenAiProvider_ConnectRunStreamAsync_ReplaysBufferedStreamingRun_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("live-openai-chat-agent");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.True(runtimeAdapter.SupportsReconnect);

        const string runId = "live-openai-reconnect-run";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var firstTextSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new AgentTurnRequest(
            "Reply with the word RECONNECT exactly 40 times separated by spaces.",
            AgentName: "live-openai-chat-agent",
            SessionId: "live-openai-reconnect",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.RunId] = runId
            });

        var originalTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(request, cts.Token))
            {
                events.Add(streamEvent);
                if (streamEvent.Kind == AgentTurnStreamEventKind.TextMessageContent)
                {
                    firstTextSeen.TrySetResult(true);
                }
            }

            firstTextSeen.TrySetResult(true);
            return events;
        }, cts.Token);

        await firstTextSeen.Task.WaitAsync(TimeSpan.FromSeconds(90), cts.Token);

        var replayed = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.ConnectRunStreamAsync(runId, cts.Token))
        {
            replayed.Add(streamEvent);
        }

        var original = await originalTask;
        Assert.NotEmpty(original);
        Assert.NotEmpty(replayed);
        Assert.Contains(replayed, static e => e.IsReplay);
        Assert.Contains(replayed, static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent);

        var originalText = string.Concat(original
            .Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent)
            .Select(static e => e.TextDelta));
        var replayedText = string.Concat(replayed
            .Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent)
            .Select(static e => e.TextDelta));

        Assert.False(string.IsNullOrWhiteSpace(originalText));
        Assert.Equal(originalText, replayedText);
    }

    [Fact]
    public async Task OpenAiProvider_ConnectRunStreamAsync_AllowsMultipleSubscribers_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddAgent("live-openai-chat-agent");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.True(runtimeAdapter.SupportsReconnect);

        const string runId = "live-openai-multi-reconnect-run";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var firstTextSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new AgentTurnRequest(
            "Reply with the word MULTIREPLAY exactly 60 times separated by spaces.",
            AgentName: "live-openai-chat-agent",
            SessionId: "live-openai-multi-reconnect",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.RunId] = runId
            });

        var originalTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(request, cts.Token))
            {
                events.Add(streamEvent);
                if (streamEvent.Kind == AgentTurnStreamEventKind.TextMessageContent)
                {
                    firstTextSeen.TrySetResult(true);
                }
            }

            firstTextSeen.TrySetResult(true);
            return events;
        }, cts.Token);

        await firstTextSeen.Task.WaitAsync(TimeSpan.FromSeconds(90), cts.Token);

        var reconnectOneTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in runtimeAdapter.ConnectRunStreamAsync(runId, cts.Token))
            {
                events.Add(streamEvent);
            }

            return events;
        }, cts.Token);

        var reconnectTwoTask = Task.Run(async () =>
        {
            var events = new List<AgentTurnStreamEvent>();
            await foreach (var streamEvent in runtimeAdapter.ConnectRunStreamAsync(runId, cts.Token))
            {
                events.Add(streamEvent);
            }

            return events;
        }, cts.Token);

        var original = await originalTask;
        var reconnectOne = await reconnectOneTask;
        var reconnectTwo = await reconnectTwoTask;

        Assert.NotEmpty(original);
        Assert.NotEmpty(reconnectOne);
        Assert.NotEmpty(reconnectTwo);
        Assert.Contains(reconnectOne, static e => e.IsReplay);
        Assert.Contains(reconnectTwo, static e => e.IsReplay);

        var originalText = string.Concat(original
            .Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent)
            .Select(static e => e.TextDelta));
        var reconnectOneText = string.Concat(reconnectOne
            .Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent)
            .Select(static e => e.TextDelta));
        var reconnectTwoText = string.Concat(reconnectTwo
            .Where(static e => e.Kind == AgentTurnStreamEventKind.TextMessageContent)
            .Select(static e => e.TextDelta));

        Assert.False(string.IsNullOrWhiteSpace(originalText));
        Assert.Equal(originalText, reconnectOneText);
        Assert.Equal(originalText, reconnectTwoText);
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnAsync_ApprovalWorkflow_RequiresApprovalThenExecutes_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LiveOpenAiApprovalCapabilities>("live-openai-approval");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var approvalResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Submit the live OpenAI approval probe workflow.",
                AgentName: "live-openai-approval",
                SessionId: "live-openai-approval"),
            cts.Token);

        Assert.True(approvalResponse.RequiresApproval);
        var pendingStep = Assert.Single(approvalResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.ApprovalRequired, pendingStep.Status);
        Assert.Equal("live_openai_approval", pendingStep.TargetId);
        Assert.Equal("submit_probe", pendingStep.ActionId);

        var approvedResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Submit the live OpenAI approval probe workflow.",
                AgentName: "live-openai-approval",
                SessionId: "live-openai-approval",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["agentblazor.approvals"] = "live_openai_approval.submit_probe"
                }),
            cts.Token);

        Assert.False(approvedResponse.RequiresApproval);
        var completedStep = Assert.Single(approvedResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Completed, completedStep.Status);
        Assert.Equal("live_openai_approval", completedStep.TargetId);
        Assert.Equal("submit_probe", completedStep.ActionId);
        Assert.NotNull(completedStep.Outputs);
        Assert.Equal("LIVE_OPENAI_APPROVED", completedStep.Outputs!["approval"]);
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnAsync_BlockedRecoveryWorkflow_RetriesAfterRecovery_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddSingleton<LiveOpenAiRecoveryProbeState>();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LiveOpenAiRecoveryCapabilities>("live-openai-recovery");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var approvalResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Call the submit live OpenAI recovery probe workflow tool now.",
                AgentName: "live-openai-recovery",
                SessionId: "live-openai-recovery"),
            cts.Token);

        Assert.True(approvalResponse.RequiresApproval);
        var approvalStep = Assert.Single(approvalResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.ApprovalRequired, approvalStep.Status);
        Assert.Equal("live_openai_recovery", approvalStep.TargetId);
        Assert.Equal("submit_probe", approvalStep.ActionId);

        var blockedResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Call the submit live OpenAI recovery probe workflow tool now.",
                AgentName: "live-openai-recovery",
                SessionId: "live-openai-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["agentblazor.approvals"] = "live_openai_recovery.submit_probe"
                }),
            cts.Token);

        Assert.False(blockedResponse.RequiresApproval);
        var blockedStep = Assert.Single(blockedResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Blocked, blockedStep.Status);
        Assert.Equal("live_openai_recovery", blockedStep.TargetId);
        Assert.Equal("submit_probe", blockedStep.ActionId);
        Assert.Contains("blocked", blockedStep.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var recoveryResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Call the apply live OpenAI recovery playbook workflow tool now.",
                AgentName: "live-openai-recovery",
                SessionId: "live-openai-recovery"),
            cts.Token);

        Assert.False(recoveryResponse.RequiresApproval);
        var recoveryStep = Assert.Single(recoveryResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Completed, recoveryStep.Status);
        Assert.Equal("live_openai_recovery", recoveryStep.TargetId);
        Assert.Equal("apply_recovery_playbook", recoveryStep.ActionId);
        Assert.Equal("LIVE_OPENAI_RECOVERY_APPLIED", recoveryStep.Outputs!["recovery"]);

        var retriedResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Call the submit live OpenAI recovery probe workflow tool now.",
                AgentName: "live-openai-recovery",
                SessionId: "live-openai-recovery",
                Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["agentblazor.approvals"] = "live_openai_recovery.submit_probe"
                }),
            cts.Token);

        Assert.False(retriedResponse.RequiresApproval);
        var retriedStep = Assert.Single(retriedResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Completed, retriedStep.Status);
        Assert.Equal("live_openai_recovery", retriedStep.TargetId);
        Assert.Equal("submit_probe", retriedStep.ActionId);
        Assert.Equal("LIVE_OPENAI_RECOVERY_READY", retriedStep.Outputs!["status"]);
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnAsync_ConcurrentWorkflowRuns_ForDifferentSessions_CanOverlap_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var probeState = new LiveOpenAiConcurrentProbeState();
        var services = new ServiceCollection();
        services.AddSingleton(probeState);
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LiveOpenAiConcurrentProbeCapabilities>("live-openai-concurrency");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var firstTask = Task.Run(() => runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Run the live OpenAI concurrency probe workflow and return the workflow outcome.",
                AgentName: "live-openai-concurrency",
                SessionId: "live-openai-concurrency-a"),
            cts.Token), cts.Token);

        var secondTask = Task.Run(() => runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Run the live OpenAI concurrency probe workflow and return the workflow outcome.",
                AgentName: "live-openai-concurrency",
                SessionId: "live-openai-concurrency-b"),
            cts.Token), cts.Token);

        await probeState.TwoCallsObserved.Task.WaitAsync(TimeSpan.FromSeconds(90), cts.Token);

        var responses = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(2, responses.Length);
        Assert.True(probeState.MaxConcurrentCalls >= 2, $"Expected overlapping workflow runs but max concurrency was {probeState.MaxConcurrentCalls}.");

        var outputs = responses
            .Select(static response => Assert.Single(response.ExecutionPlan!.Steps).Outputs!)
            .ToArray();
        Assert.Contains(outputs, static output => string.Equals(output["sessionId"]?.ToString(), "live-openai-concurrency-a", StringComparison.Ordinal));
        Assert.Contains(outputs, static output => string.Equals(output["sessionId"]?.ToString(), "live-openai-concurrency-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnAsync_PreservesDeterministicSessionStateAcrossTurns_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddSingleton<LiveOpenAiSessionProbeState>();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LiveOpenAiSessionProbeCapabilities>("live-openai-session-probe");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        const string sessionId = "live-openai-session-state";
        var firstResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Record the live OpenAI session probe workflow and return the workflow outcome.",
                AgentName: "live-openai-session-probe",
                SessionId: sessionId),
            cts.Token);

        var firstStep = Assert.Single(firstResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Completed, firstStep.Status);
        Assert.Equal("live_openai_session_probe", firstStep.TargetId);
        Assert.Equal("record_probe", firstStep.ActionId);
        Assert.Equal(sessionId, firstStep.Outputs!["sessionId"]);
        Assert.Equal("1", firstStep.Outputs["turn"]);
        Assert.False(string.IsNullOrWhiteSpace(firstResponse.ExecutionPlan.Context.RunId));

        var secondResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Record the live OpenAI session probe workflow again and return the workflow outcome.",
                AgentName: "live-openai-session-probe",
                SessionId: sessionId),
            cts.Token);

        var secondStep = Assert.Single(secondResponse.ExecutionPlan!.Steps);
        Assert.Equal(AgentExecutionStepStatus.Completed, secondStep.Status);
        Assert.Equal(sessionId, secondStep.Outputs!["sessionId"]);
        Assert.Equal("2", secondStep.Outputs["turn"]);
        Assert.NotEqual(firstResponse.ExecutionPlan.Context.RunId, secondResponse.ExecutionPlan.Context.RunId);

        var differentSessionResponse = await runtimeAdapter.RunTurnAsync(
            new AgentTurnRequest(
                "Record the live OpenAI session probe workflow for a fresh session.",
                AgentName: "live-openai-session-probe",
                SessionId: "live-openai-session-state-b"),
            cts.Token);

        var differentSessionStep = Assert.Single(differentSessionResponse.ExecutionPlan!.Steps);
        Assert.Equal("live-openai-session-state-b", differentSessionStep.Outputs!["sessionId"]);
        Assert.Equal("1", differentSessionStep.Outputs["turn"]);
    }

    [Fact]
    public async Task OpenAiProvider_RunTurnStreamingAsync_ReplaysSequentialWorkflowRunsAcrossSameSession_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddSingleton<LiveOpenAiSessionProbeState>();
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<LiveOpenAiSessionProbeCapabilities>("live-openai-session-probe");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.True(runtimeAdapter.SupportsReconnect);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        const string sessionId = "live-openai-session-stream";

        var firstOriginal = await CollectRunStreamAsync(
            runtimeAdapter,
            "live-openai-session-probe",
            sessionId,
            "live-openai-session-stream-run-1",
            "Record the live OpenAI session probe workflow and return the workflow outcome.",
            cts.Token);
        var firstReplay = await CollectReplayStreamAsync(runtimeAdapter, "live-openai-session-stream-run-1", cts.Token);
        var secondOriginal = await CollectRunStreamAsync(
            runtimeAdapter,
            "live-openai-session-probe",
            sessionId,
            "live-openai-session-stream-run-2",
            "Record the live OpenAI session probe workflow again and return the workflow outcome.",
            cts.Token);
        var secondReplay = await CollectReplayStreamAsync(runtimeAdapter, "live-openai-session-stream-run-2", cts.Token);

        Assert.Equal("1", GetFinishedStepOutput(firstOriginal, "turn"));
        Assert.Equal("1", GetFinishedStepOutput(firstReplay, "turn"));
        Assert.Equal(sessionId, GetFinishedStepOutput(firstReplay, "sessionId"));
        Assert.Contains(firstReplay, static e => e.IsReplay);

        Assert.Equal("2", GetFinishedStepOutput(secondOriginal, "turn"));
        Assert.Equal("2", GetFinishedStepOutput(secondReplay, "turn"));
        Assert.Equal(sessionId, GetFinishedStepOutput(secondReplay, "sessionId"));
        Assert.Contains(secondReplay, static e => e.IsReplay);
    }

    [Fact]
    public async Task ChatClientRuntimeAdapter_StopRunAsync_CancelsSlowStreamingWorkflow()
    {
        var probeState = new SlowCancellationProbeState();
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new FirstToolInvokingStreamingChatClient());
        services.AddSingleton(probeState);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<SlowCancellationProbeCapabilities>("slow-cancel-probe");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.True(runtimeAdapter.SupportsCancellation);

        const string runId = "slow-cancel-probe-run";
        var request = new AgentTurnRequest(
            "Run the slow cancellation probe workflow.",
            AgentName: "slow-cancel-probe",
            SessionId: "slow-cancel-probe-session",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.RunId] = runId
            });

        var streamTask = Task.Run(async () =>
        {
            await foreach (var _ in runtimeAdapter.RunTurnStreamingAsync(request))
            {
            }
        });

        await probeState.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await runtimeAdapter.StopRunAsync(runId));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await streamTask);
        await probeState.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(await runtimeAdapter.StopRunAsync(runId));
    }

    [Fact]
    public async Task OpenAiProvider_StopRunAsync_CancelsSlowStreamingWorkflow_WhenApiKeyAvailable()
    {
        var openAiApiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            return;
        }

        var probeState = new SlowCancellationProbeState();
        var openAiModel = ResolveOpenAiModel();
        var services = new ServiceCollection();
        services.AddSingleton(probeState);
        services.AddOpenAIProvider(openAiModel, openAiApiKey);
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddWorkflow<SlowCancellationProbeCapabilities>("live-openai-cancel");

        using var provider = services.BuildServiceProvider();
        var runtimeAdapter = provider.GetRequiredService<IAgentRuntimeAdapter>();
        Assert.True(runtimeAdapter.SupportsCancellation);

        const string runId = "live-openai-cancel-run";
        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var request = new AgentTurnRequest(
            "Run the slow cancellation probe workflow.",
            AgentName: "live-openai-cancel",
            SessionId: "live-openai-cancel-session",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentRuntimeContextKeys.RunId] = runId
            });

        var streamTask = Task.Run(async () =>
        {
            await foreach (var _ in runtimeAdapter.RunTurnStreamingAsync(request, overallCts.Token))
            {
            }
        }, overallCts.Token);

        await probeState.Started.Task.WaitAsync(TimeSpan.FromSeconds(90), overallCts.Token);

        Assert.True(await runtimeAdapter.StopRunAsync(runId, overallCts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await streamTask);
        await probeState.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(30), overallCts.Token);
        Assert.False(await runtimeAdapter.StopRunAsync(runId, overallCts.Token));
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

    private static string? ResolveOpenAiApiKey()
    {
        var environmentKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("OpenAI__ApiKey");
        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            return environmentKey;
        }

        var fullPath = ResolveRepoRelativePath("demo", "AgentBlazor.Demo", "appsettings.Development.json");
        if (!File.Exists(fullPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("OpenAI", out var openAiSection) ||
            !openAiSection.TryGetProperty("ApiKey", out var apiKeyProperty))
        {
            return null;
        }

        return apiKeyProperty.GetString();
    }

    private static string ResolveOpenAiModel()
    {
        var environmentModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? Environment.GetEnvironmentVariable("OpenAI__Model");
        if (!string.IsNullOrWhiteSpace(environmentModel))
        {
            return environmentModel;
        }

        var fullPath = ResolveRepoRelativePath("demo", "AgentBlazor.Demo", "appsettings.Development.json");
        if (!File.Exists(fullPath))
        {
            return DefaultOpenAiModel;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("OpenAI", out var openAiSection) ||
            !openAiSection.TryGetProperty("Model", out var modelProperty))
        {
            return DefaultOpenAiModel;
        }

        return string.IsNullOrWhiteSpace(modelProperty.GetString())
            ? DefaultOpenAiModel
            : modelProperty.GetString()!;
    }

    private static string ResolveRepoRelativePath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AgentBlazor.sln")))
            {
                return Path.Combine([current.FullName, .. segments]);
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Path.Combine(segments)));
    }

    private static async Task<List<AgentTurnStreamEvent>> CollectRunStreamAsync(
        IAgentRuntimeAdapter runtimeAdapter,
        string agentName,
        string sessionId,
        string runId,
        string prompt,
        CancellationToken cancellationToken)
    {
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.RunTurnStreamingAsync(
                           new AgentTurnRequest(
                               prompt,
                               AgentName: agentName,
                               SessionId: sessionId,
                               Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                               {
                                   [AgentRuntimeContextKeys.RunId] = runId
                               }),
                           cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static async Task<List<AgentTurnStreamEvent>> CollectReplayStreamAsync(
        IAgentRuntimeAdapter runtimeAdapter,
        string runId,
        CancellationToken cancellationToken)
    {
        var events = new List<AgentTurnStreamEvent>();
        await foreach (var streamEvent in runtimeAdapter.ConnectRunStreamAsync(runId, cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static string GetFinishedStepOutput(
        IReadOnlyList<AgentTurnStreamEvent> events,
        string outputKey)
    {
        var response = events
            .LastOrDefault(static e => e.Kind == AgentTurnStreamEventKind.RunFinished)
            ?.Response;
        Assert.NotNull(response);
        var step = Assert.Single(response.ExecutionPlan!.Steps);
        Assert.NotNull(step.Outputs);
        Assert.True(step.Outputs!.TryGetValue(outputKey, out var outputValue));
        return Assert.IsType<string>(outputValue);
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    [AgentCapability("live_openai_probe", Name = "Live OpenAI Probe")]
    private sealed class LiveOpenAiProbeCapabilities
    {
        [AgentAction("Run the live OpenAI probe", ActionId = "run_probe")]
        public CapabilityResult RunProbe()
            => CapabilityResult.Success("Live OpenAI probe executed.")
                .WithOutput("probe", "LIVE_OPENAI_OK");
    }

    [AgentCapability("live_openai_approval", Name = "Live OpenAI Approval")]
    private sealed class LiveOpenAiApprovalCapabilities
    {
        [AgentAction("Submit the live OpenAI approval probe", ActionId = "submit_probe", RequiresApproval = true)]
        public CapabilityResult SubmitProbe()
            => CapabilityResult.Success("Live OpenAI approval probe executed.")
                .WithOutput("approval", "LIVE_OPENAI_APPROVED");
    }

    [AgentCapability("live_openai_recovery", Name = "Live OpenAI Recovery")]
    private sealed class LiveOpenAiRecoveryCapabilities(LiveOpenAiRecoveryProbeState probeState)
    {
        [AgentAction("Submit the live OpenAI recovery probe", ActionId = "submit_probe", RequiresApproval = true)]
        public CapabilityResult SubmitProbe()
        {
            if (probeState.Blocked)
            {
                return CapabilityResult.Blocked("Live OpenAI recovery probe is blocked until the recovery playbook runs.")
                    .WithWarning("Recovery playbook has not been applied.")
                    .WithNextActions("Apply the live OpenAI recovery playbook");
            }

            return CapabilityResult.Success("Live OpenAI recovery probe submitted after recovery.")
                .WithOutput("status", "LIVE_OPENAI_RECOVERY_READY");
        }

        [AgentAction("Apply the live OpenAI recovery playbook", ActionId = "apply_recovery_playbook")]
        public CapabilityResult ApplyRecoveryPlaybook()
        {
            probeState.Blocked = false;
            return CapabilityResult.Success("Live OpenAI recovery playbook applied.")
                .WithOutput("recovery", "LIVE_OPENAI_RECOVERY_APPLIED");
        }
    }

    [AgentCapability("live_openai_session_probe", Name = "Live OpenAI Session Probe")]
    private sealed class LiveOpenAiSessionProbeCapabilities(LiveOpenAiSessionProbeState probeState)
    {
        [AgentAction("Record the live OpenAI session probe", ActionId = "record_probe")]
        public CapabilityResult RecordProbe(
            [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId, Required = true)] string sessionId,
            [AgentParam(ContextKey = AgentRuntimeContextKeys.RunId, Required = true)] string runId)
        {
            var turn = probeState.Record(sessionId);
            return CapabilityResult.Success(
                    $"Live OpenAI session probe turn {turn.ToString(CultureInfo.InvariantCulture)} recorded.")
                .WithOutput("turn", turn.ToString(CultureInfo.InvariantCulture))
                .WithOutput("sessionId", sessionId)
                .WithOutput("runId", runId);
        }
    }

    [AgentCapability("slow_cancellation_probe", Name = "Slow Cancellation Probe")]
    private sealed class SlowCancellationProbeCapabilities(SlowCancellationProbeState probeState)
    {
        [AgentAction("Run the slow cancellation probe", ActionId = "run_probe")]
        public async Task<CapabilityResult> RunProbeAsync(CancellationToken cancellationToken)
        {
            probeState.Started.TrySetResult(true);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return CapabilityResult.Success("Slow cancellation probe completed unexpectedly.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                probeState.Canceled.TrySetResult(true);
                throw;
            }
        }
    }

    private sealed class SlowCancellationProbeState
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class LiveOpenAiSessionProbeState
    {
        private readonly ConcurrentDictionary<string, int> _turnCounts = new(StringComparer.OrdinalIgnoreCase);

        public int Record(string sessionId)
            => _turnCounts.AddOrUpdate(sessionId, 1, static (_, current) => current + 1);
    }

    private sealed class LiveOpenAiRecoveryProbeState
    {
        public bool Blocked { get; set; } = true;
    }

    private sealed class LiveOpenAiConcurrentProbeState
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;

        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);

        public TaskCompletionSource<bool> TwoCallsObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> EnterAsync(CancellationToken cancellationToken)
        {
            var currentActive = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrent(currentActive);
            if (currentActive >= 2)
            {
                TwoCallsObserved.TrySetResult(true);
            }

            try
            {
                await TwoCallsObserved.Task.WaitAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                return currentActive;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaxConcurrent(int currentActive)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxConcurrentCalls);
                if (currentActive <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, currentActive, snapshot) == snapshot)
                {
                    return;
                }
            }
        }
    }

    [AgentCapability("live_openai_concurrency_probe", Name = "Live OpenAI Concurrency Probe")]
    private sealed class LiveOpenAiConcurrentProbeCapabilities(LiveOpenAiConcurrentProbeState probeState)
    {
        [AgentAction("Run the live OpenAI concurrency probe", ActionId = "run_probe")]
        public async Task<CapabilityResult> RunProbeAsync(
            [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId, Required = true)] string sessionId,
            [AgentParam(ContextKey = AgentRuntimeContextKeys.RunId, Required = true)] string runId,
            CancellationToken cancellationToken)
        {
            var active = await probeState.EnterAsync(cancellationToken);
            return CapabilityResult.Success("Live OpenAI concurrency probe executed.")
                .WithOutput("sessionId", sessionId)
                .WithOutput("runId", runId)
                .WithOutput("activeAtEntry", active.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class FirstToolInvokingStreamingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = options?.Tools?.OfType<AIFunction>().FirstOrDefault()
                ?? throw new InvalidOperationException("No tools were available for the streaming chat client test.");

            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Slow probe completed."));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = options?.Tools?.OfType<AIFunction>().FirstOrDefault()
                ?? throw new InvalidOperationException("No tools were available for the streaming chat client test.");

            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Slow probe completed.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
