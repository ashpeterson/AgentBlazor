using System.Text;
using System.Text.RegularExpressions;
using AgentBlazor.Agents;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Builds prompts, instructions, and responses for agent interactions.
/// </summary>
internal sealed class ResponseBuilder : IResponseBuilder
{
    private readonly ConversationOptions _options;
    private readonly ILogger<ResponseBuilder>? _logger;

    public ResponseBuilder(
        IOptions<ConversationOptions>? options = null,
        ILogger<ResponseBuilder>? logger = null)
    {
        _options = options?.Value ?? new ConversationOptions();
        _logger = logger;
    }

    public string BuildPrompt(
        string userMessage,
        IDictionary<string, string>? context,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        ConversationHistory? conversationHistory = null)
    {
        if ((context is null || context.Count == 0) &&
            registeredComponents.Count == 0 &&
            (conversationHistory is null || conversationHistory.Turns.Count == 0))
        {
            return userMessage;
        }

        var builder = new StringBuilder(userMessage);

        // Add conversation history context
        if (conversationHistory is not null && conversationHistory.Turns.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent conversation:");

            var recentTurns = conversationHistory.Turns.TakeLast(_options.MaxHistoryInPrompt);
            foreach (var turn in recentTurns)
            {
                builder.AppendLine($"User: {turn.UserMessage}");
                builder.AppendLine($"Agent: {SummarizeTurn(turn)}");
            }
        }

        // Add request context
        if (context is not null && context.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Context:");
            foreach (var entry in context.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("- ").Append(entry.Key).Append(": ").AppendLine(entry.Value);
            }
        }

        // Add registered components
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
                        .AppendLine(string.Join(", ", component.State.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                }
            }
        }

        return builder.ToString().Trim();
    }

    public string BuildInstructions(
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

        builder.AppendLine();
        builder.AppendLine("Allowed components and actions:");
        foreach (var component in allowedComponents.OrderBy(c => c.ComponentId, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("- ").Append(component.ComponentId).Append(": ").Append(component.Description).AppendLine();
            foreach (var action in component.Actions.OrderBy(a => a.ActionId, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("  - ").Append(action.ActionId).Append(": ").Append(action.Description);
                if (action.RequiresApproval)
                {
                    builder.Append(" (requires approval)");
                }
                builder.AppendLine();
            }
        }

        builder.AppendLine();
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
                        .AppendLine(string.Join(", ", component.State.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                }
            }
        }

        return builder.ToString().Trim();
    }

    public string BuildMissingParameterSuggestion(
        string userMessage,
        string componentId,
        string actionId,
        string missingParameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var columnExamples = BuildDataGridColumnExamples(registeredComponents);
        var paramLower = missingParameter?.ToLowerInvariant() ?? string.Empty;

        // DataGrid sort
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase) &&
            paramLower == "column")
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

        // DataGrid filter
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
        {
            return paramLower switch
            {
                "column" => columnExamples.Length > 0
                    ? $"Which column should I filter (for example {columnExamples})?"
                    : "Which column should I filter?",
                "operator" => "Which filter operator should I use (for example >=, <=, eq, contains)?",
                "value" => "What value should I filter by?",
                _ => $"What should I use for '{missingParameter}' in the requested filter?"
            };
        }

        // Navigation
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase) &&
            paramLower is "uri" or "url" or "target")
        {
            if (TryInferNavigationUri(userMessage, out var inferredUri))
            {
                return $"Did you mean navigate to '{inferredUri}'?";
            }
            return "Which route should I navigate to (for example /suppliers or /settings)?";
        }

        // Tabs
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            paramLower == "index")
        {
            return "Which tab should I switch to (for example first, second, or an explicit index)?";
        }

        // Form
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            return paramLower switch
            {
                "field" => "Which form field should I set?",
                "value" => "What value should I set for that field?",
                _ => $"What should I use for '{missingParameter}'?"
            };
        }

        return $"What should I use for '{missingParameter}'?";
    }

    public string AppendFailureGuidance(
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

    public AgentTurnResponse FormatResponse(
        string agentName,
        string responseText,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults)
    {
        return new AgentTurnResponse(
            agentName,
            responseText,
            plannedActions,
            executionResults);
    }

    private static string SummarizeTurn(ConversationTurn turn)
    {
        if (string.IsNullOrWhiteSpace(turn.AgentResponse))
        {
            if (turn.ExecutionResults.Count > 0)
            {
                var successful = turn.ExecutionResults.Count(r => r.Succeeded);
                return $"Executed {successful}/{turn.ExecutionResults.Count} actions";
            }
            return "No response";
        }

        const int maxLength = 150;
        if (turn.AgentResponse.Length <= maxLength)
        {
            return turn.AgentResponse;
        }
        return turn.AgentResponse[..maxLength] + "...";
    }

    private string? BuildFailureGuidance(
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
        IReadOnlyList<ComponentActionExecutionResult> results,
        int currentIndex)
    {
        var current = results[currentIndex];
        for (var i = currentIndex + 1; i < results.Count; i++)
        {
            var subsequent = results[i];
            if (string.Equals(subsequent.ComponentId, current.ComponentId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(subsequent.ActionId, current.ActionId, StringComparison.OrdinalIgnoreCase) &&
                subsequent.Succeeded)
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryExtractMissingParameter(string message, out string parameter)
    {
        parameter = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var match = Regex.Match(
            message,
            @"[Mm]issing\s+(?:required\s+)?(?:parameter|argument)[:\s]+['""]?(\w+)['""]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success)
        {
            parameter = match.Groups[1].Value;
            return true;
        }

        match = Regex.Match(
            message,
            @"[Pp]arameter\s+['""]?(\w+)['""]?\s+is\s+required",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success)
        {
            parameter = match.Groups[1].Value;
            return true;
        }

        return false;
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

        if (TryGetDataGridColumns(registeredComponents, out var columns) && columns.Length > 0)
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

            columns = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return columns.Length > 0;
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

    private static bool TryInferNavigationUri(string userMessage, out string uri)
    {
        uri = string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var explicitUriMatch = Regex.Match(
            userMessage,
            @"https?://[^\s""']+|/[a-zA-Z0-9_/\-]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

        return false;
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
