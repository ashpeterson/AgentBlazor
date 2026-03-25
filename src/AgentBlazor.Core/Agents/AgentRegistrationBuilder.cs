using System.Reflection;

namespace AgentBlazor.Agents;

public sealed class AgentRegistrationBuilder
{
    private readonly HashSet<string> _allowedComponents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedCapabilityActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _toolAssemblyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    public AgentRegistrationBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public string? Description { get; private set; }

    public string? Instructions { get; private set; }

    public AgentRegistrationBuilder WithDescription(string description)
    {
        Description = description;
        return this;
    }

    public AgentRegistrationBuilder WithInstructions(string instructions)
    {
        Instructions = instructions;
        return this;
    }

    public AgentRegistrationBuilder WithAllowedComponents(params string[] componentIds)
    {
        foreach (var componentId in componentIds.Where(static c => !string.IsNullOrWhiteSpace(c)))
        {
            _allowedComponents.Add(componentId);
        }

        return this;
    }

    public AgentRegistrationBuilder WithAllowedActions(params string[] componentActionIds)
    {
        foreach (var componentActionId in componentActionIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            if (!TryParseActionKey(componentActionId, out var componentId, out var actionId))
            {
                throw new ArgumentException(
                    $"Invalid action policy '{componentActionId}'. Use 'ComponentId.ActionId' format.",
                    nameof(componentActionIds));
            }

            _allowedActions.Add(ToActionKey(componentId, actionId));
        }

        return this;
    }

    internal AgentRegistrationBuilder WithAllowedCapabilityActions(IEnumerable<string> capabilityActionIds)
    {
        ArgumentNullException.ThrowIfNull(capabilityActionIds);

        foreach (var capabilityActionId in capabilityActionIds.Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            _allowedCapabilityActions.Add(capabilityActionId.Trim());
        }

        return this;
    }

    public AgentRegistrationBuilder WithAllowedActions(params (string ComponentId, string ActionId)[] actions)
    {
        foreach (var (componentId, actionId) in actions)
        {
            if (string.IsNullOrWhiteSpace(componentId) || string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("ComponentId and ActionId are required.", nameof(actions));
            }

            _allowedActions.Add(ToActionKey(componentId, actionId));
        }

        return this;
    }

    public AgentRegistrationBuilder WithToolsFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _toolAssemblyNames.Add(assembly.GetName().Name ?? assembly.FullName ?? assembly.ToString());
        return this;
    }

    public AgentRegistrationBuilder WithMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _metadata[key] = value;
        return this;
    }

    public AgentRegistrationBuilder WithRoutePrefixes(params string[] routePrefixes)
    {
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_metadata.TryGetValue("route_prefixes", out var existing))
        {
            foreach (var prefix in existing.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                prefixes.Add(prefix);
            }
        }

        foreach (var prefix in routePrefixes.Where(static prefix => !string.IsNullOrWhiteSpace(prefix)))
        {
            prefixes.Add(prefix.Trim());
        }

        if (prefixes.Count > 0)
        {
            _metadata["route_prefixes"] = string.Join(',', prefixes);
        }

        return this;
    }

    internal AgentRegistration Build() => new()
    {
        Name = Name,
        Description = Description,
        Instructions = Instructions,
        AllowedComponents = new HashSet<string>(_allowedComponents, StringComparer.OrdinalIgnoreCase),
        AllowedActions = new HashSet<string>(_allowedActions, StringComparer.OrdinalIgnoreCase),
        AllowedCapabilityActions = new HashSet<string>(_allowedCapabilityActions, StringComparer.OrdinalIgnoreCase),
        ToolAssemblyNames = _toolAssemblyNames.ToArray(),
        Metadata = new Dictionary<string, string>(_metadata, StringComparer.OrdinalIgnoreCase)
    };

    private static string ToActionKey(string componentId, string actionId) =>
        $"{componentId.Trim()}.{actionId.Trim()}";

    private static bool TryParseActionKey(string value, out string componentId, out string actionId)
    {
        componentId = string.Empty;
        actionId = string.Empty;
        var separatorIndex = value.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        componentId = value[..separatorIndex].Trim();
        actionId = value[(separatorIndex + 1)..].Trim();
        return componentId.Length > 0 && actionId.Length > 0;
    }
}
