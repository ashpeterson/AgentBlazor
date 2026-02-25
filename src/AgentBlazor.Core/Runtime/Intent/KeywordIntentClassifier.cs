using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Intent;

/// <summary>
/// Intent classifier that uses keyword matching with optional LLM fallback
/// when confidence is below threshold.
/// </summary>
internal sealed class KeywordIntentClassifier : IIntentClassifier
{
    private readonly IReadOnlyList<IntentRule> _rules;
    private readonly IntentClassificationOptions _options;
    private readonly IChatClient? _chatClient;
    private readonly ILogger<KeywordIntentClassifier>? _logger;
    private readonly ConcurrentDictionary<string, (IntentClassification Result, DateTime CachedAt)> _cache = new();

    public KeywordIntentClassifier(
        IOptions<IntentClassificationOptions>? options = null,
        IChatClient? chatClient = null,
        ILogger<KeywordIntentClassifier>? logger = null)
    {
        _rules = IntentRule.CreateDefaultRules();
        _options = options?.Value ?? new IntentClassificationOptions();
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<IntentClassification> ClassifyAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions,
        ConversationHistory? history = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return IntentClassification.Unknown("What would you like me to do?");
        }

        // Check cache
        if (_options.EnableCaching && TryGetCached(userMessage, out var cached))
        {
            return cached;
        }

        // Try keyword classification first
        var keywordResult = ClassifyWithKeywords(userMessage, availableActions);

        if (keywordResult.Confidence >= _options.MinConfidenceThreshold)
        {
            CacheResult(userMessage, keywordResult);
            return keywordResult;
        }

        // Fall back to LLM if enabled and available
        if (_options.UseLlmFallback && _chatClient is not null)
        {
            try
            {
                var llmResult = await ClassifyWithLlmAsync(
                    userMessage,
                    availableActions,
                    history,
                    cancellationToken);

                // Use LLM result if it has higher confidence
                if (llmResult.Confidence > keywordResult.Confidence)
                {
                    CacheResult(userMessage, llmResult);
                    return llmResult;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "LLM fallback classification failed, using keyword result");
            }
        }

        // Return keyword result even if low confidence
        CacheResult(userMessage, keywordResult);
        return keywordResult;
    }

    public async Task<IReadOnlyList<IntentClassification>> ClassifyMultipleAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions,
        ConversationHistory? history = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return [IntentClassification.Unknown()];
        }

        var results = new List<IntentClassification>();

        // Get all keyword matches
        var keywordMatches = ClassifyAllWithKeywords(userMessage, availableActions);
        results.AddRange(keywordMatches);

        // Add LLM classification if enabled
        if (_options.UseLlmFallback && _chatClient is not null)
        {
            try
            {
                var llmResult = await ClassifyWithLlmAsync(
                    userMessage,
                    availableActions,
                    history,
                    cancellationToken);

                // Merge LLM result if it's unique
                if (!results.Any(r =>
                    r.TargetComponentId == llmResult.TargetComponentId &&
                    r.TargetActionId == llmResult.TargetActionId))
                {
                    results.Add(llmResult);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "LLM multi-intent classification failed");
            }
        }

        return results
            .OrderByDescending(r => r.Confidence)
            .Take(_options.MaxIntentsToReturn)
            .ToArray();
    }

    private IntentClassification ClassifyWithKeywords(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions)
    {
        var shouldFilterByAvailableActions = availableActions.Count > 0;
        var allowedActionKeys = new HashSet<string>(
            availableActions.SelectMany(c => c.Actions.Select(a => $"{c.ComponentId}.{a.ActionId}")),
            StringComparer.OrdinalIgnoreCase);

        IntentRule? bestMatch = null;
        float bestConfidence = 0f;

        foreach (var rule in _rules.OrderByDescending(r => r.Priority))
        {
            // Skip rules for actions not available
            var actionKey = $"{rule.ComponentId}.{rule.ActionId}";
            if (shouldFilterByAvailableActions && !allowedActionKeys.Contains(actionKey))
                continue;

            if (!rule.Matches(userMessage))
                continue;

            var confidence = rule.CalculateConfidence(userMessage);
            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestMatch = rule;
            }
        }

        if (bestMatch is null)
        {
            return IntentClassification.Unknown();
        }

        // Extract entities using patterns
        var entities = ExtractEntities(userMessage, bestMatch);

        return new IntentClassification
        {
            PrimaryIntent = bestMatch.IntentName,
            TargetComponentId = bestMatch.ComponentId,
            TargetActionId = bestMatch.ActionId,
            Confidence = bestConfidence,
            ClassificationMethod = "keyword",
            ExtractedEntities = entities
        };
    }

    private IReadOnlyList<IntentClassification> ClassifyAllWithKeywords(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions)
    {
        var shouldFilterByAvailableActions = availableActions.Count > 0;
        var allowedActionKeys = new HashSet<string>(
            availableActions.SelectMany(c => c.Actions.Select(a => $"{c.ComponentId}.{a.ActionId}")),
            StringComparer.OrdinalIgnoreCase);

        var results = new List<IntentClassification>();

        foreach (var rule in _rules.OrderByDescending(r => r.Priority))
        {
            var actionKey = $"{rule.ComponentId}.{rule.ActionId}";
            if (shouldFilterByAvailableActions && !allowedActionKeys.Contains(actionKey))
                continue;

            if (!rule.Matches(userMessage))
                continue;

            var confidence = rule.CalculateConfidence(userMessage);
            var entities = ExtractEntities(userMessage, rule);

            results.Add(new IntentClassification
            {
                PrimaryIntent = rule.IntentName,
                TargetComponentId = rule.ComponentId,
                TargetActionId = rule.ActionId,
                Confidence = confidence,
                ClassificationMethod = "keyword",
                ExtractedEntities = entities
            });
        }

        return results;
    }

    private Dictionary<string, object?> ExtractEntities(string userMessage, IntentRule rule)
    {
        var entities = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in rule.EntityPatterns)
        {
            try
            {
                var match = Regex.Match(
                    userMessage,
                    pattern.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (match.Success && match.Groups.Count > pattern.GroupIndex)
                {
                    var value = match.Groups[pattern.GroupIndex].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        entities[pattern.EntityName] = NormalizeEntityValue(pattern.EntityName, value);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _logger?.LogWarning("Regex timeout extracting entity {EntityName}", pattern.EntityName);
            }
        }

        // Extract common entities not covered by patterns
        ExtractCommonEntities(userMessage, rule, entities);

        return entities;
    }

    private void ExtractCommonEntities(
        string userMessage,
        IntentRule rule,
        Dictionary<string, object?> entities)
    {
        // Extract entity keys (e.g., SUP-123, ABC456)
        if (!entities.ContainsKey("rowKey") && !entities.ContainsKey("value"))
        {
            var entityKeyMatch = Regex.Match(
                userMessage,
                @"\b([A-Za-z]{2,}[-_]\d+|[A-Za-z]+\d{3,})\b",
                RegexOptions.IgnoreCase);
            if (entityKeyMatch.Success)
            {
                var key = rule.IntentName switch
                {
                    "select_row" or "navigate_to_row" => "rowKey",
                    "filter" => "value",
                    _ => null
                };
                if (key is not null)
                {
                    entities[key] = entityKeyMatch.Value;
                }
            }
        }

        // Infer sort direction from keywords
        if (rule.IntentName == "sort" && !entities.ContainsKey("direction"))
        {
            if (ContainsAny(userMessage, "highest to lowest", "high to low", "descending", "desc", "highest", "max", "top"))
            {
                entities["direction"] = "desc";
            }
            else if (ContainsAny(userMessage, "lowest to highest", "low to high", "ascending", "asc", "lowest", "min", "bottom"))
            {
                entities["direction"] = "asc";
            }
        }

        // Infer filter operator
        if (rule.IntentName == "filter" && !entities.ContainsKey("operator"))
        {
            if (ContainsAny(userMessage, ">=", "greater than or equal"))
                entities["operator"] = "gte";
            else if (ContainsAny(userMessage, "<=", "less than or equal"))
                entities["operator"] = "lte";
            else if (ContainsAny(userMessage, ">", "greater than", "more than"))
                entities["operator"] = "gt";
            else if (ContainsAny(userMessage, "<", "less than"))
                entities["operator"] = "lt";
            else if (ContainsAny(userMessage, "contains", "includes", "like"))
                entities["operator"] = "contains";
            else if (ContainsAny(userMessage, "equals", "equal to", "is", "="))
                entities["operator"] = "eq";
        }

        // Infer tab index
        if (rule.IntentName == "switch_tab" && !entities.ContainsKey("index"))
        {
            if (ContainsAny(userMessage, "first"))
                entities["index"] = 0;
            else if (ContainsAny(userMessage, "second"))
                entities["index"] = 1;
            else if (ContainsAny(userMessage, "third"))
                entities["index"] = 2;
            else if (ContainsAny(userMessage, "fourth"))
                entities["index"] = 3;
            else if (ContainsAny(userMessage, "fifth"))
                entities["index"] = 4;
        }
    }

    private static object? NormalizeEntityValue(string entityName, string value)
    {
        // Convert ordinals to indices
        if (entityName == "index")
        {
            return value.ToLowerInvariant() switch
            {
                "first" => 0,
                "second" => 1,
                "third" => 2,
                "fourth" => 3,
                "fifth" => 4,
                _ when int.TryParse(value, out var i) => i,
                _ => value
            };
        }

        // Normalize direction
        if (entityName == "direction")
        {
            return value.ToLowerInvariant() switch
            {
                "ascending" or "asc" or "lowest to highest" => "asc",
                "descending" or "desc" or "highest to lowest" => "desc",
                _ => value.ToLowerInvariant()
            };
        }

        // Parse numbers
        if (int.TryParse(value, out var intVal))
            return intVal;
        if (double.TryParse(value, out var doubleVal))
            return doubleVal;

        return value;
    }

    private async Task<IntentClassification> ClassifyWithLlmAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions,
        ConversationHistory? history,
        CancellationToken cancellationToken)
    {
        var prompt = BuildLlmClassificationPrompt(userMessage, availableActions, history);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.LlmTimeout);

        var response = await _chatClient!.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { Temperature = 0.1f },
            cts.Token);

        var responseText = response.Text ?? string.Empty;
        return ParseLlmResponse(responseText, availableActions);
    }

    private string BuildLlmClassificationPrompt(
        string userMessage,
        IReadOnlyList<ComponentCapability> availableActions,
        ConversationHistory? history)
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are an intent classifier for a UI automation system.");
        builder.AppendLine("Classify the user's intent and extract relevant entities.");
        builder.AppendLine();
        builder.AppendLine("Available actions:");

        foreach (var component in availableActions)
        {
            foreach (var action in component.Actions)
            {
                builder.AppendLine($"- {component.ComponentId}.{action.ActionId}: {action.Description}");
            }
        }

        builder.AppendLine();

        if (_options.UseConversationContext && history?.Turns.Count > 0)
        {
            builder.AppendLine("Recent conversation:");
            foreach (var turn in history.GetRecentTurns(_options.ConversationContextTurns))
            {
                builder.AppendLine($"User: {turn.UserMessage}");
                builder.AppendLine($"Agent: {turn.Summarize()}");
            }
            builder.AppendLine();
        }

        builder.AppendLine($"User message: {userMessage}");
        builder.AppendLine();
        builder.AppendLine("Respond with JSON only:");
        builder.AppendLine("""
        {
          "intent": "the primary intent name",
          "componentId": "target component ID or null",
          "actionId": "target action ID or null",
          "confidence": 0.0 to 1.0,
          "entities": { "key": "value" }
        }
        """);

        return builder.ToString();
    }

    private IntentClassification ParseLlmResponse(
        string responseText,
        IReadOnlyList<ComponentCapability> availableActions)
    {
        try
        {
            // Extract JSON from response
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                return IntentClassification.Unknown();
            }

            var json = responseText[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var intent = root.TryGetProperty("intent", out var intentProp)
                ? intentProp.GetString() ?? "unknown"
                : "unknown";

            var componentId = root.TryGetProperty("componentId", out var compProp) && compProp.ValueKind != JsonValueKind.Null
                ? compProp.GetString()
                : null;

            var actionId = root.TryGetProperty("actionId", out var actProp) && actProp.ValueKind != JsonValueKind.Null
                ? actProp.GetString()
                : null;

            var confidence = root.TryGetProperty("confidence", out var confProp)
                ? (float)confProp.GetDouble()
                : 0.7f;

            var entities = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("entities", out var entitiesProp) && entitiesProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in entitiesProp.EnumerateObject())
                {
                    entities[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.ToString()
                    };
                }
            }

            // Validate component/action against available actions
            if (componentId is not null && actionId is not null)
            {
                var isValid = availableActions.Any(c =>
                    string.Equals(c.ComponentId, componentId, StringComparison.OrdinalIgnoreCase) &&
                    c.Actions.Any(a => string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase)));

                if (!isValid)
                {
                    _logger?.LogWarning(
                        "LLM suggested invalid action {ComponentId}.{ActionId}",
                        componentId, actionId);
                    return IntentClassification.Unknown();
                }
            }

            return new IntentClassification
            {
                PrimaryIntent = intent,
                TargetComponentId = componentId,
                TargetActionId = actionId,
                Confidence = confidence,
                ClassificationMethod = "llm",
                ExtractedEntities = entities
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse LLM classification response");
            return IntentClassification.Unknown();
        }
    }

    private bool TryGetCached(string userMessage, out IntentClassification result)
    {
        result = default!;
        var key = userMessage.ToLowerInvariant().Trim();

        if (!_cache.TryGetValue(key, out var cached))
            return false;

        if (DateTime.UtcNow - cached.CachedAt > _options.CacheDuration)
        {
            _cache.TryRemove(key, out _);
            return false;
        }

        result = cached.Result;
        return true;
    }

    private void CacheResult(string userMessage, IntentClassification result)
    {
        if (!_options.EnableCaching)
            return;

        var key = userMessage.ToLowerInvariant().Trim();
        _cache[key] = (result, DateTime.UtcNow);

        // Prune old entries periodically
        if (_cache.Count > 1000)
        {
            var cutoff = DateTime.UtcNow - _options.CacheDuration;
            foreach (var kvp in _cache.Where(kvp => kvp.Value.CachedAt < cutoff).ToArray())
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));
}
