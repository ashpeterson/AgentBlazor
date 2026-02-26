using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Conversation;

/// <summary>
/// Routes conversation storage calls to in-memory or paid persistent providers
/// based on configured feature flags.
/// </summary>
internal sealed class FeatureGatedConversationStore : IConversationStore
{
    private readonly InMemoryConversationStore _inMemoryStore;
    private readonly IPersistentConversationStore? _persistentStore;
    private readonly IOptions<AgentBlazorOptions> _options;
    private readonly ILogger<FeatureGatedConversationStore>? _logger;
    private int _missingProviderLogged;

    public FeatureGatedConversationStore(
        InMemoryConversationStore inMemoryStore,
        IOptions<AgentBlazorOptions> options,
        IPersistentConversationStore? persistentStore = null,
        ILogger<FeatureGatedConversationStore>? logger = null)
    {
        _inMemoryStore = inMemoryStore;
        _options = options;
        _persistentStore = persistentStore;
        _logger = logger;
    }

    public Task<ConversationHistory?> GetHistoryAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        ResolveStore().GetHistoryAsync(sessionId, cancellationToken);

    public Task AppendTurnAsync(
        string sessionId,
        ConversationTurn turn,
        CancellationToken cancellationToken = default) =>
        ResolveStore().AppendTurnAsync(sessionId, turn, cancellationToken);

    public Task ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        ResolveStore().ClearSessionAsync(sessionId, cancellationToken);

    public Task<IReadOnlyCollection<string>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default) =>
        ResolveStore().GetActiveSessionsAsync(cancellationToken);

    public Task SetUserIdAsync(
        string sessionId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ResolveStore().SetUserIdAsync(sessionId, userId, cancellationToken);

    public Task<IReadOnlyCollection<string>> GetSessionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        ResolveStore().GetSessionsForUserAsync(userId, cancellationToken);

    private IConversationStore ResolveStore()
    {
        var paidFeatures = _options.Value.PaidFeatures;
        if (!paidFeatures.EnablePersistentMemory)
        {
            return _inMemoryStore;
        }

        if (_persistentStore is not null)
        {
            return _persistentStore;
        }

        if (paidFeatures.RequirePersistentProviders)
        {
            throw new InvalidOperationException(
                "Persistent memory is enabled but no IPersistentConversationStore is registered.");
        }

        if (Interlocked.Exchange(ref _missingProviderLogged, 1) == 0)
        {
            _logger?.LogWarning(
                "Persistent memory is enabled but no IPersistentConversationStore is registered. Falling back to in-memory conversation store.");
        }

        return _inMemoryStore;
    }
}
