using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Preferences;

/// <summary>
/// Routes user preference calls to in-memory or paid persistent providers
/// based on configured feature flags.
/// </summary>
internal sealed class FeatureGatedUserPreferenceService : IUserPreferenceService
{
    private readonly InMemoryUserPreferenceService _inMemoryService;
    private readonly IPersistentUserPreferenceService? _persistentService;
    private readonly IOptions<AgentBlazorOptions> _options;
    private readonly ILogger<FeatureGatedUserPreferenceService>? _logger;
    private int _missingProviderLogged;

    public FeatureGatedUserPreferenceService(
        InMemoryUserPreferenceService inMemoryService,
        IOptions<AgentBlazorOptions> options,
        IPersistentUserPreferenceService? persistentService = null,
        ILogger<FeatureGatedUserPreferenceService>? logger = null)
    {
        _inMemoryService = inMemoryService;
        _options = options;
        _persistentService = persistentService;
        _logger = logger;
    }

    public Task RecordActionAsync(
        string sessionId,
        string componentId,
        string actionId,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default) =>
        ResolveService().RecordActionAsync(sessionId, componentId, actionId, arguments, cancellationToken);

    public Task<UserPreferences> GetPreferencesAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        ResolveService().GetPreferencesAsync(sessionId, cancellationToken);

    public Task<UserPreferences> GetUserPreferencesAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        ResolveService().GetUserPreferencesAsync(userId, cancellationToken);

    public Task AssociateUserAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ResolveService().AssociateUserAsync(sessionId, userId, cancellationToken);

    public Task ClearSessionPreferencesAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        ResolveService().ClearSessionPreferencesAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<string>> GetSuggestedValuesAsync(
        string sessionId,
        string parameterName,
        int maxSuggestions = 5,
        CancellationToken cancellationToken = default) =>
        ResolveService().GetSuggestedValuesAsync(sessionId, parameterName, maxSuggestions, cancellationToken);

    private IUserPreferenceService ResolveService()
    {
        var paidFeatures = _options.Value.PaidFeatures;
        if (!paidFeatures.EnablePersistentMemory)
        {
            return _inMemoryService;
        }

        if (_persistentService is not null)
        {
            return _persistentService;
        }

        if (paidFeatures.RequirePersistentProviders)
        {
            throw new InvalidOperationException(
                "Persistent memory is enabled but no IPersistentUserPreferenceService is registered.");
        }

        if (Interlocked.Exchange(ref _missingProviderLogged, 1) == 0)
        {
            _logger?.LogWarning(
                "Persistent memory is enabled but no IPersistentUserPreferenceService is registered. Falling back to in-memory preference service.");
        }

        return _inMemoryService;
    }
}
