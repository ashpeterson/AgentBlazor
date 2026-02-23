using System.Collections.Concurrent;
using AgentBlazor.Components;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime.Preferences;

/// <summary>
/// In-memory implementation of IUserPreferenceService.
/// </summary>
internal sealed class InMemoryUserPreferenceService : IUserPreferenceService
{
    private readonly ConcurrentDictionary<string, SessionPreferenceData> _sessionData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<string>> _userSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxActionsPerSession;
    private readonly TimeSpan _decayPeriod;

    public InMemoryUserPreferenceService(
        int maxActionsPerSession = 1000,
        TimeSpan? decayPeriod = null)
    {
        _maxActionsPerSession = maxActionsPerSession;
        _decayPeriod = decayPeriod ?? TimeSpan.FromDays(7);
    }

    public Task RecordActionAsync(
        string sessionId,
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var data = _sessionData.GetOrAdd(sessionId, _ => new SessionPreferenceData());
        var record = new ActionRecord
        {
            ComponentId = componentId,
            ActionId = actionId,
            Arguments = arguments,
            Timestamp = DateTime.UtcNow
        };

        lock (data.Lock)
        {
            data.Actions.Add(record);

            // Trim old actions
            if (data.Actions.Count > _maxActionsPerSession)
            {
                data.Actions.RemoveRange(0, data.Actions.Count - _maxActionsPerSession);
            }

            // Update action frequency
            var actionKey = $"{componentId}.{actionId}";
            data.ActionFrequency.TryGetValue(actionKey, out var count);
            data.ActionFrequency[actionKey] = count + 1;

            // Track parameter values
            if (arguments is not null)
            {
                foreach (var (key, value) in arguments)
                {
                    if (value is null)
                        continue;

                    var valueStr = value.ToString();
                    if (string.IsNullOrWhiteSpace(valueStr))
                        continue;

                    // Track last used value
                    data.LastUsedValues[key] = valueStr;

                    // Track value frequency
                    var valueKey = $"{key}:{valueStr}";
                    data.ValueFrequency.TryGetValue(valueKey, out var valueCount);
                    data.ValueFrequency[valueKey] = valueCount + 1;

                    // Track sort preferences
                    if (string.Equals(actionId, AgentComponentCapabilityProfile.DataGridSortActionId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(key, "column", StringComparison.OrdinalIgnoreCase))
                        {
                            data.PreferredSortColumns[componentId] = valueStr;
                        }
                        else if (string.Equals(key, "direction", StringComparison.OrdinalIgnoreCase))
                        {
                            data.PreferredSortDirections[componentId] = valueStr;
                        }
                    }

                    // Track route visits
                    if (string.Equals(actionId, AgentComponentCapabilityProfile.NavigationNavigateToActionId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(key, "uri", StringComparison.OrdinalIgnoreCase))
                    {
                        data.RouteVisits.TryGetValue(valueStr, out var routeCount);
                        data.RouteVisits[valueStr] = routeCount + 1;
                    }
                }
            }

            data.LastUpdated = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task<UserPreferences> GetPreferencesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_sessionData.TryGetValue(sessionId, out var data))
        {
            return Task.FromResult(UserPreferences.Empty);
        }

        return Task.FromResult(BuildPreferences(data));
    }

    public Task<UserPreferences> GetUserPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_userSessions.TryGetValue(userId, out var sessionIds))
        {
            return Task.FromResult(UserPreferences.Empty);
        }

        // Aggregate across all user sessions
        var aggregated = new SessionPreferenceData();

        foreach (var sessionId in sessionIds)
        {
            if (!_sessionData.TryGetValue(sessionId, out var sessionData))
                continue;

            lock (sessionData.Lock)
            {
                foreach (var (key, count) in sessionData.ActionFrequency)
                {
                    aggregated.ActionFrequency.TryGetValue(key, out var existing);
                    aggregated.ActionFrequency[key] = existing + count;
                }

                foreach (var (key, value) in sessionData.LastUsedValues)
                {
                    aggregated.LastUsedValues[key] = value;
                }

                foreach (var (key, count) in sessionData.RouteVisits)
                {
                    aggregated.RouteVisits.TryGetValue(key, out var existing);
                    aggregated.RouteVisits[key] = existing + count;
                }

                foreach (var (key, value) in sessionData.PreferredSortColumns)
                {
                    aggregated.PreferredSortColumns[key] = value;
                }

                foreach (var (key, value) in sessionData.PreferredSortDirections)
                {
                    aggregated.PreferredSortDirections[key] = value;
                }
            }
        }

        return Task.FromResult(BuildPreferences(aggregated));
    }

    public Task AssociateUserAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        _userSessions.AddOrUpdate(
            userId,
            _ => [sessionId],
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(sessionId);
                }
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task ClearSessionPreferencesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionData.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetSuggestedValuesAsync(
        string sessionId,
        string parameterName,
        int maxSuggestions = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (!_sessionData.TryGetValue(sessionId, out var data))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var prefix = $"{parameterName}:";
        var suggestions = data.ValueFrequency
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Value)
            .Take(maxSuggestions)
            .Select(kvp => kvp.Key[prefix.Length..])
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(suggestions);
    }

    private static UserPreferences BuildPreferences(SessionPreferenceData data)
    {
        lock (data.Lock)
        {
            var frequentRoutes = data.RouteVisits
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => kvp.Key)
                .ToArray();

            return new UserPreferences
            {
                ActionFrequency = new Dictionary<string, int>(data.ActionFrequency, StringComparer.OrdinalIgnoreCase),
                LastUsedValues = new Dictionary<string, string>(data.LastUsedValues, StringComparer.OrdinalIgnoreCase),
                FrequentRoutes = frequentRoutes,
                PreferredSortColumns = new Dictionary<string, string>(data.PreferredSortColumns, StringComparer.OrdinalIgnoreCase),
                PreferredSortDirections = new Dictionary<string, string>(data.PreferredSortDirections, StringComparer.OrdinalIgnoreCase),
                LastUpdated = data.LastUpdated
            };
        }
    }

    private sealed class SessionPreferenceData
    {
        public object Lock { get; } = new();
        public List<ActionRecord> Actions { get; } = [];
        public Dictionary<string, int> ActionFrequency { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LastUsedValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ValueFrequency { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RouteVisits { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PreferredSortColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PreferredSortDirections { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
