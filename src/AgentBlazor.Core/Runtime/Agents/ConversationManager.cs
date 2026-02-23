using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Manages conversation state, history, and pending clarifications for agent sessions.
/// </summary>
internal sealed class ConversationManager : IConversationManager
{
    private readonly IConversationStore _conversationStore;
    private readonly ConversationOptions _options;
    private readonly ILogger<ConversationManager>? _logger;

    private readonly ConcurrentDictionary<string, PendingClarification> _pendingClarifications = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AffirmativeReplies = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "y", "yeah", "yep", "correct", "do it", "go ahead", "sounds good", "ok", "okay", "sure", "confirm"
    };

    private static readonly HashSet<string> NegativeReplies = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "n", "nope", "cancel", "stop", "not now", "never mind", "forget it", "abort"
    };

    public ConversationManager(
        IConversationStore conversationStore,
        IOptions<ConversationOptions>? options = null,
        ILogger<ConversationManager>? logger = null)
    {
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _options = options?.Value ?? new ConversationOptions();
        _logger = logger;
    }

    public Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _conversationStore.GetHistoryAsync(sessionId, cancellationToken);
    }

    public Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turn);
        return _conversationStore.AppendTurnAsync(sessionId, turn, cancellationToken);
    }

    public async Task<string> BuildContextAsync(
        string sessionId,
        int maxTurns = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var history = await _conversationStore.GetHistoryAsync(sessionId, cancellationToken);
        if (history is null || history.Turns.Count == 0)
        {
            return string.Empty;
        }

        var effectiveMaxTurns = Math.Min(maxTurns, _options.MaxHistoryInPrompt);
        var recentTurns = history.Turns.TakeLast(effectiveMaxTurns).ToArray();

        if (recentTurns.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Recent conversation:");

        foreach (var turn in recentTurns)
        {
            builder.AppendLine($"User: {turn.UserMessage}");
            builder.AppendLine($"Agent: {SummarizeTurn(turn)}");

            if (_options.IncludeActionResultsInHistory && turn.ExecutionResults.Count > 0)
            {
                var successCount = turn.ExecutionResults.Count(r => r.Succeeded);
                var failedCount = turn.ExecutionResults.Count - successCount;
                builder.AppendLine($"  Actions: {successCount} succeeded, {failedCount} failed");
            }
        }

        return builder.ToString().Trim();
    }

    public PendingClarification? GetPendingClarification(string sessionId, string agentName)
    {
        var key = BuildConversationKey(sessionId, agentName);
        return _pendingClarifications.TryGetValue(key, out var pending) ? pending : null;
    }

    public void SetPendingClarification(string sessionId, string agentName, PendingClarification clarification)
    {
        var key = BuildConversationKey(sessionId, agentName);
        _pendingClarifications[key] = clarification;
        _logger?.LogDebug(
            "Set pending clarification for {SessionId}/{AgentName}: {Parameter}",
            sessionId, agentName, clarification.MissingParameter);
    }

    public void ClearPendingClarification(string sessionId, string agentName)
    {
        var key = BuildConversationKey(sessionId, agentName);
        if (_pendingClarifications.TryRemove(key, out _))
        {
            _logger?.LogDebug("Cleared pending clarification for {SessionId}/{AgentName}", sessionId, agentName);
        }
    }

    public bool TryResolvePendingArguments(
        string userReply,
        PendingClarification pending,
        out IReadOnlyDictionary<string, object?> resolvedArguments)
    {
        var merged = new Dictionary<string, object?>(
            pending.BaseArguments,
            StringComparer.OrdinalIgnoreCase);

        // If user confirms and we have suggestions, use them
        if (IsAffirmativeReply(userReply) && pending.SuggestedArguments.Count > 0)
        {
            foreach (var pair in pending.SuggestedArguments)
            {
                merged[pair.Key] = pair.Value;
            }

            resolvedArguments = merged;
            return true;
        }

        // Try to parse the missing parameter from the reply
        if (!TryParseParameterFromReply(userReply, pending, out var parsed))
        {
            resolvedArguments = merged;
            return false;
        }

        merged[pending.MissingParameter] = parsed;
        resolvedArguments = merged;
        return true;
    }

    public bool TryBuildPendingClarification(
        string userMessage,
        IReadOnlyList<PlannedComponentAction> plannedActions,
        IReadOnlyList<ComponentActionExecutionResult> executionResults,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        out PendingClarification? clarification)
    {
        clarification = null;

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

            var question = BuildMissingParameterQuestion(
                userMessage,
                result.ComponentId,
                result.ActionId,
                missingParameter,
                registeredComponents);

            if (string.IsNullOrWhiteSpace(question))
            {
                continue;
            }

            var suggestedArguments = BuildSuggestedArguments(
                userMessage,
                result.ComponentId,
                result.ActionId,
                missingParameter,
                registeredComponents);

            var baseArguments = planned.Arguments is null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(planned.Arguments, StringComparer.OrdinalIgnoreCase);

            clarification = new PendingClarification
            {
                ComponentId = result.ComponentId,
                ActionId = result.ActionId,
                MissingParameter = missingParameter,
                Prompt = question,
                BaseArguments = baseArguments,
                SuggestedArguments = suggestedArguments
            };

            return true;
        }

        return false;
    }

    public bool IsAffirmativeReply(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return AffirmativeReplies.Contains(normalized);
    }

    public bool IsNegativeReply(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return NegativeReplies.Contains(normalized);
    }

    public Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // Clear any pending clarifications for this session
        var keysToRemove = _pendingClarifications.Keys
            .Where(k => k.StartsWith(sessionId + ":", StringComparison.OrdinalIgnoreCase) ||
                        k.EndsWith(":" + sessionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in keysToRemove)
        {
            _pendingClarifications.TryRemove(key, out _);
        }

        return _conversationStore.ClearSessionAsync(sessionId, cancellationToken);
    }

    private static string BuildConversationKey(string sessionId, string agentName)
    {
        var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? "global" : sessionId.Trim();
        return $"{agentName}:{effectiveSessionId}";
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

        const int maxLength = 200;
        if (turn.AgentResponse.Length <= maxLength)
        {
            return turn.AgentResponse;
        }

        return turn.AgentResponse[..maxLength] + "...";
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

        var paramLower = pending.MissingParameter.ToLowerInvariant();

        // Column parameter
        if (paramLower == "column")
        {
            var fallback = userReply.Trim().Trim('"', '\'');
            if (fallback.Length > 0 && fallback.Length <= 128)
            {
                parsedValue = fallback;
                return true;
            }
            return false;
        }

        // URI parameter
        if (paramLower == "uri")
        {
            var uriMatch = Regex.Match(
                userReply,
                @"https?://[^\s""']+|/[a-zA-Z0-9_/\-]+",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!uriMatch.Success)
            {
                return false;
            }

            parsedValue = uriMatch.Value;
            return true;
        }

        // Index parameters
        if (paramLower is "index" or "pageindex")
        {
            if (int.TryParse(userReply.Trim(), out var index))
            {
                parsedValue = index;
                return true;
            }
            return false;
        }

        // Generic value parameter
        if (paramLower == "value")
        {
            var trimmed = userReply.Trim();
            if (int.TryParse(trimmed, out var i))
            {
                parsedValue = i;
                return true;
            }
            if (double.TryParse(trimmed, out var d))
            {
                parsedValue = d;
                return true;
            }
            parsedValue = trimmed.Trim('"', '\'');
            return true;
        }

        // Default: treat as string
        parsedValue = userReply.Trim().Trim('"', '\'');
        return parsedValue is string text && text.Length > 0;
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

        // Pattern: "Missing required parameter: column"
        var match = Regex.Match(
            message,
            @"[Mm]issing\s+(?:required\s+)?(?:parameter|argument)[:\s]+['""]?(\w+)['""]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success)
        {
            parameter = match.Groups[1].Value;
            return true;
        }

        // Pattern: "Parameter 'column' is required"
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

    private static string BuildMissingParameterQuestion(
        string userMessage,
        string componentId,
        string actionId,
        string missingParameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var paramLower = missingParameter.ToLowerInvariant();
        var componentType = GetComponentType(componentId);

        // DataGrid filter
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase))
        {
            if (paramLower == "column")
            {
                var columns = GetDataGridColumns(registeredComponents);
                if (columns.Length > 0)
                {
                    return $"Which column would you like to filter? Available columns: {string.Join(", ", columns.Take(6))}";
                }
                return "Which column would you like to filter?";
            }
            if (paramLower == "operator")
            {
                return "How would you like to filter? (equals, contains, greater than, less than)";
            }
            if (paramLower == "value")
            {
                return "What value would you like to filter by?";
            }
        }

        // DataGrid sort
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
        {
            if (paramLower == "column")
            {
                var columns = GetDataGridColumns(registeredComponents);
                if (columns.Length > 0)
                {
                    return $"Which column would you like to sort by? Available columns: {string.Join(", ", columns.Take(6))}";
                }
                return "Which column would you like to sort by?";
            }
        }

        // Navigation
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (paramLower is "uri" or "url" or "target")
            {
                return "Where would you like to navigate to?";
            }
        }

        // Tabs
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase) &&
            paramLower == "index")
        {
            return "Which tab would you like to switch to? (first, second, third, or a number)";
        }

        // Form
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase))
        {
            if (paramLower == "field")
            {
                return "Which field would you like to set?";
            }
            if (paramLower == "value")
            {
                return "What value would you like to set?";
            }
        }

        // Generic fallback
        return $"Please provide a value for '{missingParameter}':";
    }

    private static IReadOnlyDictionary<string, object?> BuildSuggestedArguments(
        string userMessage,
        string componentId,
        string actionId,
        string missingParameter,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var suggestions = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Try to infer suggested values from context
        if (string.Equals(componentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase))
        {
            // Infer operator for filter
            if (missingParameter.Equals("operator", StringComparison.OrdinalIgnoreCase))
            {
                if (userMessage.Contains("greater", StringComparison.OrdinalIgnoreCase) ||
                    userMessage.Contains("more than", StringComparison.OrdinalIgnoreCase) ||
                    userMessage.Contains("above", StringComparison.OrdinalIgnoreCase))
                {
                    suggestions["operator"] = "gt";
                }
                else if (userMessage.Contains("less", StringComparison.OrdinalIgnoreCase) ||
                         userMessage.Contains("fewer", StringComparison.OrdinalIgnoreCase) ||
                         userMessage.Contains("below", StringComparison.OrdinalIgnoreCase))
                {
                    suggestions["operator"] = "lt";
                }
                else
                {
                    suggestions["operator"] = "contains";
                }
            }

            // Infer direction for sort
            if (missingParameter.Equals("direction", StringComparison.OrdinalIgnoreCase))
            {
                if (userMessage.Contains("descending", StringComparison.OrdinalIgnoreCase) ||
                    userMessage.Contains("highest", StringComparison.OrdinalIgnoreCase) ||
                    userMessage.Contains("largest", StringComparison.OrdinalIgnoreCase))
                {
                    suggestions["direction"] = "desc";
                }
                else
                {
                    suggestions["direction"] = "asc";
                }
            }
        }

        return suggestions;
    }

    private static string[] GetDataGridColumns(IReadOnlyList<RegisteredComponentSnapshot> registeredComponents)
    {
        var dataGrid = registeredComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentType, "DataGrid", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(dataGrid.ComponentType) || dataGrid.State is null)
        {
            return [];
        }

        if (dataGrid.State.TryGetValue("columns", out var columnsStr))
        {
            return columnsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return [];
    }

    private static string GetComponentType(string componentId)
    {
        return componentId switch
        {
            var id when id.Contains("DataGrid", StringComparison.OrdinalIgnoreCase) => "DataGrid",
            var id when id.Contains("NavMenu", StringComparison.OrdinalIgnoreCase) => "Navigation",
            var id when id.Contains("Dialog", StringComparison.OrdinalIgnoreCase) => "Dialog",
            var id when id.Contains("Form", StringComparison.OrdinalIgnoreCase) => "Form",
            var id when id.Contains("Tabs", StringComparison.OrdinalIgnoreCase) => "Tabs",
            _ => "Component"
        };
    }
}
