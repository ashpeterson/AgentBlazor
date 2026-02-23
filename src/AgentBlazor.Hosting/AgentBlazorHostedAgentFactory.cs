using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Hosting;

internal sealed class AgentBlazorHostedAgentFactory(
    IServiceProvider services,
    IAgentRegistry agentRegistry,
    IComponentCapabilityCatalog componentCatalog,
    IAgentComponentRegistry? componentRegistry,
    IOptions<AgentBlazorOptions> options,
    IComponentActionExecutor executor,
    ILogger<AgentBlazorHostedAgentFactory>? logger = null,
    IChatClient? chatClient = null,
    IAgentBlazorEntitlementService? entitlementService = null)
{
    public bool IsProviderConfigured => chatClient is not null;

    public string? CurrentTier => entitlementService?.CurrentTier.ToString();

    public AIAgent CreateDefaultAgent()
    {
        var registration = ResolveAgent();
        var policyEvaluation = ResolveAllowedComponents(registration);
        if (policyEvaluation.BlockedActionKeys.Count > 0)
        {
            logger?.LogInformation(
                "Hosted agent policy filtered {BlockedActionCount} component actions for {AgentName}: {BlockedActions}",
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
                "Hosted agent tier {Tier} filtered {BlockedActionCount} component actions for {AgentName}: {BlockedActions}",
                entitlementService.CurrentTier,
                entitlementEvaluation.BlockedActionKeys.Count,
                registration.Name,
                ComponentActionPolicy.SummarizeBlockedActions(entitlementEvaluation.BlockedActionKeys));
        }

        if (!entitlementEvaluation.HasAllowedActions)
        {
            logger?.LogWarning(
                "Hosted agent {AgentName} has no allowed component actions after policy and tier filtering.",
                registration.Name);
        }

        var allowedComponents = entitlementEvaluation.AllowedComponents;
        var registeredComponents = RegisteredComponentSnapshotBuilder.Build(componentRegistry);
        var tools = BuildTools(registration, allowedComponents, registeredComponents);

        return new ChatClientAgent(
            chatClient ?? new NoProviderChatClient(),
            new ChatClientAgentOptions
            {
                Name = registration.Name,
                Description = registration.Description,
                ChatOptions = new ChatOptions
                {
                    Instructions = BuildInstructions(registration, policyEvaluation, entitlementEvaluation, entitlementService, registeredComponents),
                    Tools = tools,
                    ToolMode = ChatToolMode.Auto
                }
            },
            services: services);
    }

    private AgentRegistration ResolveAgent()
    {
        if (agentRegistry.TryGet(options.Value.DefaultAgent.Name, out var configuredDefault))
        {
            return configuredDefault;
        }

        return agentRegistry.GetAll()
            .OrderBy(static agent => agent.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? new AgentRegistration
            {
                Name = "AgentBlazor UI Agent",
                Description = "Fallback AgentBlazor hosted AG-UI agent."
            };
    }

    private ComponentActionPolicyEvaluation ResolveAllowedComponents(AgentRegistration registration)
        => ComponentActionPolicy.EvaluateAllowedCapabilities(
            componentCatalog.GetComponents(),
            registration.AllowedComponents,
            registration.AllowedActions);

    private List<AITool> BuildTools(
        AgentRegistration registration,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponentSnapshots)
    {
        var tools = new List<AITool>();
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in allowedComponents)
        {
            foreach (var action in component.Actions)
            {
                var componentId = component.ComponentId;
                var actionId = action.ActionId;
                var reason = "Selected by the hosted framework AG-UI agent tool invocation.";
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
                    CancellationToken cancellationToken = default)
                {
                    if (action.RequiresApproval && !ComponentActionApprovalPolicy.IsApprovalGranted(componentId, actionId))
                    {
                        return $"Approval required for {componentId}.{actionId}.";
                    }

                    var arguments = BuildToolArguments(
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
                    AddComponentStateHints(arguments, componentId, actionId, registeredComponentSnapshots);
                    var planned = new PlannedComponentAction(
                        componentId,
                        actionId,
                        reason,
                        arguments.Count == 0 ? null : arguments);
                    var result = await executor.ExecuteAsync(planned, cancellationToken);
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

    private static string BuildInstructions(
        AgentRegistration registration,
        ComponentActionPolicyEvaluation policyEvaluation,
        ComponentActionPolicyEvaluation entitlementEvaluation,
        IAgentBlazorEntitlementService? entitlementService,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var builder = new StringBuilder();
        var allowedComponents = entitlementEvaluation.AllowedComponents;
        var blockedActionKeys = policyEvaluation.BlockedActionKeys
            .Concat(entitlementEvaluation.BlockedActionKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

        if (!entitlementEvaluation.HasAllowedActions)
        {
            builder.AppendLine("Policy note: no component actions are currently allowed for this agent.");
            builder.AppendLine("Ask the operator to adjust AllowedComponents/AllowedActions and licensing tier before attempting UI actions.");
        }
        else if (blockedActionKeys.Length > 0)
        {
            if (entitlementService is not null && entitlementEvaluation.BlockedActionKeys.Count > 0)
            {
                builder.Append("Tier note: '")
                    .Append(entitlementService.CurrentTier)
                    .Append("' filtered actions: ")
                    .Append(ComponentActionPolicy.SummarizeBlockedActions(entitlementEvaluation.BlockedActionKeys))
                    .AppendLine();
            }

            if (policyEvaluation.BlockedActionKeys.Count > 0)
            {
                builder.Append("Policy note: disallowed actions were filtered: ")
                    .Append(ComponentActionPolicy.SummarizeBlockedActions(policyEvaluation.BlockedActionKeys))
                    .AppendLine();
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildComponentToolName(string componentId, string actionId) =>
        SanitizeToolName($"agentblazor_{componentId}_{actionId}");

    private static string BuildAssemblyToolName(Type type, MethodInfo method) =>
        SanitizeToolName($"agentblazor_external_{type.Name}_{method.Name}");

    private static string SanitizeToolName(string value)
    {
        var chars = value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        return new string(chars);
    }

    private static string BuildToolDescription(ComponentCapability component, ComponentActionCapability action)
    {
        var suffix = action.RequiresApproval
            ? "This action requires approval before execution in production."
            : "Execute this UI action.";
        return $"{component.ComponentId}.{action.ActionId}: {action.Description} {suffix}";
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

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            AddStateHint(arguments, state, "sortColumn", "currentSortColumn");
            AddStateHint(arguments, state, "filterColumn", "currentFilterColumn");
            AddStateHint(arguments, state, "currentPageIndex", "currentPageIndex");
            AddStateHint(arguments, state, "pageSize", "currentPageSize");
            AddStateHint(arguments, state, "focusedRowKey", "currentRowKey");
            return;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase))
        {
            AddStateHint(arguments, state, "activePanelIndex", "currentIndex");
            return;
        }

        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
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
                     .OrderBy(static registered => registered.AgentId, StringComparer.OrdinalIgnoreCase))
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
                     .OrderBy(static registered => registered.AgentId, StringComparer.OrdinalIgnoreCase))
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
            AgentComponentCapabilityProfile.AgentDataGridComponentId => "DataGrid",
            AgentComponentCapabilityProfile.AgentDialogComponentId => "Dialog",
            AgentComponentCapabilityProfile.AgentFormComponentId => "Form",
            AgentComponentCapabilityProfile.AgentNavMenuComponentId => "NavMenu",
            AgentComponentCapabilityProfile.AgentTabsComponentId => "Tabs",
            _ => string.Empty
        };

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

    private sealed class NoProviderChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant,
                    "No provider is configured. Register an AgentBlazor provider chat client.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            _ = cancellationToken;
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                "No provider is configured. Register an AgentBlazor provider chat client.");
            await Task.CompletedTask;
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
