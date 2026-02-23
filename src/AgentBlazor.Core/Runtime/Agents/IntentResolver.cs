using System.Text.RegularExpressions;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Components;
using AgentBlazor.Core.Runtime.Conversation;
using AgentBlazor.Core.Runtime.Intent;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Core.Runtime.Preferences;
using AgentBlazor.Core.Runtime.Routing;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Resolves user intents by combining intent classification, route resolution,
/// and user preferences to produce actionable operations.
/// </summary>
internal sealed class IntentResolver : IIntentResolver
{
    private readonly IIntentClassifier _intentClassifier;
    private readonly IRouteRegistry _routeRegistry;
    private readonly ILogger<IntentResolver>? _logger;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "go", "to", "open", "show", "navigate", "take", "bring", "me", "the", "a", "an", "my", "all",
        "please", "page", "screen", "section", "view", "highest", "lowest", "high", "low",
        "risk", "filtered", "filter", "sorted", "sort", "by"
    };

    public IntentResolver(
        IIntentClassifier intentClassifier,
        IRouteRegistry routeRegistry,
        ILogger<IntentResolver>? logger = null)
    {
        _intentClassifier = intentClassifier ?? throw new ArgumentNullException(nameof(intentClassifier));
        _routeRegistry = routeRegistry ?? throw new ArgumentNullException(nameof(routeRegistry));
        _logger = logger;
    }

    public async Task<ResolvedIntent?> ResolveAsync(
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        ConversationHistory? history = null,
        UserPreferences? preferences = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        // First, try to classify the intent
        var classification = await _intentClassifier.ClassifyAsync(
            userMessage,
            allowedComponents,
            history,
            cancellationToken);

        if (classification.IsUnknown)
        {
            _logger?.LogDebug("Could not classify intent for message: {Message}", userMessage);
            return null;
        }

        // Handle navigation intents with route registry
        if (string.Equals(classification.PrimaryIntent, "navigate", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveNavigationIntent(userMessage, classification, allowedComponents, preferences);
        }

        // Handle other intents by mapping to component actions
        if (!string.IsNullOrEmpty(classification.TargetComponentId) &&
            !string.IsNullOrEmpty(classification.TargetActionId))
        {
            var component = allowedComponents.FirstOrDefault(c =>
                string.Equals(c.ComponentId, classification.TargetComponentId, StringComparison.OrdinalIgnoreCase));

            if (component is not null)
            {
                var action = component.Actions.FirstOrDefault(a =>
                    string.Equals(a.ActionId, classification.TargetActionId, StringComparison.OrdinalIgnoreCase));

                if (action is not null)
                {
                    var arguments = BuildArguments(classification, userMessage, preferences);

                    return new ResolvedIntent
                    {
                        Classification = classification,
                        ComponentId = classification.TargetComponentId,
                        ActionId = classification.TargetActionId,
                        Arguments = arguments,
                        RequiresApproval = action.RequiresApproval,
                        Reason = $"Resolved from intent: {classification.PrimaryIntent}"
                    };
                }
            }
        }

        // Try fallback resolution based on intent type
        return TryFallbackResolution(userMessage, classification, allowedComponents, registeredComponents, preferences);
    }

    private ResolvedIntent? ResolveNavigationIntent(
        string userMessage,
        IntentClassification classification,
        IReadOnlyList<ComponentCapability> allowedComponents,
        UserPreferences? preferences)
    {
        // Check if navigation component is available
        var navComponent = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, AgentComponentCapabilityProfile.AgentNavMenuComponentId, StringComparison.OrdinalIgnoreCase));

        if (navComponent is null)
        {
            return null;
        }

        // Try to resolve URI from route registry first
        if (classification.ExtractedEntities.TryGetValue("route", out var routeObj) &&
            routeObj is string route &&
            _routeRegistry.TryResolve(route, out var routeMatch))
        {
            return CreateNavigationIntent(classification, routeMatch.Path, navComponent);
        }

        // Fall back to URI inference
        if (TryInferNavigationUri(userMessage, out var uri))
        {
            // Try to improve with route registry
            if (_routeRegistry.TryResolve(uri.TrimStart('/'), out var match) && match.Confidence > 0.7f)
            {
                uri = match.Path;
            }

            return CreateNavigationIntent(classification, uri, navComponent);
        }

        // Check user preferences for frequent routes
        if (preferences?.FrequentRoutes.Count > 0)
        {
            var tokens = userMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var frequentRoute in preferences.FrequentRoutes)
            {
                var routeName = frequentRoute.TrimStart('/').Replace("-", " ");
                if (tokens.Any(t => routeName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    return CreateNavigationIntent(classification, frequentRoute, navComponent);
                }
            }
        }

        return null;
    }

    private static ResolvedIntent CreateNavigationIntent(
        IntentClassification classification,
        string uri,
        ComponentCapability navComponent)
    {
        var navAction = navComponent.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, AgentComponentCapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase));

        return new ResolvedIntent
        {
            Classification = classification,
            ComponentId = AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            ActionId = AgentComponentCapabilityProfile.NavigationNavigateToActionId,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["uri"] = uri
            },
            RequiresApproval = navAction?.RequiresApproval ?? false,
            Reason = $"Navigate to {uri}"
        };
    }

    private ResolvedIntent? TryFallbackResolution(
        string userMessage,
        IntentClassification classification,
        IReadOnlyList<ComponentCapability> allowedComponents,
        IReadOnlyList<RegisteredComponentSnapshot> registeredComponents,
        UserPreferences? preferences)
    {
        var intent = classification.PrimaryIntent.ToLowerInvariant();

        // Filter intent
        if (intent is "filter" or "search" or "find")
        {
            return TryResolveFilterIntent(classification, allowedComponents, preferences);
        }

        // Sort intent
        if (intent is "sort" or "order")
        {
            return TryResolveSortIntent(classification, userMessage, allowedComponents, preferences);
        }

        // Tab switching
        if (intent is "switch_tab" or "tab")
        {
            return TryResolveTabIntent(classification, userMessage, allowedComponents);
        }

        // Dialog actions
        if (intent is "dialog" or "open_dialog" or "close_dialog")
        {
            return TryResolveDialogIntent(classification, userMessage, allowedComponents);
        }

        // Form actions
        if (intent is "form" or "submit" or "reset" or "set_field")
        {
            return TryResolveFormIntent(classification, userMessage, allowedComponents);
        }

        return null;
    }

    private static ResolvedIntent? TryResolveFilterIntent(
        IntentClassification classification,
        IReadOnlyList<ComponentCapability> allowedComponents,
        UserPreferences? preferences)
    {
        var dataGrid = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase));

        if (dataGrid is null)
        {
            return null;
        }

        var filterAction = dataGrid.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, AgentComponentCapabilityProfile.DataGridFilterActionId, StringComparison.OrdinalIgnoreCase));

        if (filterAction is null)
        {
            return null;
        }

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Extract filter parameters from classification
        if (classification.ExtractedEntities.TryGetValue("column", out var col))
            args["column"] = col;
        if (classification.ExtractedEntities.TryGetValue("operator", out var op))
            args["operator"] = op;
        if (classification.ExtractedEntities.TryGetValue("value", out var val))
            args["value"] = val;

        return new ResolvedIntent
        {
            Classification = classification,
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridFilterActionId,
            Arguments = args,
            RequiresApproval = filterAction.RequiresApproval,
            Reason = "Filter data grid"
        };
    }

    private static ResolvedIntent? TryResolveSortIntent(
        IntentClassification classification,
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents,
        UserPreferences? preferences)
    {
        var dataGrid = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, AgentComponentCapabilityProfile.AgentDataGridComponentId, StringComparison.OrdinalIgnoreCase));

        if (dataGrid is null)
        {
            return null;
        }

        var sortAction = dataGrid.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase));

        if (sortAction is null)
        {
            return null;
        }

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Extract from classification
        if (classification.ExtractedEntities.TryGetValue("column", out var col))
            args["column"] = col;
        if (classification.ExtractedEntities.TryGetValue("direction", out var dir))
            args["direction"] = dir;

        // Try to infer direction from message
        if (!args.ContainsKey("direction"))
        {
            if (userMessage.Contains("descending", StringComparison.OrdinalIgnoreCase) ||
                userMessage.Contains("highest", StringComparison.OrdinalIgnoreCase) ||
                userMessage.Contains("high to low", StringComparison.OrdinalIgnoreCase))
            {
                args["direction"] = "desc";
            }
            else if (userMessage.Contains("ascending", StringComparison.OrdinalIgnoreCase) ||
                     userMessage.Contains("lowest", StringComparison.OrdinalIgnoreCase) ||
                     userMessage.Contains("low to high", StringComparison.OrdinalIgnoreCase))
            {
                args["direction"] = "asc";
            }
        }

        // Use preference if no column specified
        if (!args.ContainsKey("column") && preferences is not null)
        {
            var preferredColumn = preferences.GetPreferredSortColumn(AgentComponentCapabilityProfile.AgentDataGridComponentId);
            if (!string.IsNullOrEmpty(preferredColumn))
            {
                args["column"] = preferredColumn;
            }

            if (!args.ContainsKey("direction"))
            {
                var preferredDirection = preferences.GetPreferredSortDirection(AgentComponentCapabilityProfile.AgentDataGridComponentId);
                if (!string.IsNullOrEmpty(preferredDirection))
                {
                    args["direction"] = preferredDirection;
                }
            }
        }

        return new ResolvedIntent
        {
            Classification = classification,
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridSortActionId,
            Arguments = args,
            RequiresApproval = sortAction.RequiresApproval,
            Reason = "Sort data grid"
        };
    }

    private static ResolvedIntent? TryResolveTabIntent(
        IntentClassification classification,
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents)
    {
        var tabs = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, AgentComponentCapabilityProfile.AgentTabsComponentId, StringComparison.OrdinalIgnoreCase));

        if (tabs is null)
        {
            return null;
        }

        var switchAction = tabs.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, AgentComponentCapabilityProfile.TabsSwitchTabActionId, StringComparison.OrdinalIgnoreCase));

        if (switchAction is null)
        {
            return null;
        }

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Try to extract index from classification
        if (classification.ExtractedEntities.TryGetValue("index", out var idx))
        {
            args["index"] = idx;
        }
        else if (TryParseIndexFromMessage(userMessage, out var index))
        {
            args["index"] = index;
        }

        return new ResolvedIntent
        {
            Classification = classification,
            ComponentId = AgentComponentCapabilityProfile.AgentTabsComponentId,
            ActionId = AgentComponentCapabilityProfile.TabsSwitchTabActionId,
            Arguments = args,
            RequiresApproval = switchAction.RequiresApproval,
            Reason = "Switch tab"
        };
    }

    private static ResolvedIntent? TryResolveDialogIntent(
        IntentClassification classification,
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents)
    {
        var dialog = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, AgentComponentCapabilityProfile.AgentDialogComponentId, StringComparison.OrdinalIgnoreCase));

        if (dialog is null)
        {
            return null;
        }

        var actionId = classification.PrimaryIntent.ToLowerInvariant() switch
        {
            "close_dialog" => AgentComponentCapabilityProfile.DialogCloseActionId,
            "confirm_dialog" => AgentComponentCapabilityProfile.DialogConfirmActionId,
            _ when userMessage.Contains("close", StringComparison.OrdinalIgnoreCase) => AgentComponentCapabilityProfile.DialogCloseActionId,
            _ when userMessage.Contains("confirm", StringComparison.OrdinalIgnoreCase) => AgentComponentCapabilityProfile.DialogConfirmActionId,
            _ => AgentComponentCapabilityProfile.DialogOpenActionId
        };

        var action = dialog.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            return null;
        }

        return new ResolvedIntent
        {
            Classification = classification,
            ComponentId = AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId = actionId,
            RequiresApproval = action.RequiresApproval,
            Reason = $"Dialog action: {actionId}"
        };
    }

    private static ResolvedIntent? TryResolveFormIntent(
        IntentClassification classification,
        string userMessage,
        IReadOnlyList<ComponentCapability> allowedComponents)
    {
        var form = allowedComponents.FirstOrDefault(c =>
            string.Equals(c.ComponentId, AgentComponentCapabilityProfile.AgentFormComponentId, StringComparison.OrdinalIgnoreCase));

        if (form is null)
        {
            return null;
        }

        var actionId = classification.PrimaryIntent.ToLowerInvariant() switch
        {
            "submit" => AgentComponentCapabilityProfile.FormSubmitActionId,
            "reset" => AgentComponentCapabilityProfile.FormResetActionId,
            "set_field" => AgentComponentCapabilityProfile.FormSetFieldActionId,
            _ when userMessage.Contains("submit", StringComparison.OrdinalIgnoreCase) => AgentComponentCapabilityProfile.FormSubmitActionId,
            _ when userMessage.Contains("reset", StringComparison.OrdinalIgnoreCase) => AgentComponentCapabilityProfile.FormResetActionId,
            _ when userMessage.Contains("validate", StringComparison.OrdinalIgnoreCase) => AgentComponentCapabilityProfile.FormValidateActionId,
            _ => AgentComponentCapabilityProfile.FormSetFieldActionId
        };

        var action = form.Actions.FirstOrDefault(a =>
            string.Equals(a.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            return null;
        }

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Extract field assignment if set_field
        if (string.Equals(actionId, AgentComponentCapabilityProfile.FormSetFieldActionId, StringComparison.OrdinalIgnoreCase))
        {
            if (classification.ExtractedEntities.TryGetValue("field", out var field))
                args["field"] = field;
            if (classification.ExtractedEntities.TryGetValue("value", out var value))
                args["value"] = value;

            // Try to extract from message pattern
            if (!args.ContainsKey("field") && TryExtractFieldAssignment(userMessage, out var extractedField, out var extractedValue))
            {
                args["field"] = extractedField;
                args["value"] = extractedValue;
            }
        }

        return new ResolvedIntent
        {
            Classification = classification,
            ComponentId = AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId = actionId,
            Arguments = args,
            RequiresApproval = action.RequiresApproval,
            Reason = $"Form action: {actionId}"
        };
    }

    private static Dictionary<string, object?> BuildArguments(
        IntentClassification classification,
        string userMessage,
        UserPreferences? preferences)
    {
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Copy extracted entities
        foreach (var (key, value) in classification.ExtractedEntities)
        {
            if (value is not null)
            {
                args[key] = value;
            }
        }

        return args;
    }

    public bool TryInferNavigationUri(string userMessage, out string uri)
    {
        uri = string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        // Try explicit URI match first
        var explicitUriMatch = Regex.Match(
            userMessage,
            @"https?://[^\s""']+|/[a-zA-Z0-9_/\-]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (explicitUriMatch.Success)
        {
            uri = explicitUriMatch.Value;
            return true;
        }

        // Check for home/dashboard keywords
        if (ContainsAny(userMessage, "home", "dashboard", "landing", "start page", "main page"))
        {
            uri = "/";
            return true;
        }

        // Try to extract route tokens
        if (!TryExtractRouteTokens(userMessage, out var routeTokens, out var hadEntityToken))
        {
            return false;
        }

        // Pluralize single tokens that look like entity names
        if (routeTokens.Length == 1 &&
            (hadEntityToken || ContainsAny(userMessage, "show me", "show all", "list", "all ")) &&
            !routeTokens[0].EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            routeTokens[0] = $"{routeTokens[0]}s";
        }

        uri = "/" + string.Join("-", routeTokens);

        // Try to improve with route registry
        var searchTerm = string.Join(" ", routeTokens);
        if (_routeRegistry.TryResolve(searchTerm, out var match) && match.Confidence > 0.5f)
        {
            uri = match.Path;
        }

        return true;
    }

    public bool TryExtractRouteTokens(string userMessage, out string[] routeTokens, out bool hadEntityToken)
    {
        routeTokens = [];
        hadEntityToken = false;

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var targetSegment = userMessage;

        // Extract target from navigation phrases
        var targetMatch = Regex.Match(
            userMessage,
            @"\b(?:go|navigate|open|show|take|bring)(?:\s+me)?(?:\s+to)?\s+(?<target>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (targetMatch.Success)
        {
            targetSegment = targetMatch.Groups["target"].Value;
        }

        // Trim at boundary words
        var boundaryMatch = Regex.Match(
            targetSegment,
            @"\b(and|then|with|where|that|which|filter|filtered|sort|sorted|by)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (boundaryMatch.Success && boundaryMatch.Index > 0)
        {
            targetSegment = targetSegment[..boundaryMatch.Index];
        }

        // Extract word candidates
        var candidates = Regex.Matches(
                targetSegment,
                @"[A-Za-z0-9][A-Za-z0-9_-]*",
                RegexOptions.CultureInvariant)
            .Select(m => m.Value.Trim())
            .Where(t => t.Length > 0)
            .ToArray();

        if (candidates.Length == 0)
        {
            return false;
        }

        var route = new List<string>();
        foreach (var token in candidates)
        {
            if (StopWords.Contains(token))
            {
                continue;
            }

            if (IsPotentialEntityKey(token))
            {
                hadEntityToken = true;
                continue;
            }

            var sanitized = new string(token.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray())
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

    public bool TryExtractEntityKeyToken(string text, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Match patterns like "SUP-123" or "ABC1234"
        var match = Regex.Match(
            text,
            @"\b[A-Za-z]{2,}[-_]\d{1,}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\b[A-Za-z]{1,}\d{3,}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!match.Success)
        {
            return false;
        }

        token = match.Value.Trim();
        return token.Length > 0;
    }

    public bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool IsPotentialEntityKey(string token)
        => Regex.IsMatch(
            token,
            @"^[A-Za-z]{2,}[-_]\d+$|^[A-Za-z]+\d{3,}$",
            RegexOptions.CultureInvariant);

    private static bool TryParseIndexFromMessage(string userMessage, out int index)
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
}
