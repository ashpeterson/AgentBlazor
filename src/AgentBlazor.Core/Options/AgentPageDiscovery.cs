using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentBlazor.Options;

/// <summary>
/// Discovers agent pages by scanning an assembly for types with [Route] and a static
/// <c>AgentComponentIds</c> property. Used to populate <see cref="AgentBlazorOptions.UnmountedComponentRoutes"/>
/// without requiring app-level configuration of component IDs or routes.
/// </summary>
public static class AgentPageDiscovery
{
    /// <summary>
    /// Name of the static property that page components can declare to indicate which
    /// agent component IDs are hosted on that route. Type must be <c>string[]</c> or
    /// <c>IReadOnlyList&lt;string&gt;</c>.
    /// </summary>
    public const string AgentComponentIdsPropertyName = "AgentComponentIds";

    /// <summary>
    /// Scans the assembly for types that have a [Route] attribute and a static property
    /// <see cref="AgentComponentIdsPropertyName"/>. For each such type, adds entries to
    /// <paramref name="unmountedComponentRoutes"/> (componentId -> route). Does not clear
    /// existing entries; later discoveries overwrite for the same component ID.
    /// </summary>
    public static void DiscoverAgentPages(Assembly assembly, IDictionary<string, string> unmountedComponentRoutes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(unmountedComponentRoutes);

        foreach (var type in assembly.GetExportedTypes())
        {
            var route = GetRouteFromType(type);
            if (string.IsNullOrWhiteSpace(route))
                continue;

            var componentIds = GetAgentComponentIdsFromType(type);
            if (componentIds is null || componentIds.Count == 0)
                continue;

            foreach (var id in componentIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    unmountedComponentRoutes[id] = route;
            }
        }
    }

    private static string? GetRouteFromType(Type type)
    {
        var routeAttributes = type.GetCustomAttributes()
            .Where(a => a.GetType().Name == "RouteAttribute")
            .ToArray();

        foreach (var routeAttr in routeAttributes)
        {
            var templateProperty = routeAttr.GetType().GetProperty("Template");
            var template = templateProperty?.GetValue(routeAttr) as string;
            if (!string.IsNullOrWhiteSpace(template))
                return NormalizeRoute(template);
        }

        return null;
    }

    private static IReadOnlyList<string>? GetAgentComponentIdsFromType(Type type)
    {
        var prop = type.GetProperty(AgentComponentIdsPropertyName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (prop is null)
            return null;

        if (!prop.CanRead || prop.GetMethod?.IsStatic != true)
            return null;

        var value = prop.GetValue(null);
        if (value is string[] arr)
            return arr;
        if (value is IReadOnlyList<string> list)
            return list;
        if (value is IEnumerable<string> seq)
            return seq.ToList();

        return null;
    }

    private static string NormalizeRoute(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        path = path.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        path = Regex.Replace(path, @"\{[^}]+\}", "");
        path = Regex.Replace(path, @"//+", "/");
        return path.TrimEnd('/');
    }
}
