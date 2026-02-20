using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Preferences;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Plans and builds component actions from user intents and resolved actions.
/// </summary>
internal sealed class ActionPlanner : IActionPlanner
{
    private readonly IIntentResolver _intentResolver;
    private readonly ILogger<ActionPlanner>? _logger;

    public ActionPlanner(
        IIntentResolver intentResolver,
        ILogger<ActionPlanner>? logger = null)
    {
        _intentResolver = intentResolver ?? throw new ArgumentNullException(nameof(intentResolver));
        _logger = logger;
    }

    public IReadOnlyList<AITool> BuildTools(IReadOnlyList<ComponentCapability> allowedComponents)
    {
        var tools = new List<AITool>();
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in allowedComponents)
        {
            foreach (var action in component.Actions)
            {
                var toolName = BuildComponentToolName(component.ComponentId, action.ActionId);
                if (!knownNames.Add(toolName))
                {
                    continue;
                }

                var description = BuildToolDescription(component, action);
                var parameters = BuildToolParameters(component.ComponentId, action.ActionId);

                // Create a simple tool definition for metadata purposes
                // Actual execution is handled by the runtime
                tools.Add(AIFunctionFactory.Create(
                    () => $"Tool {toolName} executed",
                    new AIFunctionFactoryOptions
                    {
                        Name = toolName,
                        Description = description
                    }));
            }
        }

        return tools;
    }

    public PlannedComponentAction PlanAction(
        ResolvedIntent resolvedIntent,
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        UserPreferences? preferences = null)
    {
        var arguments = BuildArgumentsWithContext(
            userMessage,
            resolvedIntent.ComponentId,
            resolvedIntent.ActionId,
            registeredComponents,
            resolvedIntent.Arguments);

        return new PlannedComponentAction(
            resolvedIntent.ComponentId,
            resolvedIntent.ActionId,
            resolvedIntent.Reason.Length > 0 ? resolvedIntent.Reason : BuildToolInvocationReason(userMessage),
            arguments.Count > 0 ? arguments : null);
    }

    public IReadOnlyDictionary<string, object?> BuildArgumentsWithContext(
        string userMessage,
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        IReadOnlyDictionary<string, object?>? seedArguments = null)
    {
        var args = seedArguments is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(seedArguments, StringComparer.OrdinalIgnoreCase);

        AddIfNotNullOrWhiteSpace(args, "intent", userMessage);
        AddComponentStateHints(args, componentId, actionId, registeredComponents);
        AddInferredArguments(args, componentId, actionId, userMessage, registeredComponents);

        return args;
    }

    public void AddInferredArguments(
        IDictionary<string, object?> args,
        string componentId,
        string actionId,
        string userMessage,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        // DataGrid actions
        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            // Sort direction inference
            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
                !args.ContainsKey("direction"))
            {
                var inferredDirection = InferSortDirection(userMessage);
                if (!string.IsNullOrWhiteSpace(inferredDirection))
                {
                    args["direction"] = inferredDirection;
                }
            }

            // Row key extraction for navigation/selection
            if ((string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridNavigateToRowActionId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSelectRowActionId, StringComparison.OrdinalIgnoreCase)) &&
                !args.ContainsKey("rowKey") &&
                _intentResolver.TryExtractEntityKeyToken(userMessage, out var rowKey))
            {
                args["rowKey"] = rowKey;
            }

            // Filter entity extraction
            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase) &&
                _intentResolver.TryExtractEntityKeyToken(userMessage, out var filterEntityKey))
            {
                AddIfMissing(args, "operator", "eq");
                AddIfMissing(args, "value", filterEntityKey);
                if (!args.ContainsKey("column"))
                {
                    args["column"] = InferDataGridIdentityColumn(registeredComponents, userMessage);
                }
            }

            return;
        }

        // Navigation actions
        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(actionId, AgentComponentV1CapabilityProfile.NavigationNavigateExternalActionId, StringComparison.OrdinalIgnoreCase)) &&
            !HasAnyKey(args, "uri", "url", "target") &&
            _intentResolver.TryInferNavigationUri(userMessage, out var inferredUri))
        {
            args["uri"] = inferredUri;
            return;
        }

        // Tab switching
        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase) &&
            !args.ContainsKey("index") &&
            TryParseIndexFromUserMessage(userMessage, out var tabIndex))
        {
            args["index"] = tabIndex;
            return;
        }

        // Form field assignment
        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentV1CapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase) &&
            TryExtractFieldAssignment(userMessage, out var field, out var value))
        {
            AddIfMissing(args, "field", field);
            AddIfMissing(args, "value", value);
        }
    }

    public IReadOnlyList<FallbackAction> ResolveStructuredToolActions(
        string text,
        IReadOnlyList<ComponentCapability> allowedComponents)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // Build a catalog of all available actions
        var catalog = allowedComponents
            .SelectMany(component => component.Actions.Select(action => new FallbackAction(
                component.ComponentId,
                action.ActionId,
                action.RequiresApproval)))
            .ToArray();

        var byToolName = new Dictionary<string, FallbackAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in catalog)
        {
            var toolName = BuildComponentToolName(action.ComponentId, action.ActionId);
            byToolName.TryAdd(toolName, action);
        }

        // Parse structured tool directives from text
        var directives = ParseStructuredToolDirectives(text);
        if (directives.Count == 0)
        {
            return [];
        }

        var resolved = new List<FallbackAction>();
        foreach (var directive in directives)
        {
            if (!TryResolveFallbackAction(directive.Name, byToolName, catalog, out var action))
            {
                continue;
            }

            if (directive.Arguments is not null && directive.Arguments.Count > 0)
            {
                action = new FallbackAction(
                    action.ComponentId,
                    action.ActionId,
                    action.RequiresApproval,
                    directive.Arguments);
            }

            resolved.Add(action);
        }

        return resolved;
    }

    public string BuildToolInvocationReason(string userMessage)
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

    private static string BuildComponentToolName(string componentId, string actionId) =>
        SanitizeToolName($"agentblazor_{componentId}_{actionId}");

    private static string SanitizeToolName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        return new string(chars);
    }

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

    private static Dictionary<string, string> BuildToolParameters(string componentId, string actionId)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
            {
                parameters["column"] = "The column to filter on";
                parameters["operator"] = "The filter operator (eq, ne, gt, lt, gte, lte, contains, startswith, endswith)";
                parameters["value"] = "The value to filter by";
            }
            else if (string.Equals(actionId, AgentComponentV1CapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
            {
                parameters["column"] = "The column to sort by";
                parameters["direction"] = "The sort direction (asc or desc)";
            }
        }
        else if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
        {
            parameters["uri"] = "The URI to navigate to";
        }
        else if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase))
        {
            parameters["index"] = "The tab index (0-based)";
        }
        else if (string.Equals(componentId, AgentComponentV1CapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            parameters["field"] = "The field name";
            parameters["value"] = "The value to set";
        }

        return parameters;
    }

    private static void AddComponentStateHints(
        IDictionary<string, object?> arguments,
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        if (!TryResolveComponentSnapshot(componentId, actionId, registeredComponents, out var snapshot))
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

    private static bool TryResolveComponentSnapshot(
        string componentId,
        string actionId,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        out RegisteredComponentSnapshot snapshot)
    {
        snapshot = default!;
        var componentType = ResolveComponentType(componentId);
        if (componentType.Length == 0 || registeredComponents.Count == 0)
        {
            return false;
        }

        // First pass: match both component type and action
        foreach (var candidate in registeredComponents.OrderBy(s => s.AgentId, StringComparer.OrdinalIgnoreCase))
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

        // Second pass: match component type only
        foreach (var candidate in registeredComponents.OrderBy(s => s.AgentId, StringComparer.OrdinalIgnoreCase))
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

    private static string InferSortDirection(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return string.Empty;
        }

        if (userMessage.Contains("descending", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("highest", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("largest", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("most", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("high to low", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("highest to lowest", StringComparison.OrdinalIgnoreCase))
        {
            return "desc";
        }

        if (userMessage.Contains("ascending", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("lowest", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("smallest", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("least", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("low to high", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("lowest to highest", StringComparison.OrdinalIgnoreCase))
        {
            return "asc";
        }

        return string.Empty;
    }

    private static string InferDataGridIdentityColumn(
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        string userMessage)
    {
        // Try to find the ID column from component state
        var dataGrid = registeredComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentType, "DataGrid", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(dataGrid.ComponentType) && dataGrid.State is not null)
        {
            if (dataGrid.State.TryGetValue("identityColumn", out var identity) && !string.IsNullOrWhiteSpace(identity))
            {
                return identity;
            }

            if (dataGrid.State.TryGetValue("columns", out var columnsStr))
            {
                var columns = columnsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var idColumn = columns.FirstOrDefault(c =>
                    c.Contains("id", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("code", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(idColumn))
                {
                    return idColumn;
                }
            }
        }

        // Default to "Id"
        return "Id";
    }

    private static bool TryParseIndexFromUserMessage(string userMessage, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        // Try ordinal words
        var ordinalMatch = Regex.Match(
            userMessage,
            @"\b(first|second|third|fourth|fifth)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

        // Try numeric with optional suffix
        var numericMatch = Regex.Match(
            userMessage,
            @"\b(?<index>\d+)(?:st|nd|rd|th)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (numericMatch.Success && int.TryParse(numericMatch.Groups["index"].Value, out var parsed))
        {
            index = Math.Max(0, parsed);
            return true;
        }

        return false;
    }

    private static bool TryExtractFieldAssignment(string userMessage, out string field, out object? value)
    {
        field = string.Empty;
        value = null;

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var match = Regex.Match(
            userMessage,
            @"\b(?:set|update|change)\s+(?<field>[a-zA-Z][a-zA-Z0-9_\s-]{0,64})\s+(?:to|as)\s+(?<value>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    private static string ToPascalIdentifier(string raw)
    {
        var tokens = Regex.Matches(raw, @"[A-Za-z0-9]+")
            .Select(m => m.Value)
            .Where(t => t.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        return string.Concat(tokens.Select(t =>
            t.Length == 1
                ? t.ToUpperInvariant()
                : char.ToUpperInvariant(t[0]) + t[1..]));
    }

    private static object ParseScalarValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (int.TryParse(value, out var intValue))
            return intValue;

        if (double.TryParse(value, out var doubleValue))
            return doubleValue;

        return value;
    }

    private static IReadOnlyList<StructuredToolDirective> ParseStructuredToolDirectives(string text)
    {
        var directives = new List<StructuredToolDirective>();

        // Pattern: [TOOL:tool_name] or [TOOL:tool_name {"arg":"value"}]
        var pattern = @"\[TOOL:(\w+)(?:\s+({[^}]+}))?\]";
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            Dictionary<string, object?>? args = null;

            if (match.Groups[2].Success)
            {
                try
                {
                    args = JsonSerializer.Deserialize<Dictionary<string, object?>>(match.Groups[2].Value);
                }
                catch
                {
                    // Ignore parse errors
                }
            }

            directives.Add(new StructuredToolDirective(name, args));
        }

        return directives;
    }

    private static bool TryResolveFallbackAction(
        string name,
        IReadOnlyDictionary<string, FallbackAction> byToolName,
        IReadOnlyList<FallbackAction> catalog,
        out FallbackAction action)
    {
        action = default;

        // Try exact match
        if (byToolName.TryGetValue(name, out action))
        {
            return true;
        }

        // Try sanitized name
        var sanitized = SanitizeToolName(name);
        if (byToolName.TryGetValue(sanitized, out action))
        {
            return true;
        }

        // Try fuzzy match
        foreach (var candidate in catalog)
        {
            var candidateName = BuildComponentToolName(candidate.ComponentId, candidate.ActionId);
            if (candidateName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(candidateName, StringComparison.OrdinalIgnoreCase))
            {
                action = candidate;
                return true;
            }
        }

        return false;
    }

    private static void AddIfNotNull(IDictionary<string, object?> dictionary, string key, object? value)
    {
        if (value is not null)
        {
            dictionary[key] = value;
        }
    }

    private static void AddIfNotNullOrWhiteSpace(IDictionary<string, object?> dictionary, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dictionary[key] = value;
        }
    }

    private static void AddIfMissing(IDictionary<string, object?> dictionary, string key, object? value)
    {
        if (!dictionary.ContainsKey(key) && value is not null)
        {
            dictionary[key] = value;
        }
    }

    private static bool HasAnyKey(IDictionary<string, object?> dictionary, params string[] keys)
        => keys.Any(dictionary.ContainsKey);

    private readonly record struct StructuredToolDirective(
        string Name,
        IReadOnlyDictionary<string, object?>? Arguments);
}
