namespace AgentBlazor.Agents;

public sealed class AgentRegistration
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? Instructions { get; init; }

    public IReadOnlySet<string> AllowedComponents { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> AllowedActions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> AllowedCapabilityActions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> ToolAssemblyNames { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
