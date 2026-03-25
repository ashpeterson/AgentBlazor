using System.Reflection;
using System.Text;
using AgentBlazor.Attributes;

namespace AgentBlazor.App;

internal static class AgentCapabilityConventions
{
    public static string GetCapabilityId(Type capabilityType)
    {
        ArgumentNullException.ThrowIfNull(capabilityType);

        var attribute = capabilityType.GetCustomAttribute<AgentCapabilityAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{capabilityType.FullName}' must be annotated with [AgentCapability].");

        return attribute.CapabilityId ?? ToCapabilityId(capabilityType.Name);
    }

    public static IReadOnlyList<string> GetActionIds(Type capabilityType)
    {
        ArgumentNullException.ThrowIfNull(capabilityType);

        var capabilityId = GetCapabilityId(capabilityType);
        return capabilityType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.GetCustomAttribute<AgentActionAttribute>() is not null)
            .Select(method => $"{capabilityId}.{GetLocalActionId(method)}")
            .ToArray();
    }

    public static string GetLocalActionId(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var actionAttribute = method.GetCustomAttribute<AgentActionAttribute>()
            ?? throw new InvalidOperationException($"Method '{method.Name}' is missing [AgentAction].");

        return actionAttribute.ActionId ?? ToSnakeCase(method.Name);
    }

    private static string ToCapabilityId(string typeName)
    {
        var trimmed = typeName.EndsWith("Capabilities", StringComparison.Ordinal)
            ? typeName[..^"Capabilities".Length]
            : typeName;
        return ToSnakeCase(trimmed);
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
