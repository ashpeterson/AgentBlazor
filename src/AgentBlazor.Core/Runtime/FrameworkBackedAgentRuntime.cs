using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Runtime;

internal sealed class FrameworkBackedAgentRuntime(
    IServiceProvider services,
    IAgentRegistry agentRegistry,
    IComponentCapabilityCatalog componentCatalog,
    IAgentComponentRegistry? componentRegistry,
    IOptions<AgentBlazorOptions> options,
    IComponentActionExecutor executor,
    IAgentBlazorTelemetrySink telemetrySink,
    ILogger<FrameworkBackedAgentRuntime>? logger = null,
    IChatClient? chatClient = null,
    IAgentBlazorEntitlementService? entitlementService = null) : IAgentRuntime
{
    private readonly ConcurrentDictionary<string, PendingClarification> _pendingClarifications =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<AgentTurnResponse> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            throw new ArgumentException("User message is required.", nameof(request));
        }

        var hasContext = request.Context is { Count: > 0 };
        var providerConfigured = chatClient is not null;
        var tierName = entitlementService?.CurrentTier.ToString();

        var registration = ResolveAgent(request.AgentName);
        if (registration is null)
        {
            await TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
            {
                Kind = AgentBlazorRunEventKind.Started,
                Source = AgentBlazorTelemetrySources.Runtime,
                AgentName = "none",
                RequestedAgentName = request.AgentName,
                ProviderConfigured = providerConfigured,
                HasContext = hasContext,
                HasRegisteredComponents = false
            });

            await TrackRunEventAsync(new AgentBlazorRunTelemetryEvent
            {
                Kind = AgentBlazorRunEventKind.Finished,
                Source = AgentBlazorTelemetrySources.Runtime,
                AgentName = "none",
                RequestedAgentName = request.AgentName,
                Outcome = AgentBlazorRunOutcome.NoAgentRegistered,
                ProviderConfigured = providerConfigured,
                HasContext = hasContext,
                HasRegisteredComponents = false,
                Detail = "No agents are registered."
            });

            return new AgentTurnResponse(
                AgentName: "none",
                ResponseText: "No agents are registered. Register AgentBlazor services to enable the built-in default agent.",
                PlannedActions: [],
                ExecutionResults: []);
        }

        var policyEvaluation = ResolveAllowedComponents(registration);
        if (policyEvaluation.BlockedActionKeys.Count > 0)
        {
            logger?.LogInformation(
                "Agent policy filtered {BlockedActionCount} component actions for {AgentName}: {BlockedActions}",
                policyEvaluation.BlockedActionKeys.Count,
                registration.Name,
                ComponentActionPolicy.SummarizeBlockedActions(policyEvaluation.BlockedActionKeys));
        }

        var entitlementEvaluation = ComponentActionPolicy.EvaluateEntitledCapabilities(
            policyEvaluation.AllowedComponents,
            entitlementService);
        if (entitlementService is not null && entitlementEvaluation.BlockedActionKeys.Count > 0)
        {
            logger?.LogInformation(
                "Tier {Tier} filtered {BlockedActionCount} component actions for {AgentName}: {BlockedActions}",
                entitlementService.CurrentTier,
                entitlementEvaluation.BlockedActionKeys.Count,
                registration.Name,
                ComponentActionPolicy.SummarizeBlockedActions(entitlementEvaluation.BlockedActionKeys));
        }

        var allowedComponents = entitlementEvaluation.AllowedComponents;
        var registeredComponentSnapshots = RegisteredComponentSnapshotBuilder.Build(componentRegistry);
        var conversationKey = BuildConversationKey(request, registration.Name);
        var hasRegisteredComponents = registeredComponentSnapshots.Count > 0;
        var blockedActionKeys = policyEvaluation.BlockedActionKeys
            .Concat(entitlementEvaluation.BlockedActionKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AgentBlazorRunTelemetryEvent CreateRunEvent(
            AgentBlazorRunEventKind kind,
            AgentBlazorRunOutcome? outcome = null,
            int plannedActionCount = 0,
            int executionResultCount = 0,
            int failedExecutionCount = 0,
            string? detail = null) =>
            new()
            {
                Kind = kind,
                Source = AgentBlazorTelemetrySources.Runtime,
                AgentName = registration.Name,
                RequestedAgentName = request.AgentName,
                Outcome = outcome,
                PlannedActionCount = plannedActionCount,
                ExecutionResultCount = executionResultCount,
                FailedExecutionCount = failedExecutionCount,
                BlockedActionCount = blockedActionKeys.Length,
                Tier = tierName,
                ProviderConfigured = providerConfigured,
                HasContext = hasContext,
                HasRegisteredComponents = hasRegisteredComponents,
                Detail = detail
            };

        await TrackRunEventAsync(CreateRunEvent(AgentBlazorRunEventKind.Started));

        ConcurrentQueue<PlannedComponentAction>? plannedActions = null;
        ConcurrentQueue<ComponentActionExecutionResult>? executionResults = null;

        try
        {
            plannedActions = new ConcurrentQueue<PlannedComponentAction>();
            executionResults = new ConcurrentQueue<ComponentActionExecutionResult>();

            var pendingResponse = await TryHandlePendingClarificationAsync(
                request,
                registration,
                conversationKey,
                registeredComponentSnapshots,
                plannedActions,
                executionResults,
                cancellationToken);
            if (pendingResponse is not null)
            {
                var plannedSnapshot = plannedActions.ToArray();
                var executionSnapshot = executionResults.ToArray();
                await TrackRunEventAsync(CreateRunEvent(
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.Succeeded,
                    plannedActionCount: plannedSnapshot.Length,
                    executionResultCount: executionSnapshot.Length,
                    failedExecutionCount: CountFailedExecutionResults(executionSnapshot)));
                return pendingResponse;
            }

            if (chatClient is null)
            {
                var providerMissingResponse = new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText: "No provider is configured. Register an AgentBlazor provider (OpenAI or Azure OpenAI) to run the Microsoft Agent Framework runtime.",
                    PlannedActions: [],
                    ExecutionResults: []);

                await TrackRunEventAsync(CreateRunEvent(
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.ProviderMissing,
                    detail: "No framework provider is configured."));
                return providerMissingResponse;
            }

            var tools = BuildTools(
                registration,
                request.UserMessage,
                allowedComponents,
                registeredComponentSnapshots,
                plannedActions,
                executionResults,
                cancellationToken);
            if (tools.Count == 0)
            {
                var policyMessageSuffix = blockedActionKeys.Length > 0
                    ? $" Filtered actions: {ComponentActionPolicy.SummarizeBlockedActions(blockedActionKeys)}."
                    : string.Empty;
                var tierSuffix = entitlementService is not null && entitlementEvaluation.BlockedActionKeys.Count > 0
                    ? $" Current tier: {entitlementService.CurrentTier}."
                    : string.Empty;

                logger?.LogWarning(
                    "No allowed component tools are available for {AgentName}. Policy and tier checks excluded all registered component actions.",
                    registration.Name);

                var noActionsResponse = new AgentTurnResponse(
                    AgentName: registration.Name,
                    ResponseText:
                    "No allowed component actions are available for this agent policy. " +
                    "Adjust AllowedComponents/AllowedActions and licensing tier for this agent registration." +
                    policyMessageSuffix +
                    tierSuffix,
                    PlannedActions: [],
                    ExecutionResults: []);

                await TrackRunEventAsync(CreateRunEvent(
                    AgentBlazorRunEventKind.Finished,
                    AgentBlazorRunOutcome.NoAllowedActions,
                    detail: "Policy and tier filtering removed all component actions."));
                return noActionsResponse;
            }

            var agent = new ChatClientAgent(
                chatClient,
                new ChatClientAgentOptions
                {
                    Name = registration.Name,
                    Description = registration.Description,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = BuildInstructions(registration, allowedComponents, registeredComponentSnapshots),
                        Tools = tools,
                        ToolMode = ChatToolMode.Auto
                    }
                },
                services: services);

            var runOptions = CreateRunOptions(request.Context, registeredComponentSnapshots);
            var agentResponse = await agent.RunAsync(
                BuildPrompt(request, registeredComponentSnapshots),
                options: runOptions,
                cancellationToken: cancellationToken);

            var text = string.IsNullOrWhiteSpace(agentResponse.Text)
                ? "Run completed."
                : agentResponse.Text.Trim();

            if (plannedActions.IsEmpty && executionResults.IsEmpty)
            {
                var fallbackActions = ResolveStructuredToolActions(text, allowedComponents);
                if (fallbackActions.Count > 0)
                {
                    foreach (var fallbackAction in fallbackActions)
                    {
                        var planned = new PlannedComponentAction(
                            fallbackAction.ComponentId,
                            fallbackAction.ActionId,
                            $"{BuildToolInvocationReason(request.UserMessage)} (structured tool directive fallback)",
                            BuildToolArgumentsWithContext(
                                request.UserMessage,
                                fallbackAction.ComponentId,
                                fallbackAction.ActionId,
                                registeredComponentSnapshots));
                        plannedActions.Enqueue(planned);

                        ComponentActionExecutionResult result;
                        if (fallbackAction.RequiresApproval &&
                            !ComponentActionApprovalPolicy.IsApprovalGranted(fallbackAction.ComponentId, fallbackAction.ActionId))
                        {
                            result = new ComponentActionExecutionResult(
                                fallbackAction.ComponentId,
                                fallbackAction.ActionId,
                                Succeeded: false,
                                Message: $"Approval required for {fallbackAction.ComponentId}.{fallbackAction.ActionId}.");
                        }
                        else
                        {
                            result = await executor.ExecuteAsync(planned, cancellationToken);
                        }

                        executionResults.Enqueue(result);
                    }

                    logger?.LogInformation(
                        "Executed {FallbackActionCount} structured tool directive actions from model text output.",
                        fallbackActions.Count);

                    if (LooksLikeStructuredToolDirective(text))
                    {
                        text = $"Executed {fallbackActions.Count} action(s).";
                    }
                }
            }

            if (blockedActionKeys.Length > 0 &&
                plannedActions.IsEmpty &&
                executionResults.IsEmpty)
            {
                if (entitlementService is not null && entitlementEvaluation.BlockedActionKeys.Count > 0)
                {
                    text = $"{text} Tier '{entitlementService.CurrentTier}' filtered disallowed actions: " +
                           $"{ComponentActionPolicy.SummarizeBlockedActions(blockedActionKeys)}.";
                }
                else
                {
                    text = $"{text} Policy filtered disallowed actions: " +
                           $"{ComponentActionPolicy.SummarizeBlockedActions(blockedActionKeys)}.";
                }
            }

            var plannedActionSnapshot = plannedActions.ToArray();
            var executionResultSnapshot = executionResults.ToArray();
            UpdatePendingClarification(
                conversationKey,
                request.UserMessage,
                plannedActionSnapshot,
                executionResultSnapshot,
                registeredComponentSnapshots);
            text = AppendFailureGuidance(
                text,
                request.UserMessage,
                executionResultSnapshot,
                registeredComponentSnapshots);
            var response = new AgentTurnResponse(
                AgentName: registration.Name,
                ResponseText: text,
                PlannedActions: plannedActionSnapshot,
                ExecutionResults: executionResultSnapshot);

            await TrackRunEventAsync(CreateRunEvent(
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Succeeded,
                plannedActionCount: plannedActionSnapshot.Length,
                executionResultCount: executionResultSnapshot.Length,
                failedExecutionCount: CountFailedExecutionResults(executionResultSnapshot)));

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TrackRunEventAsync(CreateRunEvent(
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Canceled,
                plannedActionCount: plannedActions?.Count ?? 0,
                executionResultCount: executionResults?.Count ?? 0,
                failedExecutionCount: CountFailedExecutionResults(executionResults),
                detail: "Run canceled."));
            throw;
        }
        catch (Exception ex)
        {
            await TrackRunEventAsync(CreateRunEvent(
                AgentBlazorRunEventKind.Finished,
                AgentBlazorRunOutcome.Failed,
                plannedActionCount: plannedActions?.Count ?? 0,
                executionResultCount: executionResults?.Count ?? 0,
                failedExecutionCount: CountFailedExecutionResults(executionResults),
                detail: ex.Message));
            throw;
        }
    }

    private AgentRegistration? ResolveAgent(string? requestedAgentName)
    {
        if (!string.IsNullOrWhiteSpace(requestedAgentName) &&
            agentRegistry.TryGet(requestedAgentName, out var requested))
        {
            return requested;
        }

        if (agentRegistry.TryGet(options.Value.DefaultAgent.Name, out var configuredDefault))
        {
            return configuredDefault;
        }

        return agentRegistry.GetAll()
            .OrderBy(static agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private ComponentActionPolicyEvaluation ResolveAllowedComponents(AgentRegistration registration)
        => ComponentActionPolicy.EvaluateAllowedCapabilities(
            componentCatalog.GetComponents(),
            registration.AllowedComponents,
            registration.AllowedActions);

    private List<AITool> BuildTools(
        AgentRegistration registration,
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        ConcurrentQueue<PlannedComponentAction> plannedActions,
        ConcurrentQueue<ComponentActionExecutionResult> executionResults,
        CancellationToken cancellationToken)
    {
        var tools = new List<AITool>();
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in allowedComponents)
        {
            foreach (var action in component.Actions)
            {
                var componentId = component.ComponentId;
                var actionId = action.ActionId;
                var reason = BuildToolInvocationReason(userMessage);
                var toolName = BuildComponentToolName(componentId, actionId);
                if (!knownNames.Add(toolName))
                {
                    continue;
                }

                async Task<string> ExecuteActionAsync(
                    string? column = null,
                    string? @operator = null,
                    object? value = null,
                    string? direction = null,
                    int? pageIndex = null,
                    int? page = null,
                    int? pageSize = null,
                    string? rowKey = null,
                    string? uri = null,
                    string? url = null,
                    string? target = null,
                    string? field = null,
                    object? fieldValue = null,
                    int? index = null,
                    CancellationToken callToken = default)
                {
                    var arguments = BuildToolArgumentsWithContext(
                        userMessage,
                        componentId,
                        actionId,
                        registeredComponentSnapshots,
                        column,
                        @operator,
                        value,
                        direction,
                        pageIndex,
                        page,
                        pageSize,
                        rowKey,
                        uri,
                        url,
                        target,
                        field,
                        fieldValue,
                        index);
                    var planned = new PlannedComponentAction(
                        componentId,
                        actionId,
                        reason,
                        arguments.Count == 0 ? null : arguments);
                    plannedActions.Enqueue(planned);

                    ComponentActionExecutionResult result;
                    if (action.RequiresApproval && !ComponentActionApprovalPolicy.IsApprovalGranted(componentId, actionId))
                    {
                        result = new ComponentActionExecutionResult(
                            ComponentId: componentId,
                            ActionId: actionId,
                            Succeeded: false,
                            Message: $"Approval required for {componentId}.{actionId}.");
                    }
                    else
                    {
                        if (callToken.CanBeCanceled && cancellationToken.CanBeCanceled)
                        {
                            using var linked = CancellationTokenSource.CreateLinkedTokenSource(callToken, cancellationToken);
                            result = await executor.ExecuteAsync(planned, linked.Token);
                        }
                        else
                        {
                            var effectiveToken = callToken.CanBeCanceled ? callToken : cancellationToken;
                            result = await executor.ExecuteAsync(planned, effectiveToken);
                        }
                    }

                    executionResults.Enqueue(result);
                    return result.Message;
                }

                tools.Add(AIFunctionFactory.Create(
                    (Func<string?, string?, object?, string?, int?, int?, int?, string?, string?, string?, string?, string?, object?, int?, CancellationToken, Task<string>>)ExecuteActionAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = toolName,
                        Description = BuildToolDescription(component, action)
                    }));
            }
        }

        AddRegisteredAssemblyTools(registration, tools, knownNames);

        return tools;
    }

    private void AddRegisteredAssemblyTools(
        AgentRegistration registration,
        ICollection<AITool> tools,
        ISet<string> knownNames)
    {
        foreach (var assemblyName in registration.ToolAssemblyNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var assembly = ResolveAssembly(assemblyName);
            if (assembly is null)
            {
                continue;
            }

            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsGenericTypeDefinition || type.IsNestedPrivate)
                {
                    continue;
                }

                object? instance = null;
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    if (!IsToolMethodCandidate(method))
                    {
                        continue;
                    }

                    if (!method.IsStatic)
                    {
                        if (type.IsAbstract)
                        {
                            continue;
                        }

                        instance ??= CreateToolInstance(type);
                        if (instance is null)
                        {
                            continue;
                        }
                    }

                    var toolName = BuildAssemblyToolName(type, method);
                    if (!knownNames.Add(toolName))
                    {
                        continue;
                    }

                    tools.Add(AIFunctionFactory.Create(
                        method,
                        method.IsStatic ? null : instance,
                        new AIFunctionFactoryOptions
                        {
                            Name = toolName,
                            Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
                        }));
                }
            }
        }
    }

    private object? CreateToolInstance(Type type)
    {
        try
        {
            return ActivatorUtilities.GetServiceOrCreateInstance(services, type);
        }
        catch
        {
            return null;
        }
    }

    private static Assembly? ResolveAssembly(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.FullName, assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsToolMethodCandidate(MethodInfo method)
    {
        if (!method.IsPublic || method.IsSpecialName || method.ContainsGenericParameters)
        {
            return false;
        }

        return method.GetCustomAttribute<DescriptionAttribute>() is not null ||
               method.GetCustomAttribute<DisplayNameAttribute>() is not null;
    }

    private static IReadOnlyList<FallbackAction> ResolveStructuredToolActions(
        string responseText,
        IReadOnlyList<ComponentCapability> allowedComponents)
    {
        if (!LooksLikeStructuredToolDirective(responseText))
        {
            return [];
        }

        var directives = ExtractStructuredToolDirectiveNames(responseText);
        if (directives.Count == 0)
        {
            return [];
        }

        var catalog = allowedComponents
            .SelectMany(component => component.Actions.Select(action => new FallbackAction(
                component.ComponentId,
                action.ActionId,
                action.RequiresApproval)))
            .ToArray();

        var byToolName = new Dictionary<string, FallbackAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in catalog)
        {
            byToolName[BuildComponentToolName(action.ComponentId, action.ActionId)] = action;
            byToolName[ComponentActionPolicy.ToActionKey(action.ComponentId, action.ActionId)] = action;
        }

        var resolved = new List<FallbackAction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directive in directives)
        {
            if (TryResolveFallbackAction(directive, byToolName, catalog, out var action))
            {
                var key = ComponentActionPolicy.ToActionKey(action.ComponentId, action.ActionId);
                if (seen.Add(key))
                {
                    resolved.Add(action);
                }
            }
        }

        return resolved;
    }

    private static bool TryResolveFallbackAction(
        string directive,
        IReadOnlyDictionary<string, FallbackAction> byToolName,
        IReadOnlyList<FallbackAction> catalog,
        out FallbackAction action)
    {
        var trimmed = directive.Trim();
        if (trimmed.Length == 0)
        {
            action = default;
            return false;
        }

        if (byToolName.TryGetValue(trimmed, out action))
        {
            return true;
        }

        var sanitized = SanitizeToolName(trimmed);
        if (byToolName.TryGetValue(sanitized, out action))
        {
            return true;
        }

        var actionIdMatches = catalog
            .Where(candidate =>
                string.Equals(candidate.ActionId, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(SanitizeToolName(candidate.ActionId), sanitized, StringComparison.OrdinalIgnoreCase) ||
                sanitized.EndsWith($"_{SanitizeToolName(candidate.ActionId)}", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(static candidate => ComponentActionPolicy.ToActionKey(candidate.ComponentId, candidate.ActionId), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (actionIdMatches.Length == 1)
        {
            action = actionIdMatches[0];
            return true;
        }

        action = default;
        return false;
    }

    private static IReadOnlyList<string> ExtractStructuredToolDirectiveNames(string responseText)
    {
        var trimmed = UnwrapCodeFence(responseText.Trim());
        if (trimmed.Length == 0)
        {
            return [];
        }

        var names = new List<string>();
        if (TryExtractDirectiveNamesFromJson(trimmed, names))
        {
            return names;
        }

        const string quotedPattern = "\"name\"\\s*:\\s*\"([^\"]+)\"";
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(
                     trimmed,
                     quotedPattern,
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                     System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            if (match is System.Text.RegularExpressions.Match m &&
                m.Groups.Count > 1 &&
                !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                names.Add(m.Groups[1].Value.Trim());
            }
        }

        const string barePattern = "\"name\"\\s*:\\s*([A-Za-z0-9._:-]+)";
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(
                     trimmed,
                     barePattern,
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                     System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            if (match is System.Text.RegularExpressions.Match m &&
                m.Groups.Count > 1 &&
                !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                names.Add(m.Groups[1].Value.Trim());
            }
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryExtractDirectiveNamesFromJson(string text, ICollection<string> names)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            ExtractNamesFromJsonElement(document.RootElement, names);
            return names.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ExtractNamesFromJsonElement(JsonElement element, ICollection<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("name", out var nameProp) &&
                    nameProp.ValueKind == JsonValueKind.String)
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }

                if (element.TryGetProperty("function", out var functionProp) &&
                    functionProp.ValueKind == JsonValueKind.Object)
                {
                    ExtractNamesFromJsonElement(functionProp, names);
                }

                if (element.TryGetProperty("tool_calls", out var toolCallsProp) &&
                    toolCallsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in toolCallsProp.EnumerateArray())
                    {
                        ExtractNamesFromJsonElement(child, names);
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    ExtractNamesFromJsonElement(property.Value, names);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractNamesFromJsonElement(item, names);
                }

                break;
        }
    }

    private static bool LooksLikeStructuredToolDirective(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var trimmed = responseText.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[') || trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return true;
        }

        return trimmed.Contains("\"name\"", StringComparison.OrdinalIgnoreCase) &&
               (trimmed.Contains("\"arguments\"", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("\"tool_calls\"", StringComparison.OrdinalIgnoreCase));
    }

    private static string UnwrapCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) ||
            !text.EndsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewLine = text.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return text.Trim('`');
        }

        return text[(firstNewLine + 1)..^3].Trim();
    }

    private static string BuildPrompt(
        AgentTurnRequest request,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        if ((request.Context is null || request.Context.Count == 0) &&
            registeredComponents.Count == 0)
        {
            return request.UserMessage;
        }

        var builder = new StringBuilder(request.UserMessage);
        if (request.Context is not null && request.Context.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Context:");
            foreach (var entry in request.Context.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("- ").Append(entry.Key).Append(": ").AppendLine(entry.Value);
            }
        }

        if (registeredComponents.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Registered Components:");
            foreach (var component in registeredComponents)
            {
                builder.Append("- ")
                    .Append(component.AgentId)
                    .Append(" (")
                    .Append(component.ComponentType)
                    .Append(") actions: ")
                    .Append(component.Actions.Length == 0 ? "none" : string.Join(", ", component.Actions))
                    .AppendLine();
                if (component.State.Count > 0)
                {
                    builder.Append("  state: ")
                        .AppendLine(string.Join(", ", component.State.Select(static kvp => $"{kvp.Key}={kvp.Value}")));
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildInstructions(
        AgentRegistration registration,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are AgentBlazor's built-in UI agent powered by Microsoft Agent Framework.");
        builder.AppendLine("Always use the provided tools when a user asks for UI actions.");
        builder.AppendLine("Prefer concise responses and reference completed UI actions.");
        builder.AppendLine("If required action parameters are missing or ambiguous, ask a concise clarifying question before calling a tool.");
        builder.AppendLine("When sorting or filtering, include explicit column/operator/value arguments.");
        if (!string.IsNullOrWhiteSpace(registration.Instructions))
        {
            builder.AppendLine(registration.Instructions);
        }

        builder.AppendLine("Allowed components and actions:");
        foreach (var component in allowedComponents.OrderBy(static c => c.ComponentId, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("- ").Append(component.ComponentId).Append(": ").Append(component.Description).AppendLine();
            foreach (var action in component.Actions.OrderBy(static a => a.ActionId, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("  - ").Append(action.ActionId).Append(": ").Append(action.Description);
                if (action.RequiresApproval)
                {
                    builder.Append(" (requires approval)");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("Registered component instances and state:");
        if (registeredComponents.Count == 0)
        {
            builder.AppendLine("- None are currently registered.");
        }
        else
        {
            foreach (var component in registeredComponents)
            {
                builder.Append("- ").Append(component.AgentId)
                    .Append(" (type=").Append(component.ComponentType).Append(")");
                if (component.Actions.Length > 0)
                {
                    builder.Append(" actions=[").Append(string.Join(", ", component.Actions)).Append(']');
                }

                builder.AppendLine();
                if (component.State.Count > 0)
                {
                    builder.Append("  state: ")
                        .AppendLine(string.Join(", ", component.State.Select(static kvp => $"{kvp.Key}={kvp.Value}")));
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildComponentToolName(string componentId, string actionId) =>
        SanitizeToolName($"agentblazor_{componentId}_{actionId}");

    private static string BuildAssemblyToolName(Type type, MethodInfo method) =>
        SanitizeToolName($"agentblazor_external_{type.Name}_{method.Name}");

    private static string BuildToolInvocationReason(string userMessage)
    {
        var trimmed = userMessage?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "Selected by the framework agent tool invocation.";
        }

        const int maxLength = 240;
        if (trimmed.Length > maxLength)
        {
            trimmed = $"{trimmed[..maxLength]}...";
        }

        return $"Selected by the framework agent tool invocation for user request: {trimmed}";
    }

    private static string SanitizeToolName(string value)
    {
        var chars = value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        return new string(chars);
    }

    private readonly record struct FallbackAction(string ComponentId, string ActionId, bool RequiresApproval);

    private static string BuildToolDescription(ComponentCapability component, ComponentActionCapability action)
    {
        var suffix = action.RequiresApproval
            ? "This action requires approval before execution in production."
            : "Execute this UI action.";
        return $"{component.ComponentId}.{action.ActionId}: {action.Description} {suffix}";
    }

    private static AgentRunOptions? CreateRunOptions(
        IDictionary<string, string>? context,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        if ((context is null || context.Count == 0) &&
            registeredComponents.Count == 0)
        {
            return null;
        }

        var additionalProperties = new AdditionalPropertiesDictionary();
        if (context is not null && context.Count > 0)
        {
            additionalProperties["agentblazor_context"] = new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase);
        }

        if (registeredComponents.Count > 0)
        {
            additionalProperties["agentblazor_registered_components"] = registeredComponents
                .Select(static component => new Dictionary<string, object?>
                {
                    ["agentId"] = component.AgentId,
                    ["componentType"] = component.ComponentType,
                    ["actions"] = component.Actions,
                    ["state"] = component.State
                })
                .ToArray();
        }

        return new AgentRunOptions
        {
            AdditionalProperties = additionalProperties
        };
    }

    private static Dictionary<string, object?> BuildToolArgumentsWithContext(
        string userMessage,
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        string? column = null,
        string? @operator = null,
        object? value = null,
        string? direction = null,
        int? pageIndex = null,
        int? page = null,
        int? pageSize = null,
        string? rowKey = null,
        string? uri = null,
        string? url = null,
        string? target = null,
        string? field = null,
        object? fieldValue = null,
        int? index = null)
    {
        var args = BuildToolArguments(
            column,
            @operator,
            value,
            direction,
            pageIndex,
            page,
            pageSize,
            rowKey,
            uri,
            url,
            target,
            field,
            fieldValue,
            index);

        AddIfNotNullOrWhiteSpace(args, "intent", userMessage);
        AddComponentStateHints(args, componentId, actionId, registeredComponentSnapshots);
        return args;
    }

    private static Dictionary<string, object?> BuildToolArguments(
        string? column,
        string? @operator,
        object? value,
        string? direction,
        int? pageIndex,
        int? page,
        int? pageSize,
        string? rowKey,
        string? uri,
        string? url,
        string? target,
        string? field,
        object? fieldValue,
        int? index)
    {
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        AddIfNotNullOrWhiteSpace(args, "column", column);
        AddIfNotNullOrWhiteSpace(args, "operator", @operator);
        AddIfNotNull(args, "value", value);
        AddIfNotNullOrWhiteSpace(args, "direction", direction);
        AddIfNotNull(args, "pageIndex", pageIndex);
        AddIfNotNull(args, "page", page);
        AddIfNotNull(args, "pageSize", pageSize);
        AddIfNotNullOrWhiteSpace(args, "rowKey", rowKey);
        AddIfNotNullOrWhiteSpace(args, "uri", uri);
        AddIfNotNullOrWhiteSpace(args, "url", url);
        AddIfNotNullOrWhiteSpace(args, "target", target);
        AddIfNotNullOrWhiteSpace(args, "field", field);
        AddIfNotNull(args, "fieldValue", fieldValue);
        AddIfNotNull(args, "index", index);
        return args;
    }

    private static void AddComponentStateHints(
        IDictionary<string, object?> arguments,
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots)
    {
        if (!TryResolveComponentState(componentId, actionId, registeredComponentSnapshots, out var state))
        {
            return;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            AddStateHint(arguments, state, "sortColumn", "currentSortColumn");
            AddStateHint(arguments, state, "filterColumn", "currentFilterColumn");
            AddStateHint(arguments, state, "currentPageIndex", "currentPageIndex");
            AddStateHint(arguments, state, "pageSize", "currentPageSize");
            AddStateHint(arguments, state, "focusedRowKey", "currentRowKey");
            return;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase))
        {
            AddStateHint(arguments, state, "activePanelIndex", "currentIndex");
            return;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
        {
            AddStateHint(arguments, state, "uri", "currentUri");
        }
    }

    private static void AddStateHint(
        IDictionary<string, object?> arguments,
        IReadOnlyDictionary<string, string> state,
        string stateKey,
        string argumentKey)
    {
        if (arguments.ContainsKey(argumentKey))
        {
            return;
        }

        if (!state.TryGetValue(stateKey, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (int.TryParse(raw, out var intValue))
        {
            arguments[argumentKey] = intValue;
            return;
        }

        if (bool.TryParse(raw, out var boolValue))
        {
            arguments[argumentKey] = boolValue;
            return;
        }

        arguments[argumentKey] = raw;
    }

    private static bool TryResolveComponentState(
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        out IReadOnlyDictionary<string, string> state)
    {
        state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var componentType = ResolveComponentType(componentId);
        if (componentType.Length == 0 || registeredComponentSnapshots.Count == 0)
        {
            return false;
        }

        foreach (var snapshot in registeredComponentSnapshots
                     .OrderBy(static snapshot => snapshot.AgentId, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(snapshot.ComponentType, componentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!snapshot.Actions.Contains(actionId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            state = snapshot.State;
            return true;
        }

        foreach (var snapshot in registeredComponentSnapshots
                     .OrderBy(static snapshot => snapshot.AgentId, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(snapshot.ComponentType, componentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            state = snapshot.State;
            return true;
        }

        return false;
    }

    private static string ResolveComponentType(string componentId) =>
        componentId switch
        {
            AgentComponentV1CapabilityProfile.AgentDataGridComponentId => "DataGrid",
            AgentComponentV1CapabilityProfile.AgentDialogComponentId => "Dialog",
            AgentComponentV1CapabilityProfile.AgentFormComponentId => "Form",
            AgentComponentV1CapabilityProfile.AgentNavMenuComponentId => "NavMenu",
            AgentComponentV1CapabilityProfile.AgentTabsComponentId => "Tabs",
            _ => string.Empty
        };

    private static string AppendFailureGuidance(
        string responseText,
        string userMessage,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var guidance = BuildFailureGuidance(userMessage, executionResults, registeredComponents);
        if (string.IsNullOrWhiteSpace(guidance))
        {
            return responseText;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return guidance;
        }

        if (responseText.Contains(guidance, StringComparison.OrdinalIgnoreCase))
        {
            return responseText;
        }

        return $"{responseText} {guidance}";
    }

    private static string? BuildFailureGuidance(
        string userMessage,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var failed = executionResults
            .Where(static result => !result.Succeeded)
            .ToArray();
        if (failed.Length == 0)
        {
            return null;
        }

        var suggestions = new List<string>();
        foreach (var result in failed)
        {
            if (!TryExtractMissingParameter(result.Message, out var parameter))
            {
                continue;
            }

            var suggestion = BuildMissingParameterSuggestion(
                userMessage,
                result.ComponentId,
                result.ActionId,
                parameter,
                registeredComponents);
            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                suggestions.Add(suggestion);
            }
        }

        var unique = suggestions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (unique.Length == 0)
        {
            return null;
        }

        return $"I need one more detail: {string.Join(" ", unique)}";
    }

    private static bool TryExtractMissingParameter(string? message, out string parameter)
    {
        parameter = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            message,
            "requires\\s+'(?<param>[^']+)'\\s+parameter",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        parameter = match.Groups["param"].Value.Trim();
        return parameter.Length > 0;
    }

    private static string BuildMissingParameterSuggestion(
        string userMessage,
        string componentId,
        string actionId,
        string parameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parameter, "column", StringComparison.OrdinalIgnoreCase))
        {
            if (TrySuggestDataGridSort(userMessage, registeredComponents, out var column, out var direction))
            {
                var directionText = direction switch
                {
                    "desc" => " descending",
                    "asc" => " ascending",
                    _ => string.Empty
                };
                return $"Did you mean sort by '{column}'{directionText}?";
            }

            return "Which column should I sort by (for example RiskScore, Region, Name, or LastAuditDate)?";
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
        {
            return parameter.ToLowerInvariant() switch
            {
                "column" => "Which column should I filter (for example RiskScore, Region, or Name)?",
                "operator" => "Which filter operator should I use (for example >=, <=, eq, contains)?",
                "value" => "What value should I filter by?",
                _ => $"Please provide '{parameter}' for the requested filter."
            };
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parameter, "uri", StringComparison.OrdinalIgnoreCase))
        {
            return "Which route should I navigate to (for example /suppliers or /settings)?";
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parameter, "index", StringComparison.OrdinalIgnoreCase))
        {
            return "Which tab should I switch to (for example first, second, or an explicit index)?";
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            return parameter.ToLowerInvariant() switch
            {
                "field" => "Which form field should I set?",
                "value" => "What value should I set for that field?",
                _ => $"Please provide '{parameter}' so I can continue."
            };
        }

        return $"Please provide '{parameter}' so I can continue.";
    }

    private static bool TrySuggestDataGridSort(
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        out string column,
        out string? direction)
    {
        direction = InferSortDirection(userMessage);

        if (TryGetDataGridStateValue(registeredComponents, "filterColumn", out var filterColumn))
        {
            column = filterColumn;
            return true;
        }

        if (TryGetDataGridStateValue(registeredComponents, "sortColumn", out var sortColumn))
        {
            column = sortColumn;
            return true;
        }

        if (ContainsAny(userMessage, "risk", "highest", "lowest", "high", "low"))
        {
            column = "RiskScore";
            return true;
        }

        if (ContainsAny(userMessage, "audit", "date"))
        {
            column = "LastAuditDate";
            return true;
        }

        if (ContainsAny(userMessage, "region"))
        {
            column = "Region";
            return true;
        }

        if (ContainsAny(userMessage, "name", "supplier"))
        {
            column = "Name";
            return true;
        }

        column = string.Empty;
        return false;
    }

    private static string? InferSortDirection(string userMessage)
    {
        if (ContainsAny(userMessage, "highest to lowest", "high to low", "descending", "desc"))
        {
            return "desc";
        }

        if (ContainsAny(userMessage, "lowest to highest", "low to high", "ascending", "asc"))
        {
            return "asc";
        }

        if (ContainsAny(userMessage, "highest", "max", "top"))
        {
            return "desc";
        }

        if (ContainsAny(userMessage, "lowest", "min", "bottom"))
        {
            return "asc";
        }

        return null;
    }

    private static bool TryGetDataGridStateValue(
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        string key,
        out string value)
    {
        value = string.Empty;
        foreach (var component in registeredComponents)
        {
            if (!string.Equals(component.ComponentType, "DataGrid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!component.State.TryGetValue(key, out var raw) ||
                string.IsNullOrWhiteSpace(raw) ||
                string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = raw;
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private async Task<AgentTurnResponse?> TryHandlePendingClarificationAsync(
        AgentTurnRequest request,
        AgentRegistration registration,
        string conversationKey,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        ConcurrentQueue<PlannedComponentAction> plannedActions,
        ConcurrentQueue<ComponentActionExecutionResult> executionResults,
        CancellationToken cancellationToken)
    {
        if (!_pendingClarifications.TryGetValue(conversationKey, out var pending))
        {
            return null;
        }

        var message = request.UserMessage.Trim();
        if (message.Length == 0)
        {
            return null;
        }

        if (IsNegativeReply(message))
        {
            _pendingClarifications.TryRemove(conversationKey, out _);
            return new AgentTurnResponse(
                registration.Name,
                "Okay, canceled that pending action.",
                PlannedActions: [],
                ExecutionResults: []);
        }

        if (!TryResolvePendingArguments(message, pending, out var resolvedArguments))
        {
            if (!IsAffirmativeReply(message))
            {
                // Treat this as a new instruction and clear stale pending clarification.
                _pendingClarifications.TryRemove(conversationKey, out _);
                return null;
            }

            return new AgentTurnResponse(
                registration.Name,
                $"I still need '{pending.MissingParameter}'. {pending.Prompt}",
                PlannedActions: [],
                ExecutionResults: []);
        }

        var planned = new PlannedComponentAction(
            pending.ComponentId,
            pending.ActionId,
            "User confirmed pending clarification.",
            resolvedArguments);
        plannedActions.Enqueue(planned);

        var result = await executor.ExecuteAsync(planned, cancellationToken);
        executionResults.Enqueue(result);

        if (result.Succeeded)
        {
            _pendingClarifications.TryRemove(conversationKey, out _);
        }
        else
        {
            UpdatePendingClarification(
                conversationKey,
                request.UserMessage,
                [planned],
                [result],
                registeredComponentSnapshots);
        }

        var text = $"Executed 1 action(s). {result.Message}";
        text = AppendFailureGuidance(text, request.UserMessage, [result], registeredComponentSnapshots);

        return new AgentTurnResponse(
            registration.Name,
            text,
            PlannedActions: [planned],
            ExecutionResults: [result]);
    }

    private void UpdatePendingClarification(
        string conversationKey,
        string userMessage,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        if (TryBuildPendingClarification(
                userMessage,
                plannedActions,
                executionResults,
                registeredComponents,
                out var pending))
        {
            _pendingClarifications[conversationKey] = pending!;
            return;
        }

        _pendingClarifications.TryRemove(conversationKey, out _);
    }

    private static bool TryBuildPendingClarification(
        string userMessage,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        out PendingClarification? pending)
    {
        pending = null;
        foreach (var result in executionResults.Where(static item => !item.Succeeded))
        {
            if (!TryExtractMissingParameter(result.Message, out var missingParameter))
            {
                continue;
            }

            var planned = plannedActions.FirstOrDefault(action =>
                string.Equals(action.ComponentId, result.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action.ActionId, result.ActionId, StringComparison.OrdinalIgnoreCase));
            if (planned is null)
            {
                continue;
            }

            var question = BuildMissingParameterSuggestion(
                userMessage,
                result.ComponentId,
                result.ActionId,
                missingParameter,
                registeredComponents);
            if (string.IsNullOrWhiteSpace(question))
            {
                continue;
            }

            var suggestedArguments = BuildSuggestedArgumentsForMissingParameter(
                userMessage,
                result.ComponentId,
                result.ActionId,
                missingParameter,
                registeredComponents);
            var baseArguments = planned.Arguments is null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(planned.Arguments, StringComparer.OrdinalIgnoreCase);

            pending = new PendingClarification(
                result.ComponentId,
                result.ActionId,
                missingParameter,
                question,
                baseArguments,
                suggestedArguments);
            return true;
        }

        return false;
    }

    private static Dictionary<string, object?> BuildSuggestedArgumentsForMissingParameter(
        string userMessage,
        string componentId,
        string actionId,
        string missingParameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(missingParameter, "column", StringComparison.OrdinalIgnoreCase) &&
            TrySuggestDataGridSort(userMessage, registeredComponents, out var column, out var direction))
        {
            args["column"] = column;
            if (!string.IsNullOrWhiteSpace(direction))
            {
                args["direction"] = direction;
            }

            return args;
        }

        return args;
    }

    private static bool TryResolvePendingArguments(
        string userReply,
        PendingClarification pending,
        out IReadOnlyDictionary<string, object?> resolvedArguments)
    {
        var merged = new Dictionary<string, object?>(
            pending.BaseArguments,
            StringComparer.OrdinalIgnoreCase);

        if (IsAffirmativeReply(userReply) && pending.SuggestedArguments.Count > 0)
        {
            foreach (var pair in pending.SuggestedArguments)
            {
                merged[pair.Key] = pair.Value;
            }

            resolvedArguments = merged;
            return true;
        }

        if (!TryParseParameterFromReply(userReply, pending, out var parsed))
        {
            resolvedArguments = merged;
            return false;
        }

        merged[pending.MissingParameter] = parsed;
        resolvedArguments = merged;
        return true;
    }

    private static bool TryParseParameterFromReply(
        string userReply,
        PendingClarification pending,
        out object? parsedValue)
    {
        parsedValue = null;
        if (string.IsNullOrWhiteSpace(userReply))
        {
            return false;
        }

        if (string.Equals(pending.MissingParameter, "column", StringComparison.OrdinalIgnoreCase))
        {
            var knownColumns = new[] { "RiskScore", "Region", "Name", "SupplierId", "LastAuditDate" };
            foreach (var known in knownColumns)
            {
                if (userReply.Contains(known, StringComparison.OrdinalIgnoreCase))
                {
                    parsedValue = known;
                    return true;
                }
            }

            var fallback = userReply.Trim().Trim('"', '\'');
            if (fallback.Length > 0 && fallback.Length <= 64)
            {
                parsedValue = fallback;
                return true;
            }

            return false;
        }

        if (string.Equals(pending.MissingParameter, "uri", StringComparison.OrdinalIgnoreCase))
        {
            var uriMatch = System.Text.RegularExpressions.Regex.Match(
                userReply,
                @"https?://[^\s""']+|/[a-zA-Z0-9_/\-]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!uriMatch.Success)
            {
                return false;
            }

            parsedValue = uriMatch.Value;
            return true;
        }

        if (string.Equals(pending.MissingParameter, "index", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pending.MissingParameter, "pageIndex", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(userReply.Trim(), out var index))
            {
                parsedValue = index;
                return true;
            }

            return false;
        }

        if (string.Equals(pending.MissingParameter, "value", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(userReply.Trim(), out var i))
            {
                parsedValue = i;
                return true;
            }

            if (double.TryParse(userReply.Trim(), out var d))
            {
                parsedValue = d;
                return true;
            }

            parsedValue = userReply.Trim().Trim('"', '\'');
            return true;
        }

        parsedValue = userReply.Trim().Trim('"', '\'');
        return parsedValue is string text && text.Length > 0;
    }

    private static bool IsAffirmativeReply(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return normalized is "yes" or "y" or "yeah" or "yep" or "correct" or "do it" or "go ahead" or "sounds good";
    }

    private static bool IsNegativeReply(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return normalized is "no" or "n" or "nope" or "cancel" or "stop" or "not now";
    }

    private static string BuildConversationKey(AgentTurnRequest request, string resolvedAgentName)
    {
        var sessionId = string.Empty;
        if (request.Context is not null &&
            request.Context.TryGetValue("agentblazor.session_id", out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            sessionId = value.Trim();
        }

        if (sessionId.Length == 0)
        {
            sessionId = "global";
        }

        return $"{resolvedAgentName}:{sessionId}";
    }

    private sealed record PendingClarification(
        string ComponentId,
        string ActionId,
        string MissingParameter,
        string Prompt,
        IReadOnlyDictionary<string, object?> BaseArguments,
        IReadOnlyDictionary<string, object?> SuggestedArguments);

    private static void AddIfNotNull(
        IDictionary<string, object?> dictionary,
        string key,
        object? value)
    {
        if (value is not null)
        {
            dictionary[key] = value;
        }
    }

    private static void AddIfNotNullOrWhiteSpace(
        IDictionary<string, object?> dictionary,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dictionary[key] = value;
        }
    }

    private static int CountFailedExecutionResults(IEnumerable<ComponentActionExecutionResult>? executionResults)
        => executionResults?.Count(static result => !result.Succeeded) ?? 0;

    private async ValueTask TrackRunEventAsync(AgentBlazorRunTelemetryEvent telemetryEvent)
    {
        try
        {
            await telemetrySink.TrackRunEventAsync(telemetryEvent);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "Telemetry sink failed while recording runtime event {TelemetryKind} for {AgentName}.",
                telemetryEvent.Kind,
                telemetryEvent.AgentName);
        }
    }

}
