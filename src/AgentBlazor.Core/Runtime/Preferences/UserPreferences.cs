namespace AgentBlazor.Core.Runtime.Preferences;

/// <summary>
/// Represents learned user preferences from past interactions.
/// </summary>
public sealed record UserPreferences
{
    /// <summary>
    /// Frequency of actions performed (key: "ComponentId.ActionId", value: count).
    /// </summary>
    public IReadOnlyDictionary<string, int> ActionFrequency { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Last used values for various parameters (key: parameter name, value: last used value).
    /// </summary>
    public IReadOnlyDictionary<string, string> LastUsedValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Most frequently visited routes, ordered by frequency.
    /// </summary>
    public IReadOnlyList<string> FrequentRoutes { get; init; } = [];

    /// <summary>
    /// Preferred sort columns per component (key: componentId, value: column name).
    /// </summary>
    public IReadOnlyDictionary<string, string> PreferredSortColumns { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Preferred sort directions per component (key: componentId, value: "asc" or "desc").
    /// </summary>
    public IReadOnlyDictionary<string, string> PreferredSortDirections { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When these preferences were last updated.
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates empty preferences.
    /// </summary>
    public static UserPreferences Empty => new();

    /// <summary>
    /// Gets the most frequently used action, if any.
    /// </summary>
    public string? MostFrequentAction => ActionFrequency
        .OrderByDescending(kvp => kvp.Value)
        .Select(kvp => kvp.Key)
        .FirstOrDefault();

    /// <summary>
    /// Gets the frequency of a specific action.
    /// </summary>
    public int GetActionFrequency(string componentId, string actionId)
    {
        var key = $"{componentId}.{actionId}";
        return ActionFrequency.TryGetValue(key, out var count) ? count : 0;
    }

    /// <summary>
    /// Gets the last used value for a parameter.
    /// </summary>
    public string? GetLastUsedValue(string parameterName) =>
        LastUsedValues.TryGetValue(parameterName, out var value) ? value : null;

    /// <summary>
    /// Gets the preferred sort column for a component.
    /// </summary>
    public string? GetPreferredSortColumn(string componentId) =>
        PreferredSortColumns.TryGetValue(componentId, out var column) ? column : null;

    /// <summary>
    /// Gets the preferred sort direction for a component.
    /// </summary>
    public string? GetPreferredSortDirection(string componentId) =>
        PreferredSortDirections.TryGetValue(componentId, out var direction) ? direction : null;
}

/// <summary>
/// Represents a single action record for preference tracking.
/// </summary>
internal sealed record ActionRecord
{
    /// <summary>
    /// The component ID.
    /// </summary>
    public required string ComponentId { get; init; }

    /// <summary>
    /// The action ID.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Arguments passed to the action.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Arguments { get; init; }

    /// <summary>
    /// When the action was performed.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the action succeeded.
    /// </summary>
    public bool Succeeded { get; init; } = true;
}
