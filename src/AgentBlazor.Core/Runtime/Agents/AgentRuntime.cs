using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Preferences;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Agents;

internal sealed class AgentRuntime(
    IServiceProvider services,
    IAgentRegistry agentRegistry,
    IComponentCapabilityCatalog componentCatalog,
    IAgentComponentRegistry? componentRegistry,
    IOptions<AgentBlazorOptions> options,
    IComponentActionExecutor executor,
    IAgentBlazorTelemetrySink telemetrySink,
    IConversationManager? conversationManager = null,
    IIntentResolver? intentResolver = null,
    IUserPreferenceService? userPreferenceService = null,
    IResponseBuilder? responseBuilder = null,
    ILogger<AgentRuntime>? logger = null,
    IChatClient? chatClient = null,
    IAgentBlazorEntitlementService? entitlementService = null) : IAgentRuntime
{
    // Legacy support: keep internal clarifications for backward compatibility
    // when IConversationManager is not available
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
        var sessionId = request.GetEffectiveSessionId();
        var userId = request.GetEffectiveUserId();

        // Get conversation history and user preferences if available
        ConversationHistory? conversationHistory = null;
        UserPreferences? userPreferences = null;
        if (conversationManager is not null)
        {
            conversationHistory = await conversationManager.GetHistoryAsync(sessionId, cancellationToken);
        }
        if (userPreferenceService is not null)
        {
            userPreferences = await userPreferenceService.GetPreferencesAsync(sessionId, cancellationToken);
        }
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
                                registeredComponentSnapshots,
                                fallbackAction.Arguments));
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

            await TryExecuteNavigationContinuationAsync(
                request.UserMessage,
                allowedComponents,
                registeredComponentSnapshots,
                plannedActions,
                executionResults,
                cancellationToken);
            await TryExecuteMissingParameterRecoveryAsync(
                request.UserMessage,
                registeredComponentSnapshots,
                plannedActions,
                executionResults,
                cancellationToken);

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

            // Store conversation turn and record actions for preferences
            await StoreConversationTurnAsync(
                sessionId,
                request.UserMessage,
                text,
                plannedActionSnapshot,
                executionResultSnapshot,
                cancellationToken);
            await RecordActionsForPreferencesAsync(
                sessionId,
                executionResultSnapshot,
                cancellationToken);

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

        var directives = ExtractStructuredToolDirectives(responseText);
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

        var resolved = new Dictionary<string, FallbackAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var directive in directives)
        {
            if (!TryResolveFallbackAction(directive.Name, byToolName, catalog, out var action))
            {
                continue;
            }

            var key = ComponentActionPolicy.ToActionKey(action.ComponentId, action.ActionId);
            var enriched = new FallbackAction(
                action.ComponentId,
                action.ActionId,
                action.RequiresApproval,
                directive.Arguments);

            if (resolved.TryGetValue(key, out var existing))
            {
                if (HasArguments(existing.Arguments) || !HasArguments(enriched.Arguments))
                {
                    continue;
                }
            }

            resolved[key] = enriched;
        }

        return resolved.Values.ToArray();
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

    private static bool HasArguments(IReadOnlyDictionary<string, object?>? arguments)
        => arguments is { Count: > 0 };

    private static IReadOnlyList<StructuredToolDirective> ExtractStructuredToolDirectives(string responseText)
    {
        var trimmed = UnwrapCodeFence(responseText.Trim());
        if (trimmed.Length == 0)
        {
            return [];
        }

        var directives = new List<StructuredToolDirective>();
        if (TryExtractDirectivesFromJson(trimmed, directives))
        {
            return directives;
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
                directives.Add(new StructuredToolDirective(m.Groups[1].Value.Trim(), Arguments: null));
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
                directives.Add(new StructuredToolDirective(m.Groups[1].Value.Trim(), Arguments: null));
            }
        }

        return directives;
    }

    private static bool TryExtractDirectivesFromJson(string text, ICollection<StructuredToolDirective> directives)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            ExtractDirectivesFromJsonElement(document.RootElement, directives);
            return directives.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ExtractDirectivesFromJsonElement(
        JsonElement element,
        ICollection<StructuredToolDirective> directives)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryExtractDirectiveFromObject(element, out var directive))
                {
                    directives.Add(directive);
                }

                foreach (var property in element.EnumerateObject())
                {
                    ExtractDirectivesFromJsonElement(property.Value, directives);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractDirectivesFromJsonElement(item, directives);
                }

                break;
        }
    }

    private static bool TryExtractDirectiveFromObject(
        JsonElement element,
        out StructuredToolDirective directive)
    {
        directive = default;
        var hasName = element.TryGetProperty("name", out var nameProp) &&
                      nameProp.ValueKind == JsonValueKind.String &&
                      !string.IsNullOrWhiteSpace(nameProp.GetString());
        if (hasName)
        {
            var name = nameProp.GetString()!;
            TryExtractDirectiveArguments(element, out var arguments);
            directive = new StructuredToolDirective(name, arguments);
            return true;
        }

        if (!element.TryGetProperty("function", out var functionProp) ||
            functionProp.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!functionProp.TryGetProperty("name", out var functionNameProp) ||
            functionNameProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(functionNameProp.GetString()))
        {
            return false;
        }

        var functionName = functionNameProp.GetString()!;
        if (!TryExtractDirectiveArguments(functionProp, out var functionArguments))
        {
            TryExtractDirectiveArguments(element, out functionArguments);
        }

        directive = new StructuredToolDirective(functionName, functionArguments);
        return true;
    }

    private static bool TryExtractDirectiveArguments(
        JsonElement element,
        out IReadOnlyDictionary<string, object?>? arguments)
    {
        arguments = null;
        if (TryExtractDirectiveArgumentsByKey(element, "arguments", out arguments))
        {
            return true;
        }

        if (TryExtractDirectiveArgumentsByKey(element, "params", out arguments))
        {
            return true;
        }

        if (TryExtractDirectiveArgumentsByKey(element, "input", out arguments))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractDirectiveArgumentsByKey(
        JsonElement element,
        string key,
        out IReadOnlyDictionary<string, object?>? arguments)
    {
        arguments = null;
        if (!element.TryGetProperty(key, out var value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (!TryParseArgumentObjectFromJsonString(raw, out arguments))
            {
                return false;
            }

            return true;
        }

        var unwrapped = JsonArgumentHelper.Unwrap(value);
        if (!TryCoerceArgumentsDictionary(unwrapped, out arguments))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseArgumentObjectFromJsonString(
        string raw,
        out IReadOnlyDictionary<string, object?>? arguments)
    {
        arguments = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var unwrapped = JsonArgumentHelper.Unwrap(document.RootElement);
            return TryCoerceArgumentsDictionary(unwrapped, out arguments);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryCoerceArgumentsDictionary(
        object? raw,
        out IReadOnlyDictionary<string, object?>? arguments)
    {
        arguments = null;
        switch (raw)
        {
            case IReadOnlyDictionary<string, object?> readOnly:
                arguments = new Dictionary<string, object?>(readOnly, StringComparer.OrdinalIgnoreCase);
                return true;

            case IDictionary<string, object?> dictionary:
                arguments = new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
                return true;

            default:
                return false;
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
        builder.AppendLine("For multi-step requests, execute all required tools in the same turn (for example navigate, then filter/sort/select).");
        builder.AppendLine("Do not stop after navigation when the user also requested work on the destination surface.");
        builder.AppendLine("You can call component tools even before the destination component is mounted; runtime will queue them.");
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

    private async Task TryExecuteNavigationContinuationAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        ConcurrentQueue<PlannedComponentAction> plannedActions,
        ConcurrentQueue<ComponentActionExecutionResult> executionResults,
        CancellationToken cancellationToken)
    {
        if (!TryInferNavigationContinuationAction(
                userMessage,
                allowedComponents,
                registeredComponentSnapshots,
                plannedActions,
                executionResults,
                out var followUpAction))
        {
            return;
        }

        var planned = new PlannedComponentAction(
            followUpAction.ComponentId,
            followUpAction.ActionId,
            $"{BuildToolInvocationReason(userMessage)} (navigation continuation)",
            BuildToolArgumentsWithContext(
                userMessage,
                followUpAction.ComponentId,
                followUpAction.ActionId,
                registeredComponentSnapshots,
                followUpAction.Arguments));
        plannedActions.Enqueue(planned);

        ComponentActionExecutionResult result;
        if (followUpAction.RequiresApproval &&
            !ComponentActionApprovalPolicy.IsApprovalGranted(followUpAction.ComponentId, followUpAction.ActionId))
        {
            result = new ComponentActionExecutionResult(
                followUpAction.ComponentId,
                followUpAction.ActionId,
                Succeeded: false,
                Message: $"Approval required for {followUpAction.ComponentId}.{followUpAction.ActionId}.");
        }
        else
        {
            result = await executor.ExecuteAsync(planned, cancellationToken);
        }

        executionResults.Enqueue(result);
        logger?.LogInformation(
            "Applied navigation continuation action {ComponentId}.{ActionId}.",
            followUpAction.ComponentId,
            followUpAction.ActionId);
    }

    private static bool TryInferNavigationContinuationAction(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        ConcurrentQueue<PlannedComponentAction> plannedActions,
        ConcurrentQueue<ComponentActionExecutionResult> executionResults,
        out FallbackAction action)
    {
        action = default;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var plannedSnapshot = plannedActions.ToArray();
        var executionSnapshot = executionResults.ToArray();
        var hasNavigation = HasSuccessfulNavigationAction(plannedSnapshot, executionSnapshot);
        var hasNonNavigation = HasNonNavigationAction(plannedSnapshot, executionSnapshot);

        if (hasNavigation &&
            !hasNonNavigation &&
            TryInferPostNavigationAction(
                userMessage,
                allowedComponents,
                registeredComponentSnapshots,
                out action))
        {
            return true;
        }

        // If the model invoked a destination action without navigation, add navigation so queued
        // actions can be applied when the destination surface loads.
        if (!hasNavigation &&
            hasNonNavigation &&
            TryInferNavigationForQueuedSurfaceAction(
                userMessage,
                allowedComponents,
                registeredComponentSnapshots,
                executionSnapshot,
                out action))
        {
            return true;
        }

        return false;
    }

    private static bool TryInferPostNavigationAction(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        out FallbackAction action)
    {
        if (TryInferDataGridEntityFollowUpAction(
                userMessage,
                allowedComponents,
                registeredComponentSnapshots,
                out action))
        {
            return true;
        }

        if (ContainsAny(
                userMessage,
                "high risk",
                "highest risk",
                "low risk",
                "lowest risk",
                "filter"))
        {
            return TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridFilterActionId,
                out action);
        }

        if (ContainsAny(
                userMessage,
                "sort",
                "ascending",
                "descending",
                "highest to lowest",
                "lowest to highest"))
        {
            return TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridSortActionId,
                out action);
        }

        if (ContainsAny(userMessage, "switch tab", "tab "))
        {
            if (!TryResolveAllowedAction(
                    allowedComponents,
                    AgentComponentV1CapabilityProfile.AgentTabsComponentId,
                    AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
                    out action))
            {
                return false;
            }

            if (TryParseIndexFromUserMessage(userMessage, out var tabIndex))
            {
                action = action with
                {
                    Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["index"] = tabIndex
                    }
                };
            }

            return true;
        }

        if (ContainsAny(userMessage, "open dialog", "close dialog", "confirm dialog"))
        {
            var actionId =
                ContainsAny(userMessage, "close dialog")
                    ? AgentComponentV1CapabilityProfile.DialogCloseActionId
                    : ContainsAny(userMessage, "confirm dialog")
                        ? AgentComponentV1CapabilityProfile.DialogConfirmActionId
                        : AgentComponentV1CapabilityProfile.DialogOpenActionId;
            return TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentDialogComponentId,
                actionId,
                out action);
        }

        if (ContainsAny(userMessage, "validate form", "submit form", "reset form", "set field", "update field"))
        {
            var actionId =
                ContainsAny(userMessage, "submit form")
                    ? AgentComponentV1CapabilityProfile.FormSubmitActionId
                    : ContainsAny(userMessage, "reset form")
                        ? AgentComponentV1CapabilityProfile.FormResetActionId
                        : ContainsAny(userMessage, "validate form")
                            ? AgentComponentV1CapabilityProfile.FormValidateActionId
                            : AgentComponentV1CapabilityProfile.FormSetFieldActionId;
            if (!TryResolveAllowedAction(
                    allowedComponents,
                    AgentComponentV1CapabilityProfile.AgentFormComponentId,
                    actionId,
                    out action))
            {
                return false;
            }

            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase) &&
                TryExtractFieldAssignment(userMessage, out var field, out var value))
            {
                action = action with
                {
                    Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["field"] = field,
                        ["value"] = value
                    }
                };
            }

            return true;
        }

        action = default;
        return false;
    }

    private static bool TryInferNavigationForQueuedSurfaceAction(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        IReadOnlyList<ComponentActionExecutionResult> executionSnapshot,
        out FallbackAction action)
    {
        action = default;
        if (!TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
                AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
                out var navigationAction))
        {
            return false;
        }

        if (!TryInferNavigationUri(userMessage, out var inferredUri))
        {
            return false;
        }

        var hasQueuedSurfaceAction = executionSnapshot.Any(result =>
            !string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded &&
            result.Message.Contains("Queued", StringComparison.OrdinalIgnoreCase));
        if (!hasQueuedSurfaceAction)
        {
            return false;
        }

        var hasMissingSurfaceType = executionSnapshot.Any(result =>
        {
            if (string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var componentType = ResolveComponentType(result.ComponentId);
            return componentType.Length > 0 &&
                   !string.Equals(componentType, "NavMenu", StringComparison.OrdinalIgnoreCase) &&
                   !HasRegisteredComponentType(registeredComponentSnapshots, componentType);
        });
        if (!hasMissingSurfaceType)
        {
            return false;
        }

        action = navigationAction with
        {
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["uri"] = inferredUri
            }
        };
        return true;
    }

    private static IReadOnlyDictionary<string, object?>? BuildNavigationContinuationArguments(string userMessage)
    {
        if (!TryInferNavigationUri(userMessage, out var uri))
        {
            return null;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["uri"] = uri
        };
    }

    private static bool TryInferDataGridEntityFollowUpAction(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        out FallbackAction action)
    {
        action = default;
        if (!TryExtractEntityKeyToken(userMessage, out var entityKey))
        {
            return false;
        }

        if (TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridFilterActionId,
                out var filterAction))
        {
            action = filterAction with
            {
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["column"] = InferDataGridIdentityColumn(registeredComponentSnapshots, userMessage),
                    ["operator"] = "eq",
                    ["value"] = entityKey
                }
            };
            return true;
        }

        if (TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridSelectRowActionId,
                out var selectAction))
        {
            action = selectAction with
            {
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rowKey"] = entityKey
                }
            };
            return true;
        }

        if (TryResolveAllowedAction(
                allowedComponents,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId,
                out var navigateToRowAction))
        {
            action = navigateToRowAction with
            {
                Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rowKey"] = entityKey
                }
            };
            return true;
        }

        return false;
    }

    private static string InferDataGridIdentityColumn(
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        string userMessage)
    {
        if (!TryGetDataGridColumns(registeredComponentSnapshots, out var columns) || columns.Length == 0)
        {
            return "Id";
        }

        var subjectTokens = ExtractSubjectTokens(userMessage);
        var scored = columns
            .Select(column => (column, score: ScoreIdentityColumn(column, subjectTokens)))
            .OrderByDescending(static candidate => candidate.score)
            .ThenBy(static candidate => candidate.column, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return scored[0].score > 0 ? scored[0].column : columns[0];
    }

    private static int ScoreIdentityColumn(string column, IReadOnlyCollection<string> subjectTokens)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return 0;
        }

        var normalized = column.Trim().ToLowerInvariant();
        var score = 0;
        if (string.Equals(normalized, "id", StringComparison.OrdinalIgnoreCase))
        {
            score += 12;
        }

        if (normalized.EndsWith("id", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        if (normalized.Contains("key", StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        if (normalized.Contains("code", StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        if (normalized.Contains("number", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("num", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (normalized.Contains("identifier", StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        foreach (var token in subjectTokens)
        {
            if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
        }

        return score;
    }

    private static bool TryInferNavigationUri(string userMessage, out string uri)
    {
        uri = string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var explicitUriMatch = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"https?://[^\s""']+|/[a-zA-Z0-9_/\-]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (explicitUriMatch.Success)
        {
            uri = explicitUriMatch.Value;
            return true;
        }

        if (ContainsAny(userMessage, "home", "dashboard", "landing", "start page", "main page"))
        {
            uri = "/";
            return true;
        }

        if (!TryExtractRouteTokens(userMessage, out var routeTokens, out var hadEntityToken))
        {
            return false;
        }

        if (routeTokens.Length == 1 &&
            (hadEntityToken || ContainsAny(userMessage, "show me", "show all", "list", "all ")) &&
            !routeTokens[0].EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            routeTokens[0] = $"{routeTokens[0]}s";
        }

        uri = "/" + string.Join("-", routeTokens);
        return true;
    }

    private static bool TryExtractRouteTokens(
        string userMessage,
        out string[] routeTokens,
        out bool hadEntityToken)
    {
        routeTokens = [];
        hadEntityToken = false;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var targetSegment = userMessage;
        var targetMatch = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"\b(?:go|navigate|open|show|take|bring)(?:\s+me)?(?:\s+to)?\s+(?<target>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (targetMatch.Success)
        {
            targetSegment = targetMatch.Groups["target"].Value;
        }

        var boundaryMatch = System.Text.RegularExpressions.Regex.Match(
            targetSegment,
            @"\b(and|then|with|where|that|which|filter|filtered|sort|sorted|by)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (boundaryMatch.Success && boundaryMatch.Index > 0)
        {
            targetSegment = targetSegment[..boundaryMatch.Index];
        }

        var candidates = System.Text.RegularExpressions.Regex.Matches(
                targetSegment,
                @"[A-Za-z0-9][A-Za-z0-9_-]*",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Select(static match => match.Value.Trim())
            .Where(static token => token.Length > 0)
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "go", "to", "open", "show", "navigate", "take", "bring", "me", "the", "a", "an", "my", "all",
            "please", "page", "screen", "section", "view", "highest", "lowest", "high", "low",
            "risk", "filtered", "filter", "sorted", "sort", "by"
        };

        var route = new List<string>();
        foreach (var token in candidates)
        {
            if (stopWords.Contains(token))
            {
                continue;
            }

            if (IsPotentialEntityKey(token))
            {
                hadEntityToken = true;
                continue;
            }

            var sanitized = new string(token.Where(static ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray())
                .Trim('-')
                .ToLowerInvariant();
            if (sanitized.Length == 0)
            {
                continue;
            }

            route.Add(sanitized);
        }

        if (route.Count == 0)
        {
            return false;
        }

        routeTokens = route.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return routeTokens.Length > 0;
    }

    private static bool TryExtractEntityKeyToken(string text, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"\b[A-Za-z]{2,}[-_]\d{1,}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(
                text,
                @"\b[A-Za-z]{1,}\d{3,}\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        if (!match.Success)
        {
            return false;
        }

        token = match.Value.Trim();
        return token.Length > 0;
    }

    private static bool IsPotentialEntityKey(string token)
        => System.Text.RegularExpressions.Regex.IsMatch(
            token,
            @"^[A-Za-z]{2,}[-_]\d+$|^[A-Za-z]+\d{3,}$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool TryParseIndexFromUserMessage(string userMessage, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var ordinalMatch = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"\b(first|second|third|fourth|fifth)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (ordinalMatch.Success)
        {
            index = ordinalMatch.Groups[1].Value.ToLowerInvariant() switch
            {
                "first" => 0,
                "second" => 1,
                "third" => 2,
                "fourth" => 3,
                "fifth" => 4,
                _ => 0
            };
            return true;
        }

        var numericMatch = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"\b(?<index>\d+)(?:st|nd|rd|th)?\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (numericMatch.Success &&
            int.TryParse(numericMatch.Groups["index"].Value, out var parsed))
        {
            index = Math.Max(0, parsed);
            return true;
        }

        return false;
    }

    private static bool TryExtractFieldAssignment(
        string userMessage,
        out string field,
        out object? value)
    {
        field = string.Empty;
        value = null;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"\b(?:set|update|change)\s+(?<field>[a-zA-Z][a-zA-Z0-9_\s-]{0,64})\s+(?:to|as)\s+(?<value>.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var fieldRaw = match.Groups["field"].Value.Trim();
        var valueRaw = match.Groups["value"].Value.Trim().Trim('"', '\'');
        if (fieldRaw.Length == 0 || valueRaw.Length == 0)
        {
            return false;
        }

        field = ToPascalIdentifier(fieldRaw);
        value = ParseScalarValue(valueRaw);
        return field.Length > 0;
    }

    private static object ParseScalarValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        if (double.TryParse(value, out var doubleValue))
        {
            return doubleValue;
        }

        return value;
    }

    private static string ToPascalIdentifier(string raw)
    {
        var tokens = System.Text.RegularExpressions.Regex.Matches(raw, @"[A-Za-z0-9]+")
            .Select(static match => match.Value)
            .Where(static token => token.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(tokens.Select(static token =>
            token.Length == 1
                ? token.ToUpperInvariant()
                : char.ToUpperInvariant(token[0]) + token[1..]));
    }

    private static IReadOnlyCollection<string> ExtractSubjectTokens(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return Array.Empty<string>();
        }

        var tokens = System.Text.RegularExpressions.Regex.Matches(userMessage, @"[A-Za-z]{3,}")
            .Select(static match => match.Value.ToLowerInvariant())
            .Where(static token => token is not ("show" or "filter" or "sorted" or "sort" or "navigate" or "open" or "close" or "set" or "update" or "form" or "tab"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return tokens;
    }

    private static bool HasSuccessfulNavigationAction(
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults)
    {
        return executionResults.Any(static result =>
            result.Succeeded &&
            string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase)) ||
            plannedActions.Any(static action =>
                string.Equals(action.ComponentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNonNavigationAction(
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults)
    {
        return executionResults.Any(static result =>
                   !string.Equals(result.ComponentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase)) ||
               plannedActions.Any(static action =>
                   !string.Equals(action.ComponentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasQueuedExecutionResultForComponent(
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        string componentId)
    {
        return executionResults.Any(result =>
            string.Equals(result.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
            result.Succeeded &&
            result.Message.Contains("Queued", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasRegisteredComponentType(
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        string componentType)
        => registeredComponentSnapshots.Any(snapshot =>
            string.Equals(snapshot.ComponentType, componentType, StringComparison.OrdinalIgnoreCase));

    private static bool TryResolveAllowedAction(
        IReadOnlyList<ComponentCapability> allowedComponents,
        string componentId,
        string actionId,
        out FallbackAction action)
    {
        action = default;
        var component = allowedComponents.FirstOrDefault(candidate =>
            string.Equals(candidate.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));
        if (component is null)
        {
            return false;
        }

        var allowedAction = component.Actions.FirstOrDefault(candidate =>
            string.Equals(candidate.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        if (allowedAction is null)
        {
            return false;
        }

        action = new FallbackAction(componentId, actionId, allowedAction.RequiresApproval);
        return true;
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

    private readonly record struct StructuredToolDirective(
        string Name,
        IReadOnlyDictionary<string, object?>? Arguments);

    private readonly record struct FallbackAction(
        string ComponentId,
        string ActionId,
        bool RequiresApproval,
        IReadOnlyDictionary<string, object?>? Arguments = null);

    private static string BuildToolDescription(ComponentCapability component, ComponentActionCapability action)
    {
        var suffix = action.RequiresApproval
            ? "This action requires approval before execution in production."
            : "Execute this UI action.";
        var requiredArgumentsHint = action.ActionId switch
        {
            AgentComponentV1CapabilityProfile.NavigationNavigateToActionId =>
                " Required args: provide one of uri, url, or target (for example uri='/suppliers').",
            AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId =>
                " Required args: provide one of uri, url, or target.",
            AgentComponentV1CapabilityProfile.DataGridFilterActionId =>
                " Required args: column, operator, and value.",
            AgentComponentV1CapabilityProfile.DataGridSortActionId =>
                " Required args: column. Optional: direction ('asc' or 'desc').",
            AgentComponentV1CapabilityProfile.TabsSwitchTabActionId =>
                " Required args: index.",
            AgentComponentV1CapabilityProfile.FormSetFieldActionId =>
                " Required args: field and value.",
            _ => string.Empty
        };
        return $"{component.ComponentId}.{action.ActionId}: {action.Description}{requiredArgumentsHint} {suffix}";
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
        IReadOnlyDictionary<string, object?>? seedArguments)
    {
        var args = seedArguments is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(seedArguments, StringComparer.OrdinalIgnoreCase);
        AddIfNotNullOrWhiteSpace(args, "intent", userMessage);
        AddComponentStateHints(args, componentId, actionId, registeredComponentSnapshots);
        AddInferredArguments(args, componentId, actionId, userMessage, registeredComponentSnapshots);
        return args;
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
        AddInferredArguments(args, componentId, actionId, userMessage, registeredComponentSnapshots);
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
        if (!TryResolveComponentSnapshot(componentId, actionId, registeredComponentSnapshots, out var snapshot))
        {
            return;
        }

        AddIfNotNullOrWhiteSpace(arguments, "agentId", snapshot.AgentId);
        var state = snapshot.State;

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

    private static void AddInferredArguments(
        IDictionary<string, object?> arguments,
        string componentId,
        string actionId,
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots)
    {
        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
                !arguments.ContainsKey("direction"))
            {
                var inferredDirection = InferSortDirection(userMessage);
                if (!string.IsNullOrWhiteSpace(inferredDirection))
                {
                    arguments["direction"] = inferredDirection;
                }
            }

            if ((string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSelectRowActionId, StringComparison.OrdinalIgnoreCase)) &&
                !arguments.ContainsKey("rowKey") &&
                TryExtractEntityKeyToken(userMessage, out var rowKey))
            {
                arguments["rowKey"] = rowKey;
            }

            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase) &&
                TryExtractEntityKeyToken(userMessage, out var filterEntityKey))
            {
                AddIfMissing(arguments, "operator", "eq");
                AddIfMissing(arguments, "value", filterEntityKey);
                if (!arguments.ContainsKey("column"))
                {
                    arguments["column"] = InferDataGridIdentityColumn(registeredComponentSnapshots, userMessage);
                }
            }

            return;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase)) &&
            !HasAnyKey(arguments, "uri", "url", "target") &&
            TryInferNavigationUri(userMessage, out var inferredUri))
        {
            arguments["uri"] = inferredUri;
            return;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase) &&
            !arguments.ContainsKey("index") &&
            TryParseIndexFromUserMessage(userMessage, out var tabIndex))
        {
            arguments["index"] = tabIndex;
            return;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase) &&
            TryExtractFieldAssignment(userMessage, out var field, out var value))
        {
            AddIfMissing(arguments, "field", field);
            AddIfMissing(arguments, "value", value);
        }
    }

    private static bool TryResolveComponentSnapshot(
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        out RegisteredComponentSnapshot snapshot)
    {
        snapshot = default!;
        var componentType = ResolveComponentType(componentId);
        if (componentType.Length == 0 || registeredComponentSnapshots.Count == 0)
        {
            return false;
        }

        foreach (var candidate in registeredComponentSnapshots
                     .OrderBy(static snapshot => snapshot.AgentId, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(candidate.ComponentType, componentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!candidate.Actions.Contains(actionId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            snapshot = candidate;
            return true;
        }

        foreach (var candidate in registeredComponentSnapshots
                     .OrderBy(static snapshot => snapshot.AgentId, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(candidate.ComponentType, componentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            snapshot = candidate;
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
        return guidance;
    }

    private static string? BuildFailureGuidance(
        string userMessage,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        for (var i = 0; i < executionResults.Count; i++)
        {
            var result = executionResults[i];
            if (result.Succeeded || HasSubsequentSuccessfulExecution(executionResults, i))
            {
                continue;
            }

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
                return suggestion;
            }
        }

        return null;
    }

    private static bool HasSubsequentSuccessfulExecution(
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        int failedIndex)
    {
        if (failedIndex < 0 || failedIndex >= executionResults.Count)
        {
            return false;
        }

        var failed = executionResults[failedIndex];
        for (var i = failedIndex + 1; i < executionResults.Count; i++)
        {
            var current = executionResults[i];
            if (current.Succeeded &&
                string.Equals(current.ComponentId, failed.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.ActionId, failed.ActionId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractMissingParameter(string? message, out string parameter)
    {
        parameter = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var requiresIndex = message.IndexOf("requires", StringComparison.OrdinalIgnoreCase);
        var parameterIndex = message.IndexOf("parameter", StringComparison.OrdinalIgnoreCase);
        if (requiresIndex >= 0 && parameterIndex > requiresIndex)
        {
            var segment = message[requiresIndex..parameterIndex];
            var quotedMatches = System.Text.RegularExpressions.Regex.Matches(
                segment,
                "'(?<param>[^']+)'",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (quotedMatches.Count > 0)
            {
                var candidates = quotedMatches
                    .Select(static match => match.Groups["param"].Value.Trim())
                    .Where(static value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (candidates.Length > 0)
                {
                    parameter = candidates
                        .FirstOrDefault(static candidate =>
                            candidate.Equals("uri", StringComparison.OrdinalIgnoreCase))
                        ?? candidates[0];
                    return true;
                }
            }
        }

        var unquotedMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            "requires\\s+(?<param>[a-zA-Z][a-zA-Z0-9_-]*)\\s+parameter",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!unquotedMatch.Success)
        {
            return false;
        }

        parameter = unquotedMatch.Groups["param"].Value.Trim();
        return parameter.Length > 0;
    }

    private static string BuildMissingParameterSuggestion(
        string userMessage,
        string componentId,
        string actionId,
        string parameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var columnExamples = BuildDataGridColumnExamples(registeredComponents);

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

            return columnExamples.Length > 0
                ? $"Which column should I sort by (for example {columnExamples})?"
                : "Which column should I sort by?";
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
        {
            return parameter.ToLowerInvariant() switch
            {
                "column" => columnExamples.Length > 0
                    ? $"Which column should I filter (for example {columnExamples})?"
                    : "Which column should I filter?",
                "operator" => "Which filter operator should I use (for example >=, <=, eq, contains)?",
                "value" => "What value should I filter by?",
                _ => $"What should I use for '{parameter}' in the requested filter?"
            };
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            parameter is not null &&
            (parameter.Equals("uri", StringComparison.OrdinalIgnoreCase) ||
             parameter.Equals("url", StringComparison.OrdinalIgnoreCase) ||
             parameter.Equals("target", StringComparison.OrdinalIgnoreCase)))
        {
            if (TryInferNavigationUri(userMessage, out var inferredUri))
            {
                return $"Did you mean navigate to '{inferredUri}'?";
            }

            return "Which route should I navigate to (for example /suppliers or /settings)?";
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parameter, "index", StringComparison.OrdinalIgnoreCase))
        {
            return "Which tab should I switch to (for example first, second, or an explicit index)?";
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            var parameterKey = parameter?.ToLowerInvariant() ?? string.Empty;
            return parameterKey switch
            {
                "field" => "Which form field should I set?",
                "value" => "What value should I set for that field?",
                _ => $"What should I use for '{parameter}'?"
            };
        }

        return $"What should I use for '{parameter}'?";
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

        if (TryGetDataGridColumns(registeredComponents, out var columns) &&
            columns.Length > 0)
        {
            column = columns[0];
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

    private static bool TryGetDataGridColumns(
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        out string[] columns)
    {
        columns = [];
        foreach (var component in registeredComponents)
        {
            if (!string.Equals(component.ComponentType, "DataGrid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!component.State.TryGetValue("columns", out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(raw);
                if (parsed is { Length: > 0 })
                {
                    columns = parsed
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return columns.Length > 0;
                }
            }
            catch
            {
                // Fall back to CSV-like parsing below.
            }

            columns = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static value => value.Trim('"'))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (columns.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildDataGridColumnExamples(IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        if (!TryGetDataGridColumns(registeredComponents, out var columns) || columns.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", columns.Take(4));
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
                pending.Prompt,
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
        for (var i = 0; i < executionResults.Count; i++)
        {
            var result = executionResults[i];
            if (result.Succeeded || HasSubsequentSuccessfulExecution(executionResults, i))
            {
                continue;
            }

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

    private async Task TryExecuteMissingParameterRecoveryAsync(
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        ConcurrentQueue<PlannedComponentAction> plannedActions,
        ConcurrentQueue<ComponentActionExecutionResult> executionResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        var executionSnapshot = executionResults.ToArray();
        if (executionSnapshot.Length == 0)
        {
            return;
        }

        var plannedSnapshot = plannedActions.ToArray();
        var recoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < executionSnapshot.Length; i++)
        {
            var failed = executionSnapshot[i];
            if (failed.Succeeded || HasSubsequentSuccessfulExecution(executionSnapshot, i))
            {
                continue;
            }

            if (!TryExtractMissingParameter(failed.Message, out var missingParameter))
            {
                continue;
            }

            var recoveryKey = $"{failed.ComponentId}:{failed.ActionId}:{missingParameter}";
            if (!recoveredKeys.Add(recoveryKey))
            {
                continue;
            }

            var failedPlan = plannedSnapshot.LastOrDefault(action =>
                string.Equals(action.ComponentId, failed.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action.ActionId, failed.ActionId, StringComparison.OrdinalIgnoreCase));
            if (failedPlan is null)
            {
                continue;
            }

            if (!TryBuildRecoveredArgumentsForMissingParameter(
                    userMessage,
                    failedPlan,
                    missingParameter,
                    registeredComponentSnapshots,
                    out var recoveredArguments))
            {
                continue;
            }

            var recoveryPlan = new PlannedComponentAction(
                failedPlan.ComponentId,
                failedPlan.ActionId,
                $"{failedPlan.Reason} (missing-parameter recovery)",
                recoveredArguments);
            plannedActions.Enqueue(recoveryPlan);

            var recoveryResult = await executor.ExecuteAsync(recoveryPlan, cancellationToken);
            executionResults.Enqueue(recoveryResult);
        }
    }

    private static bool TryBuildRecoveredArgumentsForMissingParameter(
        string userMessage,
        PlannedComponentAction failedPlan,
        string missingParameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots,
        out IReadOnlyDictionary<string, object?> recoveredArguments)
    {
        recoveredArguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var args = failedPlan.Arguments is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(failedPlan.Arguments, StringComparer.OrdinalIgnoreCase);

        if (!args.ContainsKey("column") &&
            TryGetFirstString(args, out var aliasColumn, "field", "property") &&
            !string.IsNullOrWhiteSpace(aliasColumn))
        {
            args["column"] = aliasColumn;
        }

        if (!args.ContainsKey("operator") &&
            TryGetFirstString(args, out var aliasOperator, "op", "comparison") &&
            !string.IsNullOrWhiteSpace(aliasOperator))
        {
            args["operator"] = aliasOperator;
        }

        if (!args.ContainsKey("value") &&
            TryGetFirstValue(args, out var aliasValue, "fieldValue", "threshold", "query", "term"))
        {
            args["value"] = aliasValue;
        }

        if (string.Equals(
                failedPlan.ComponentId,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                failedPlan.ActionId,
                AgentComponentV1CapabilityProfile.DataGridFilterActionId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(missingParameter, "operator", StringComparison.OrdinalIgnoreCase) &&
            !args.ContainsKey("operator") &&
            args.ContainsKey("value"))
        {
            args["operator"] = "contains";
        }

        if (string.Equals(
                failedPlan.ComponentId,
                AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                failedPlan.ActionId,
                AgentComponentV1CapabilityProfile.NavigationNavigateToActionId,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!args.ContainsKey("uri") &&
                TryGetFirstString(args, out var aliasUri, "url", "target", "route", "path", "destination") &&
                !string.IsNullOrWhiteSpace(aliasUri))
            {
                args["uri"] = aliasUri;
            }

            if (!args.ContainsKey("uri") &&
                TryInferNavigationUri(userMessage, out var inferredUri))
            {
                args["uri"] = inferredUri;
            }
        }

        if (string.Equals(
                failedPlan.ComponentId,
                AgentComponentV1CapabilityProfile.AgentNavMenuComponentId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                failedPlan.ActionId,
                AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!args.ContainsKey("uri") &&
                TryGetFirstString(args, out var aliasUri, "url", "target", "route", "path", "destination") &&
                !string.IsNullOrWhiteSpace(aliasUri))
            {
                args["uri"] = aliasUri;
            }

            if (!args.ContainsKey("uri") &&
                TryInferNavigationUri(userMessage, out var inferredUri))
            {
                args["uri"] = inferredUri;
            }
        }

        if (string.Equals(
                failedPlan.ComponentId,
                AgentComponentV1CapabilityProfile.AgentDataGridComponentId,
                StringComparison.OrdinalIgnoreCase))
        {
            if ((string.Equals(
                     failedPlan.ActionId,
                     AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     failedPlan.ActionId,
                     AgentComponentV1CapabilityProfile.DataGridSelectRowActionId,
                     StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(missingParameter, "rowKey", StringComparison.OrdinalIgnoreCase) &&
                !args.ContainsKey("rowKey") &&
                TryExtractEntityKeyToken(userMessage, out var rowKey))
            {
                args["rowKey"] = rowKey;
            }

            if (string.Equals(
                    failedPlan.ActionId,
                    AgentComponentV1CapabilityProfile.DataGridFilterActionId,
                    StringComparison.OrdinalIgnoreCase) &&
                TryExtractEntityKeyToken(userMessage, out var filterEntity))
            {
                if (string.Equals(missingParameter, "column", StringComparison.OrdinalIgnoreCase) && !args.ContainsKey("column"))
                {
                    args["column"] = InferDataGridIdentityColumn(registeredComponentSnapshots, userMessage);
                }

                if (string.Equals(missingParameter, "operator", StringComparison.OrdinalIgnoreCase) && !args.ContainsKey("operator"))
                {
                    args["operator"] = "eq";
                }

                if (string.Equals(missingParameter, "value", StringComparison.OrdinalIgnoreCase) && !args.ContainsKey("value"))
                {
                    args["value"] = filterEntity;
                }
            }
        }

        if (string.Equals(
                failedPlan.ComponentId,
                AgentComponentV1CapabilityProfile.AgentTabsComponentId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                failedPlan.ActionId,
                AgentComponentV1CapabilityProfile.TabsSwitchTabActionId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(missingParameter, "index", StringComparison.OrdinalIgnoreCase) &&
            !args.ContainsKey("index") &&
            TryParseIndexFromUserMessage(userMessage, out var tabIndex))
        {
            args["index"] = tabIndex;
        }

        if (string.Equals(
                failedPlan.ComponentId,
                AgentComponentV1CapabilityProfile.AgentFormComponentId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                failedPlan.ActionId,
                AgentComponentV1CapabilityProfile.FormSetFieldActionId,
                StringComparison.OrdinalIgnoreCase) &&
            TryExtractFieldAssignment(userMessage, out var field, out var value))
        {
            if (string.Equals(missingParameter, "field", StringComparison.OrdinalIgnoreCase) &&
                !args.ContainsKey("field"))
            {
                args["field"] = field;
            }

            if (string.Equals(missingParameter, "value", StringComparison.OrdinalIgnoreCase) &&
                !args.ContainsKey("value"))
            {
                args["value"] = value;
            }
        }

        if (HasRequiredParameter(args, missingParameter))
        {
            recoveredArguments = args;
            return true;
        }

        return false;
    }

    private static bool TryGetFirstString(
        IReadOnlyDictionary<string, object?> arguments,
        out string? value,
        params string[] keys)
    {
        value = null;
        foreach (var key in keys)
        {
            if (!arguments.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            var text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            value = text;
            return true;
        }

        return false;
    }

    private static bool TryGetFirstValue(
        IReadOnlyDictionary<string, object?> arguments,
        out object? value,
        params string[] keys)
    {
        value = null;
        foreach (var key in keys)
        {
            if (arguments.TryGetValue(key, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRequiredParameter(
        IReadOnlyDictionary<string, object?> arguments,
        string parameter)
    {
        if (!arguments.TryGetValue(parameter, out var raw))
        {
            return false;
        }

        return raw switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };
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

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(missingParameter, "uri", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(missingParameter, "url", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(missingParameter, "target", StringComparison.OrdinalIgnoreCase)) &&
            TryInferNavigationUri(userMessage, out var inferredUri))
        {
            args["uri"] = inferredUri;
            return args;
        }

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(missingParameter, "index", StringComparison.OrdinalIgnoreCase) &&
            TryParseIndexFromUserMessage(userMessage, out var tabIndex))
        {
            args["index"] = tabIndex;
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
            var fallback = userReply.Trim().Trim('"', '\'');
            if (fallback.Length > 0 && fallback.Length <= 128)
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

    private static void AddIfMissing(
        IDictionary<string, object?> dictionary,
        string key,
        object? value)
    {
        if (!dictionary.ContainsKey(key) && value is not null)
        {
            dictionary[key] = value;
        }
    }

    private static bool HasAnyKey(IDictionary<string, object?> dictionary, params string[] keys)
        => keys.Any(dictionary.ContainsKey);

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

    private async Task StoreConversationTurnAsync(
        string sessionId,
        string userMessage,
        string agentResponse,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        CancellationToken cancellationToken)
    {
        if (conversationManager is null)
        {
            return;
        }

        try
        {
            var turn = new ConversationTurn
            {
                Timestamp = DateTime.UtcNow,
                UserMessage = userMessage,
                AgentResponse = agentResponse,
                PlannedActions = plannedActions,
                ExecutionResults = executionResults
            };

            await conversationManager.AppendTurnAsync(sessionId, turn, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to store conversation turn for session {SessionId}", sessionId);
        }
    }

    private async Task RecordActionsForPreferencesAsync(
        string sessionId,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        CancellationToken cancellationToken)
    {
        if (userPreferenceService is null)
        {
            return;
        }

        try
        {
            foreach (var result in executionResults.Where(r => r.Succeeded))
            {
                await userPreferenceService.RecordActionAsync(
                    sessionId,
                    result.ComponentId,
                    result.ActionId,
                    arguments: null, // Arguments not currently tracked in execution results
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to record actions for preferences in session {SessionId}", sessionId);
        }
    }

}
