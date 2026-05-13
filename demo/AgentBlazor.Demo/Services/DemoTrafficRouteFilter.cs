namespace AgentBlazor.Demo.Services;

internal static class DemoTrafficRouteFilter
{
    public static bool IsHumanPageViewRoute(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? "/" : path;

        if (path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWith("/.", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/wp-", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/wp/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/phpmyadmin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/pma", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/xmlrpc.php", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Path.HasExtension(path);
    }
}
