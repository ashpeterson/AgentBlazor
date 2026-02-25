using AgentBlazor.Components;

namespace AgentBlazor.Core.Runtime.Intent;

/// <summary>
/// Defines a rule for matching user intent based on keywords.
/// </summary>
internal sealed class IntentRule
{
    /// <summary>
    /// The intent name this rule matches (e.g., "navigate", "filter", "sort").
    /// </summary>
    public required string IntentName { get; init; }

    /// <summary>
    /// Target component ID for this intent.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// Target action ID for this intent.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Keywords that trigger this intent (case-insensitive).
    /// </summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>
    /// Keywords that exclude this intent when present.
    /// </summary>
    public IReadOnlyList<string> ExcludeKeywords { get; init; } = [];

    /// <summary>
    /// Base confidence when keywords match (0.0-1.0).
    /// </summary>
    public float BaseConfidence { get; init; } = 0.7f;

    /// <summary>
    /// Priority when multiple rules match (higher = preferred).
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Entity extraction patterns for this intent.
    /// </summary>
    public IReadOnlyList<EntityExtractionPattern> EntityPatterns { get; init; } = [];

    /// <summary>
    /// Checks if this rule matches the given message.
    /// </summary>
    public bool Matches(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        // Check for exclusions first
        foreach (var exclude in ExcludeKeywords)
        {
            if (message.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check for any matching keyword
        foreach (var keyword in Keywords)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates confidence based on keyword matches.
    /// </summary>
    public float CalculateConfidence(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0f;

        var matchCount = 0;
        foreach (var keyword in Keywords)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                matchCount++;
        }

        if (matchCount == 0)
            return 0f;

        // More keyword matches = higher confidence, up to BaseConfidence + 0.2
        var bonus = Math.Min(0.2f, (matchCount - 1) * 0.05f);
        return Math.Min(1.0f, BaseConfidence + bonus);
    }

    /// <summary>
    /// Creates the default intent rules based on AgentBlazor's component capabilities.
    /// </summary>
    public static IReadOnlyList<IntentRule> CreateDefaultRules() =>
    [
        // Navigation intents
        new IntentRule
        {
            IntentName = "navigate",
            ComponentId = AgentComponentCapabilityProfile.AgentNavMenuComponentId,
            ActionId = AgentComponentCapabilityProfile.NavigationNavigateToActionId,
            Keywords = ["go to", "navigate to", "open", "show me", "take me to", "bring me to", "go", "navigate", "switch to"],
            ExcludeKeywords = ["dialog", "tab"],
            BaseConfidence = 0.75f,
            Priority = 10,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "uri",
                    Pattern = @"(?:go\s+to|navigate\s+to|open|show\s+me)\s+(?:the\s+)?([a-zA-Z0-9_/-]+)",
                    GroupIndex = 1
                }
            ]
        },

        // Filter intents
        new IntentRule
        {
            IntentName = "filter",
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridFilterActionId,
            Keywords = ["filter", "show only", "only show", "where", "with", "having", "find", "search for"],
            BaseConfidence = 0.75f,
            Priority = 8,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "column",
                    Pattern = @"(?:filter|sort)\s+(?:by\s+)?([a-zA-Z][a-zA-Z0-9_]*)",
                    GroupIndex = 1
                },
                new EntityExtractionPattern
                {
                    EntityName = "value",
                    Pattern = @"(?:>=|<=|>|<|=|equals?|contains?|like)\s*[""']?([^""'\s]+)[""']?",
                    GroupIndex = 1
                }
            ]
        },

        // Sort intents
        new IntentRule
        {
            IntentName = "sort",
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridSortActionId,
            Keywords = ["sort", "order by", "arrange", "ascending", "descending", "highest to lowest", "lowest to highest", "asc", "desc"],
            BaseConfidence = 0.8f,
            Priority = 7,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "direction",
                    Pattern = @"(ascending|descending|asc|desc|highest\s+to\s+lowest|lowest\s+to\s+highest)",
                    GroupIndex = 1
                }
            ]
        },

        // Tab switching intents
        new IntentRule
        {
            IntentName = "switch_tab",
            ComponentId = AgentComponentCapabilityProfile.AgentTabsComponentId,
            ActionId = AgentComponentCapabilityProfile.TabsSwitchTabActionId,
            Keywords = ["switch tab", "tab", "first tab", "second tab", "third tab", "next tab", "previous tab"],
            BaseConfidence = 0.8f,
            Priority = 6,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "index",
                    Pattern = @"(first|second|third|fourth|fifth|\d+)(?:\s+tab)?",
                    GroupIndex = 1
                }
            ]
        },

        // Dialog intents
        new IntentRule
        {
            IntentName = "open_dialog",
            ComponentId = AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId = AgentComponentCapabilityProfile.DialogOpenActionId,
            Keywords = ["open dialog", "show dialog", "open modal", "show modal", "popup"],
            BaseConfidence = 0.85f,
            Priority = 5
        },
        new IntentRule
        {
            IntentName = "close_dialog",
            ComponentId = AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId = AgentComponentCapabilityProfile.DialogCloseActionId,
            Keywords = ["close dialog", "close modal", "dismiss", "cancel dialog"],
            BaseConfidence = 0.85f,
            Priority = 5
        },
        new IntentRule
        {
            IntentName = "confirm_dialog",
            ComponentId = AgentComponentCapabilityProfile.AgentDialogComponentId,
            ActionId = AgentComponentCapabilityProfile.DialogConfirmActionId,
            Keywords = ["confirm", "confirm dialog", "ok", "yes", "proceed", "submit dialog"],
            ExcludeKeywords = ["form"],
            BaseConfidence = 0.7f,
            Priority = 4
        },

        // Form intents
        new IntentRule
        {
            IntentName = "set_field",
            ComponentId = AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId = AgentComponentCapabilityProfile.FormSetFieldActionId,
            Keywords = ["set field", "update field", "change field", "set", "update", "change", "fill in", "enter"],
            BaseConfidence = 0.7f,
            Priority = 3,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "field",
                    Pattern = @"(?:set|update|change)\s+([a-zA-Z][a-zA-Z0-9_\s]*?)\s+(?:to|as|=)",
                    GroupIndex = 1
                },
                new EntityExtractionPattern
                {
                    EntityName = "value",
                    Pattern = @"(?:to|as|=)\s*[""']?([^""'\n]+)[""']?$",
                    GroupIndex = 1
                }
            ]
        },
        new IntentRule
        {
            IntentName = "submit_form",
            ComponentId = AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId = AgentComponentCapabilityProfile.FormSubmitActionId,
            Keywords = ["submit form", "submit", "save form", "save"],
            ExcludeKeywords = ["dialog"],
            BaseConfidence = 0.8f,
            Priority = 5
        },
        new IntentRule
        {
            IntentName = "validate_form",
            ComponentId = AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId = AgentComponentCapabilityProfile.FormValidateActionId,
            Keywords = ["validate form", "validate", "check form", "verify form"],
            BaseConfidence = 0.8f,
            Priority = 4
        },
        new IntentRule
        {
            IntentName = "reset_form",
            ComponentId = AgentComponentCapabilityProfile.AgentFormComponentId,
            ActionId = AgentComponentCapabilityProfile.FormResetActionId,
            Keywords = ["reset form", "clear form", "reset", "clear fields"],
            BaseConfidence = 0.8f,
            Priority = 4
        },

        // DataGrid pagination
        new IntentRule
        {
            IntentName = "go_to_page",
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridGoToPageActionId,
            Keywords = ["page", "go to page", "next page", "previous page", "first page", "last page"],
            BaseConfidence = 0.8f,
            Priority = 3,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "pageIndex",
                    Pattern = @"(?:page\s+)?(\d+)",
                    GroupIndex = 1
                }
            ]
        },

        // DataGrid row selection
        new IntentRule
        {
            IntentName = "select_row",
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridSelectRowActionId,
            Keywords = ["select", "select row", "click on", "choose"],
            BaseConfidence = 0.7f,
            Priority = 2,
            EntityPatterns =
            [
                new EntityExtractionPattern
                {
                    EntityName = "rowKey",
                    Pattern = @"(?:select|choose)\s+([A-Za-z]{2,}[-_]\d+|[A-Za-z]+\d{3,})",
                    GroupIndex = 1
                }
            ]
        },

        // Clear filters
        new IntentRule
        {
            IntentName = "clear_filters",
            ComponentId = AgentComponentCapabilityProfile.AgentDataGridComponentId,
            ActionId = AgentComponentCapabilityProfile.DataGridClearFiltersActionId,
            Keywords = ["clear filter", "clear filters", "remove filter", "reset filter", "show all"],
            BaseConfidence = 0.85f,
            Priority = 6
        }
    ];
}

/// <summary>
/// Pattern for extracting entities from user messages.
/// </summary>
internal sealed class EntityExtractionPattern
{
    /// <summary>
    /// The name of the entity to extract (e.g., "column", "value", "uri").
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Regex pattern for extraction.
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Which capture group contains the value (1-based).
    /// </summary>
    public int GroupIndex { get; init; } = 1;
}
