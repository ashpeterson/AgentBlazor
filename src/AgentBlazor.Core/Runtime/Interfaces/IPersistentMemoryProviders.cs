namespace AgentBlazor.Core.Runtime.Interfaces;

/// <summary>
/// Marker interface for paid persistent conversation storage.
/// Register this when persistent memory is available.
/// </summary>
public interface IPersistentConversationStore : IConversationStore;

/// <summary>
/// Marker interface for paid persistent user preference storage.
/// Register this when cross-session behavior learning is available.
/// </summary>
public interface IPersistentUserPreferenceService : IUserPreferenceService;
