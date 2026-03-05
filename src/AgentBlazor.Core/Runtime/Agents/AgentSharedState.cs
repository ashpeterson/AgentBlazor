namespace AgentBlazor.Core.Runtime.Agents;

/// <summary>
/// Canonical shared-state snapshot for a specific agent/session/run tuple.
/// </summary>
public sealed record AgentSharedStateSnapshot(
    string AgentName,
    string SessionId,
    string RunId,
    IReadOnlyDictionary<string, string> Values,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Shared-state delta where key/value entries with null represent removals.
/// </summary>
public sealed record AgentSharedStateDelta(
    string AgentName,
    string SessionId,
    string RunId,
    IReadOnlyDictionary<string, string?> Changes,
    DateTimeOffset UpdatedAt);
