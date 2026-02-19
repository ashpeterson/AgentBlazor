using System.Globalization;
using System.Text.Json;

namespace AgentBlazor.Runtime;

public readonly record struct RegisteredComponentSnapshot(
    string AgentId,
    string ComponentType,
    string[] Actions,
    IReadOnlyDictionary<string, string> State);

public static class RegisteredComponentSnapshotBuilder
{
    public static IReadOnlyList<RegisteredComponentSnapshot> Build(IAgentComponentRegistry? componentRegistry)
    {
        if (componentRegistry is null)
        {
            return [];
        }

        var snapshots = new List<RegisteredComponentSnapshot>();
        foreach (var component in componentRegistry.GetAll()
                     .OrderBy(static c => c.AgentId, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var capability = component.GetCapability();
                var state = component.GetCurrentState();
                var actions = capability.Actions
                    .Select(static action => action.ActionId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var stateSnapshot = state
                    .OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static kvp => kvp.Key,
                        static kvp => FormatStateValue(kvp.Value),
                        StringComparer.OrdinalIgnoreCase);

                snapshots.Add(new RegisteredComponentSnapshot(
                    component.AgentId,
                    component.ComponentType,
                    actions,
                    stateSnapshot));
            }
            catch
            {
                // Keep runtime/hosting resilient to component-level snapshot faults.
            }
        }

        return snapshots;
    }

    private static string FormatStateValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string s)
        {
            return s;
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };
    }
}
